using System.IO.Compression;
using System.Text.Json;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.Core.Library;

public enum SkinKind
{
    /// <summary>Um par .dff + .txd que substitui um personagem.</summary>
    Ped,
    /// <summary>Pacote de peças do player.img (troca do CJ). Os arquivos mantêm os nomes originais.</summary>
    CjPack,
}

public sealed class LibrarySkin
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public SkinKind Kind { get; init; }
    public List<string> Files { get; init; } = new();
    public string Source { get; init; } = "";
    public DateTime ImportedAt { get; init; }

    public string? DffFile => Files.FirstOrDefault(f => f.EndsWith(".dff", StringComparison.OrdinalIgnoreCase));
    public string? TxdFile => Files.FirstOrDefault(f => f.EndsWith(".txd", StringComparison.OrdinalIgnoreCase));
}

public sealed record ImportResult(List<LibrarySkin> Imported, List<string> Warnings);

/// <summary>Biblioteca de skins importadas, em %AppData%\SaSkinManager\library\&lt;id&gt;\.</summary>
public sealed class SkinLibrary
{
    private readonly string _dir;
    private readonly List<LibrarySkin> _skins = new();

    public SkinLibrary(AppPaths paths)
    {
        _dir = paths.LibraryDir;
        Directory.CreateDirectory(_dir);
        Reload();
    }

    public IReadOnlyList<LibrarySkin> Skins => _skins;

    public string SkinDir(LibrarySkin s) => Path.Combine(_dir, s.Id);
    public string FilePath(LibrarySkin s, string file) => Path.Combine(SkinDir(s), file);
    public string ThumbPath(LibrarySkin s) => Path.Combine(SkinDir(s), "thumb.png");

    public void Reload()
    {
        _skins.Clear();
        foreach (var d in Directory.EnumerateDirectories(_dir))
        {
            var meta = Path.Combine(d, "skin.json");
            if (!File.Exists(meta)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<LibrarySkin>(File.ReadAllText(meta));
                if (s is not null) _skins.Add(s);
            }
            catch (JsonException) { }
        }
        _skins.Sort((a, b) => b.ImportedAt.CompareTo(a.ImportedAt));
    }

    /// <summary>
    /// Importa arquivos soltos, pastas ou .zip. Normalmente cria uma skin por par .dff/.txd com o mesmo nome;
    /// com <paramref name="asCjPack"/> tudo vira um único pacote de peças do CJ.
    /// </summary>
    public ImportResult Import(IEnumerable<string> paths, bool asCjPack = false)
    {
        var warnings = new List<string>();
        var files = new List<(string Name, byte[] Data)>();
        string? sourceName = null;

        foreach (var p in paths)
        {
            sourceName ??= Path.GetFileNameWithoutExtension(p.TrimEnd('\\', '/'));
            if (Directory.Exists(p))
            {
                foreach (var f in Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories))
                    if (IsModelFile(f)) files.Add((Path.GetFileName(f), File.ReadAllBytes(f)));
            }
            else if (p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(p);
                foreach (var e in zip.Entries)
                {
                    // só o nome do arquivo, nunca o caminho dentro do zip
                    var name = Path.GetFileName(e.FullName);
                    if (!IsModelFile(name) || e.Length > 64 * 1024 * 1024) continue;
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    files.Add((name, ms.ToArray()));
                }
            }
            else if (File.Exists(p) && IsModelFile(p))
            {
                files.Add((Path.GetFileName(p), File.ReadAllBytes(p)));
            }
        }

        // validação básica do formato RenderWare
        files = files.Where(f =>
        {
            bool ok = f.Name.EndsWith(".dff", StringComparison.OrdinalIgnoreCase) ? RwChunk.IsDff(f.Data) : RwChunk.IsTxd(f.Data);
            if (!ok) warnings.Add($"{f.Name}: não parece um arquivo RenderWare válido, ignorado.");
            return ok;
        }).ToList();

        // remove duplicados (mesmo nome)
        files = files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        var imported = new List<LibrarySkin>();
        if (files.Count == 0)
        {
            warnings.Add("Nenhum arquivo .dff/.txd encontrado.");
            return new ImportResult(imported, warnings);
        }

        if (asCjPack)
        {
            imported.Add(Save(sourceName ?? "Pacote CJ", SkinKind.CjPack, files, sourceName ?? ""));
            return new ImportResult(imported, warnings);
        }

        var dffs = files.Where(f => f.Name.EndsWith(".dff", StringComparison.OrdinalIgnoreCase)).ToList();
        var txds = files.Where(f => f.Name.EndsWith(".txd", StringComparison.OrdinalIgnoreCase)).ToList();
        var usedTxd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dff in dffs)
        {
            var baseName = Path.GetFileNameWithoutExtension(dff.Name);
            var txd = txds.FirstOrDefault(t => Path.GetFileNameWithoutExtension(t.Name).Equals(baseName, StringComparison.OrdinalIgnoreCase));
            if (txd.Name is null && dffs.Count == 1 && txds.Count == 1) txd = txds[0];
            if (txd.Name is null)
            {
                warnings.Add($"{dff.Name}: sem .txd correspondente, ignorado.");
                continue;
            }
            usedTxd.Add(txd.Name);
            string name = dffs.Count == 1 && sourceName is not null ? sourceName : baseName;
            imported.Add(Save(name, SkinKind.Ped, new() { dff, txd }, sourceName ?? ""));
        }
        foreach (var t in txds.Where(t => !usedTxd.Contains(t.Name)))
            warnings.Add($"{t.Name}: sem .dff correspondente, ignorado.");
        if (dffs.Count > 1 && imported.Count < dffs.Count)
            warnings.Add("Dica: se for um pacote de roupas do CJ, use \"Importar pacote do CJ\".");

        return new ImportResult(imported, warnings);
    }

    private LibrarySkin Save(string name, SkinKind kind, List<(string Name, byte[] Data)> files, string source)
    {
        var skin = new LibrarySkin
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            Kind = kind,
            Files = files.Select(f => f.Name).ToList(),
            Source = source,
            ImportedAt = DateTime.Now,
        };
        var dir = SkinDir(skin);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllBytes(Path.Combine(dir, f.Name), f.Data);
        SaveMeta(skin);
        _skins.Insert(0, skin);
        return skin;
    }

    public void Rename(LibrarySkin skin, string name)
    {
        skin.Name = name;
        SaveMeta(skin);
    }

    private void SaveMeta(LibrarySkin skin) =>
        File.WriteAllText(Path.Combine(SkinDir(skin), "skin.json"), JsonSerializer.Serialize(skin, AppPaths.Json));

    public void Remove(LibrarySkin skin)
    {
        _skins.Remove(skin);
        var dir = SkinDir(skin);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public byte[] ReadFile(LibrarySkin skin, string file) => File.ReadAllBytes(FilePath(skin, file));

    private static bool IsModelFile(string path) =>
        path.EndsWith(".dff", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txd", StringComparison.OrdinalIgnoreCase);
}
