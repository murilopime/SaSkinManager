using SaSkinManager.Core.Formats;
using SaSkinManager.Core.Game;
using SaSkinManager.Core.Library;

namespace SaSkinManager.Core.Swap;

public sealed class SwapException(string message) : Exception(message);

/// <summary>
/// Aplica e desfaz trocas. Dois modos:
/// <list type="bullet">
/// <item>Modloader: grava cópias renomeadas em modloader\SaSkinManager\&lt;modelo&gt;\ (não mexe nos .img).</item>
/// <item>IMG: substitui as entradas dentro do gta3.img/player.img, com backup completo e por entrada.</item>
/// </list>
/// </summary>
public sealed class SwapService
{
    public const string ModloaderFolderName = "SaSkinManager";

    private readonly GameInstall _game;
    private readonly SkinLibrary _library;
    private readonly string _backupDir;

    /// <summary>Permite trocar a checagem de "jogo aberto" nos testes.</summary>
    public Func<bool> IsGameRunning { get; set; } = GameInstall.IsGameRunning;

    public SwapService(GameInstall game, SkinLibrary library, AppPaths paths)
    {
        _game = game;
        _library = library;
        var dataDir = paths.GameDataDir(game.Root);
        _backupDir = Path.Combine(dataDir, "backups");
        Manifest = new SwapManifest(Path.Combine(dataDir, "manifest.json"));
    }

    public SwapManifest Manifest { get; }

    public SwapMode Resolve(SwapMode preferred) => preferred switch
    {
        SwapMode.Auto => _game.HasModloader ? SwapMode.Modloader : SwapMode.Img,
        _ => preferred,
    };

    public SwapRecord? FindFor(GameModel target) => Manifest.Find(target.Key);

    /// <summary>Monta "nome no jogo" → bytes para a troca.</summary>
    internal Dictionary<string, byte[]> BuildFiles(GameModel target, LibrarySkin skin)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (target.Kind == ModelKind.Player)
        {
            if (skin.Kind != SkinKind.CjPack)
                throw new SwapException(
                    "Para trocar o CJ é preciso um pacote de peças do player.img (importe com \"Importar pacote do CJ\"). " +
                    "Um modelo de personagem comum (um só .dff) não substitui o CJ diretamente; para isso use um mod como o Skin Selector (CLEO).");
            foreach (var f in skin.Files) files[f] = _library.ReadFile(skin, f);
        }
        else
        {
            if (skin.Kind != SkinKind.Ped || skin.DffFile is null || skin.TxdFile is null)
                throw new SwapException("Esta skin precisa ter um .dff e um .txd.");
            files[target.ModelName + ".dff"] = _library.ReadFile(skin, skin.DffFile);
            files[target.TxdName + ".txd"] = _library.ReadFile(skin, skin.TxdFile);
        }
        return files;
    }

    public SwapRecord Apply(GameModel target, LibrarySkin skin, SwapMode preferred)
    {
        var mode = Resolve(preferred);
        if (mode == SwapMode.Modloader && !_game.HasModloader)
            throw new SwapException("Mod Loader não está instalado neste jogo (modloader.asi + pasta modloader).");
        if (mode == SwapMode.Img && IsGameRunning())
            throw new SwapException("Feche o GTA San Andreas antes de alterar os arquivos .img.");

        var files = BuildFiles(target, skin);

        if (FindFor(target) is { } existing) Revert(existing);

        SwapRecord record;
        if (mode == SwapMode.Modloader)
        {
            var dir = Path.Combine(_game.ModloaderDir, ModloaderFolderName,
                target.Kind == ModelKind.Player ? Path.Combine("cj", "player.img") : target.ModelName.ToLowerInvariant());
            Directory.CreateDirectory(dir);
            foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(dir, name), data);
            record = NewRecord(target, skin, mode, files.Keys, dir);
        }
        else
        {
            var imgPath = _game.ImgPath(target.ImgName);
            if (!File.Exists(imgPath)) throw new SwapException($"{target.ImgName} não encontrado.");
            EnsureFullBackup(imgPath);
            using var img = ImgArchive.Open(imgPath, writable: true);
            var missing = files.Keys.Where(n => !img.Contains(n)).ToList();
            if (missing.Count > 0)
                throw new SwapException($"Estes arquivos não existem em {target.ImgName}: {string.Join(", ", missing)}. " +
                                        "No modo IMG só dá para substituir arquivos que já existem.");
            foreach (var (name, data) in files)
            {
                BackupEntry(img, target.ImgName, name);
                img.Replace(name, data);
            }
            record = NewRecord(target, skin, mode, files.Keys, null);
        }

        Manifest.Add(record);
        return record;
    }

    private static SwapRecord NewRecord(GameModel t, LibrarySkin s, SwapMode mode, IEnumerable<string> files, string? dir) => new()
    {
        TargetKey = t.Key,
        TargetModel = t.ModelName,
        TargetName = t.DisplayName,
        TargetKind = t.Kind,
        ImgName = t.ImgName,
        SkinId = s.Id,
        SkinName = s.Name,
        Mode = mode,
        Files = files.ToList(),
        ModloaderDir = dir,
        AppliedAt = DateTime.Now,
    };

    public void Revert(SwapRecord r)
    {
        if (r.Mode == SwapMode.Modloader)
        {
            if (r.ModloaderDir is { } dir && Directory.Exists(dir))
            {
                foreach (var f in r.Files)
                {
                    var p = Path.Combine(dir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                DeleteEmptyUpTo(dir, Path.Combine(_game.ModloaderDir, ModloaderFolderName));
            }
        }
        else
        {
            if (IsGameRunning()) throw new SwapException("Feche o GTA San Andreas antes de alterar os arquivos .img.");
            using var img = ImgArchive.Open(_game.ImgPath(r.ImgName), writable: true);
            foreach (var f in r.Files)
            {
                var backup = EntryBackupPath(r.ImgName, f);
                if (!File.Exists(backup))
                    throw new SwapException($"Backup de {f} não encontrado. Use o backup completo {r.ImgName}.skinmanager.bak.");
                img.Replace(f, File.ReadAllBytes(backup));
            }
        }
        Manifest.Remove(r);
    }

    public void RevertAll()
    {
        foreach (var r in Manifest.Records.ToList()) Revert(r);
    }

    private string EntryBackupPath(string imgName, string entry) =>
        Path.Combine(_backupDir, imgName, entry.ToLowerInvariant());

    /// <summary>Guarda a entrada original só na primeira vez (para o desfazer sempre voltar ao original).</summary>
    private void BackupEntry(ImgArchive img, string imgName, string entry)
    {
        var path = EntryBackupPath(imgName, entry);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, img.Read(img.Find(entry)!));
    }

    public static string FullBackupPath(string imgPath) => imgPath + ".skinmanager.bak";

    private static void EnsureFullBackup(string imgPath)
    {
        var bak = FullBackupPath(imgPath);
        if (File.Exists(bak)) return;
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(imgPath))!);
        long size = new FileInfo(imgPath).Length;
        if (drive.AvailableFreeSpace < size + 100L * 1024 * 1024)
            throw new SwapException($"Sem espaço em disco para o backup de {Path.GetFileName(imgPath)} ({size / 1024 / 1024} MB).");
        File.Copy(imgPath, bak + ".tmp", true);
        File.Move(bak + ".tmp", bak);
    }

    private static void DeleteEmptyUpTo(string dir, string stopAt)
    {
        var stop = Path.GetFullPath(stopAt).TrimEnd('\\');
        var d = new DirectoryInfo(dir);
        while (d is not null && d.Exists && !d.EnumerateFileSystemInfos().Any())
        {
            var parent = d.Parent;
            d.Delete();
            if (d.FullName.TrimEnd('\\').Equals(stop, StringComparison.OrdinalIgnoreCase)) break;
            d = parent;
        }
    }
}
