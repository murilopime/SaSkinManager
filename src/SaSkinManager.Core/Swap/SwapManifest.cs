using System.Text.Json;
using SaSkinManager.Core.Game;

namespace SaSkinManager.Core.Swap;

public sealed class SwapRecord
{
    public string TargetKey { get; init; } = "";
    public string TargetModel { get; init; } = "";
    public string TargetName { get; init; } = "";
    public ModelKind TargetKind { get; init; }
    public string ImgName { get; init; } = "gta3.img";
    public string SkinId { get; init; } = "";
    public string SkinName { get; init; } = "";
    public SwapMode Mode { get; init; }
    /// <summary>Nomes dos arquivos instalados (como o jogo os vê, ex.: "bmybar.dff").</summary>
    public List<string> Files { get; init; } = new();
    /// <summary>Modo modloader: pasta onde os arquivos foram gravados.</summary>
    public string? ModloaderDir { get; init; }
    public DateTime AppliedAt { get; init; }
}

/// <summary>Lista de trocas ativas de uma instalação, salva em JSON.</summary>
public sealed class SwapManifest
{
    private readonly string _file;

    public SwapManifest(string file)
    {
        _file = file;
        if (File.Exists(file))
        {
            try { Records = JsonSerializer.Deserialize<List<SwapRecord>>(File.ReadAllText(file)) ?? new(); }
            catch (JsonException) { Records = new(); }
        }
    }

    public List<SwapRecord> Records { get; } = new();

    public SwapRecord? Find(string targetKey) =>
        Records.FirstOrDefault(r => r.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));

    public void Add(SwapRecord r)
    {
        Records.RemoveAll(x => x.TargetKey.Equals(r.TargetKey, StringComparison.OrdinalIgnoreCase));
        Records.Add(r);
        Save();
    }

    public void Remove(SwapRecord r)
    {
        Records.Remove(r);
        Save();
    }

    public void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Records, AppPaths.Json));
        File.Move(tmp, _file, true);
    }
}
