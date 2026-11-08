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
