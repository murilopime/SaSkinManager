using System.Globalization;
using System.Text.Json;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.Core.Game;

/// <summary>Monta a lista de personagens a partir do data/peds.ide, com nomes amigáveis e categorias.</summary>
public static class PedCatalog
{
    public const string CjCategory = "Jogador (CJ)";

    private sealed record NameInfo(string? Name, string? Category);

    private static readonly Lazy<Dictionary<string, NameInfo>> Names = new(LoadNames);

    private static Dictionary<string, NameInfo> LoadNames()
    {
        using var s = typeof(PedCatalog).Assembly.GetManifestResourceStream("SaSkinManager.Core.ped_names.json");
        if (s is null) return new(StringComparer.OrdinalIgnoreCase);
        var raw = JsonSerializer.Deserialize<Dictionary<string, NameInfo>>(s,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return new Dictionary<string, NameInfo>(raw, StringComparer.OrdinalIgnoreCase);
    }

    public static GameModel Cj { get; } = new()
    {
        Id = 0,
        ModelName = "player",
        TxdName = "player",
        Kind = ModelKind.Player,
        Category = CjCategory,
        DisplayName = "CJ (Carl Johnson)",
        Description = "Protagonista. O corpo é montado por peças de roupa do player.img.",
        PedType = "PLAYER1",
    };

    public static List<GameModel> FromIdeFile(string pedsIdePath) => FromIde(IdeFile.Load(pedsIdePath));

    public static List<GameModel> FromIde(IdeFile ide)
    {
        var list = new List<GameModel> { Cj };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ide.Section("peds"))
        {
            if (row.Length < 4) continue;
            if (!int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
            var model = row[1];
            var txd = row[2];
            if (model.Equals("null", StringComparison.OrdinalIgnoreCase) || id == 0) continue;
            if (!seen.Add(model)) continue;
            var pedType = row[3].ToUpperInvariant();

            Names.Value.TryGetValue(model, out var info);
            list.Add(new GameModel
            {
                Id = id,
                ModelName = model,
                TxdName = txd,
                Kind = ModelKind.Ped,
                PedType = pedType,
                Category = info?.Category ?? CategoryFor(model, pedType),
                DisplayName = info?.Name ?? model,
                Description = Describe(model, pedType),
            });
        }
        return list;
    }

    public const string SpecialCategory = "Missões / Especiais";
    public const string CutsceneCategory = "Cutscenes";

    /// <summary>
    /// Personagens que não estão no peds.ide: os "especiais" de missão (carregados por nome pelo script)
    /// e as versões de cutscene. Detectados pelo esqueleto no .dff dentro do gta3.img.
    /// </summary>
    public static List<GameModel> FindSpecials(ImgArchive img, IEnumerable<GameModel> known,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var knownNames = new HashSet<string>(known.Select(m => m.ModelName), StringComparer.OrdinalIgnoreCase) { "player" };
        var dffs = img.Entries
            .Where(e => e.Name.EndsWith(".dff", StringComparison.OrdinalIgnoreCase))
            .Where(e => !knownNames.Contains(Path.GetFileNameWithoutExtension(e.Name)))
            .Where(e => img.Contains(Path.GetFileNameWithoutExtension(e.Name) + ".txd"))
            .ToList();

        var found = new List<GameModel>();
        for (int i = 0; i < dffs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = dffs[i];
            var name = Path.GetFileNameWithoutExtension(e.Name);
            if (i % 50 == 0) progress?.Report(new ScanProgress(i, dffs.Count, "procurando especiais: " + name));
            bool skinned;
            try { skinned = DffInfo.IsSkinned(img.ReadPrefix(e, DffInfo.PrefixBytes)); }
            catch (IOException) { continue; }
            if (!skinned) continue;
            found.Add(Special(name));
        }
        return found.OrderBy(m => m.Category).ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static GameModel Special(string name)
    {
        var lower = name.ToLowerInvariant();
        bool cutscene = lower.StartsWith("cs") && lower.Length > 2;
        NameInfo? info;
        string display;
        if (Names.Value.TryGetValue(lower, out info)) display = info.Name ?? name;
        else if (cutscene && Names.Value.TryGetValue(lower[2..], out info) && info.Name is not null) display = info.Name + " (cutscene)";
        else display = name;

        return new GameModel
        {
            Id = -1,
            ModelName = name,
            TxdName = name,
            Kind = ModelKind.Ped,
            PedType = "SPECIAL",
            Category = cutscene ? CutsceneCategory : SpecialCategory,
            DisplayName = display,
            Description = cutscene ? "Modelo usado nas cenas da história" : "Personagem especial de missão (não está no peds.ide)",
        };
    }

    internal static string CategoryFor(string model, string pedType)
    {
        var m = model.ToLowerInvariant();
        if (m.StartsWith("ballas")) return "Ballas";
        if (m.StartsWith("fam")) return "Grove Street Families";
        if (m.StartsWith("lsv")) return "Los Santos Vagos";
        if (m.StartsWith("vla")) return "Varrios Los Aztecas";
        if (m.StartsWith("sfr")) return "San Fierro Rifa";
        if (m.StartsWith("dnb")) return "Da Nang Boys";
        if (m.StartsWith("triad") || m == "triboss") return "Tríades";
        if (m.StartsWith("vmaff") || m.StartsWith("maff")) return "Máfia";
        return CategoryByType(pedType);
    }

    private static string CategoryByType(string pedType) => pedType switch
    {
        "COP" => "Polícia / Militar",
        "MEDIC" or "FIREMAN" or "EMERGENCY" => "Serviços (médicos, bombeiros)",
        "DEALER" => "Traficantes",
        "PROSTITUTE" => "Prostitutas",
        "CRIMINAL" => "Criminosos",
        "CIVFEMALE" => "Pedestres (mulheres)",
        "CIVMALE" => "Pedestres (homens)",
        _ when pedType.StartsWith("GANG") => "Gangues",
        _ => "Outros",
    };

    /// <summary>
    /// Os modelos genéricos seguem um padrão: etnia (b/w/h/o) + sexo (m/f) + idade (y/m/o) + papel.
    /// Ex.: "bmybar" = homem jovem negro, bar.
    /// </summary>
    internal static string Describe(string model, string pedType)
    {
        var m = model.ToLowerInvariant();
        if (m.Length >= 4 && "bwho".Contains(m[0]) && "mf".Contains(m[1]) && "ymo".Contains(m[2]))
        {
            bool female = m[1] == 'f';
            string race = m[0] switch
            {
                'b' => female ? "negra" : "negro",
                'w' => female ? "branca" : "branco",
                'h' => female ? "hispânica" : "hispânico",
                _ => "oriental",
            };
            string age = m[2] switch { 'y' => "jovem", 'm' => "adult" + (female ? "a" : "o"), _ => "idos" + (female ? "a" : "o") };
            string who = female ? "Mulher" : "Homem";
            return $"{who} {age} {race} · {m[3..]}";
        }
        return pedType.Length > 0 ? $"Tipo: {pedType}" : "";
    }
}
