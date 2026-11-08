using System.Diagnostics;

namespace SaSkinManager.Core.Game;

/// <summary>Uma instalação do GTA San Andreas (PC, clássico).</summary>
public sealed class GameInstall
{
    public static readonly string[] ExeNames = { "gta_sa.exe", "gta-sa.exe", "gta_sa_compact.exe" };

    public string Root { get; }

    private GameInstall(string root) => Root = root;

    public string ModelsDir => Path.Combine(Root, "models");
    public string Gta3ImgPath => Path.Combine(ModelsDir, "gta3.img");
    public string PlayerImgPath => Path.Combine(ModelsDir, "player.img");
    public string PedsIdePath => Path.Combine(Root, "data", "peds.ide");
    public string ModloaderDir => Path.Combine(Root, "modloader");

    public string? ExePath => ExeNames.Select(n => Path.Combine(Root, n)).FirstOrDefault(File.Exists);

    public bool HasModloader =>
        Directory.Exists(ModloaderDir) && File.Exists(Path.Combine(Root, "modloader.asi"));

    public string ImgPath(string imgName) => Path.Combine(ModelsDir, imgName);

    /// <summary>Valida a pasta. Retorna null se OK, ou a mensagem do que está faltando.</summary>
    public static string? Validate(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return "Pasta não encontrada.";
        var g = new GameInstall(root);
        if (g.ExePath is null) return "gta_sa.exe não encontrado nesta pasta.";
        if (!File.Exists(g.Gta3ImgPath)) return "models\\gta3.img não encontrado.";
        if (!File.Exists(g.PedsIdePath)) return "data\\peds.ide não encontrado.";
        return null;
    }

    public static GameInstall Open(string root)
    {
        var err = Validate(root);
        if (err is not null) throw new DirectoryNotFoundException(err);
        return new GameInstall(Path.GetFullPath(root));
    }

    /// <summary>Procura o jogo nos lugares mais comuns (Steam, Rockstar, discos C–F).</summary>
    public static IEnumerable<string> GuessLocations()
    {
        var rel = new[]
        {
            @"Program Files (x86)\Rockstar Games\GTA San Andreas",
            @"Program Files\Rockstar Games\GTA San Andreas",
            @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto San Andreas",
            @"Program Files\Steam\steamapps\common\Grand Theft Auto San Andreas",
            @"SteamLibrary\steamapps\common\Grand Theft Auto San Andreas",
            @"Games\GTA San Andreas",
            @"Jogos\GTA San Andreas",
            @"GTA San Andreas",
            @"Rockstar Games\GTA San Andreas",
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in new[] { "C:\\", "D:\\", "E:\\", "F:\\" })
        foreach (var r in rel)
        {
            var p = Path.Combine(drive, r);
            if (seen.Add(p) && Validate(p) is null) yield return p;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var sub in new[] { "Desktop", "Documents", "Downloads", "Games" })
        {
            var p = Path.Combine(home, sub, "GTA San Andreas");
            if (seen.Add(p) && Validate(p) is null) yield return p;
        }
    }

    public static bool IsGameRunning() =>
        ExeNames.Any(n => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(n)).Length > 0);

    public void Launch()
    {
        var exe = ExePath ?? throw new FileNotFoundException("Executável do jogo não encontrado.");
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Root, UseShellExecute = true });
    }
}
