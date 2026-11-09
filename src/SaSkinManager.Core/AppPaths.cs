using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaSkinManager.Core;

/// <summary>Pastas do app em %AppData%\SaSkinManager.</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaSkinManager");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LibraryDir => Path.Combine(Root, "library");

    /// <summary>Pasta de dados de uma instalação específica (manifest, backups, cache).</summary>
    public string GameDataDir(string gameRoot)
    {
        var norm = Path.GetFullPath(gameRoot).TrimEnd('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..12];
        var dir = Path.Combine(Root, "games", hash);
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}

public enum SwapMode
{
    /// <summary>Mod Loader se estiver instalado, senão IMG.</summary>
    Auto,
    Modloader,
    Img,
}

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public SwapMode PreferredMode { get; set; } = SwapMode.Auto;

    /// <summary>"auto" (segue o Windows), "pt" ou "en".</summary>
    public string Language { get; set; } = L.Auto;

    /// <summary>Personagens em grade (true) ou lista (false).</summary>
    public bool GridView { get; set; } = true;

    /// <summary>Lado das miniaturas na grade, em pixels.</summary>
    public int ThumbSize { get; set; } = 112;

    /// <summary>Cor de destaque da interface: "orange", "green", "yellow", "purple", "blue" ou "pink".</summary>
    public string Accent { get; set; } = "orange";

    /// <summary>Mostra as versões de cutscene na lista de personagens (elas já são trocadas junto com o personagem).</summary>
    public bool ShowCutscenes { get; set; }

    /// <summary>Personagens marcados com estrela (GameModel.Key).</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>Ordem da biblioteca: "recent", "name" ou "usage".</summary>
    public string SkinSort { get; set; } = "recent";

    /// <summary>Modo 18+: mostra as versões "Hot Coffee" das namoradas do CJ (desligado por padrão).</summary>
    public bool AdultMode { get; set; }

    /// <summary>Miniaturas e prévia com o modelo 3D (false = só a textura, como na v1).</summary>
    public bool Preview3D { get; set; } = true;

    /// <summary>Todos os modelos visíveis na grade ficam girando (false = só o que está sob o mouse).</summary>
    public bool SpinGrid { get; set; } = true;

    /// <summary>A prévia 3D gira sozinha.</summary>
    public bool AutoRotate { get; set; } = true;

    /// <summary>Fecha o programa depois de clicar em Jogar.</summary>
    public bool CloseOnPlay { get; set; }

    public static AppSettings Load(AppPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile)) ?? new();
        }
        catch (JsonException) { }
        return new AppSettings();
    }

    public void Save(AppPaths paths) =>
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, AppPaths.Json));
}
