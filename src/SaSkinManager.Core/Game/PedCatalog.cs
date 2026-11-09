using System.Globalization;
using System.Text.Json;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.Core.Game;

/// <summary>Monta a lista de personagens a partir do data/peds.ide, com nomes amigáveis e categorias.</summary>
public static class PedCatalog
{
    public const string CjCategory = "Jogador (CJ)";

    private sealed record NameInfo(string? Name, string? En, string? Category);

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

    private static readonly System.Text.RegularExpressions.Regex AdultModel = new(
        @"^(gangrl|mecgrl|copgrl|nurgrl|crogrl|gungrl)[12]$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Versões 1 e 2 das namoradas do CJ (conteúdo "Hot Coffee": roupa íntima / sem roupa).
    /// A versão 3 é a normal do jogo e não conta. Ficam escondidas fora do modo 18+.
    /// </summary>
    public static bool IsAdult(GameModel m) => AdultModel.IsMatch(m.ModelName);

    /// <summary>
    /// Personagens que não estão no peds.ide: os "especiais" de missão (carregados por nome pelo script)
    /// e as versões de cutscene. Detectados pelo esqueleto no .dff dentro do gta3.img.
    /// </summary>
    public static List<GameModel> FindSpecials(ImgArchive img, IEnumerable<GameModel> known,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default, string? imgName = null)
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
            if (i % 50 == 0) progress?.Report(new ScanProgress(i, dffs.Count, L.T("procurando especiais: ", "looking for specials: ") + name));
            bool skinned;
            try { skinned = DffInfo.IsCharacter(img.ReadPrefix(e, DffInfo.PrefixBytes)); } // esqueleto completo: personagem, não objeto
            catch (IOException) { continue; }
            if (!skinned) continue;
            found.Add(Special(name, imgName));
        }
        return found.OrderBy(m => m.Category).ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private const string StoryCategory = "História";

    private static bool IsStoryLike(GameModel m) =>
        m.Kind == ModelKind.Ped && (m.Category is StoryCategory or SpecialCategory or CutsceneCategory);

    /// <summary>Nome sem o prefixo "cs" das versões de cutscene.</summary>
    private static string BaseName(GameModel m)
    {
        var n = m.ModelName.ToLowerInvariant();
        return m.Category == CutsceneCategory && n.StartsWith("cs") && n.Length > 2 ? n[2..] : n;
    }

    /// <summary>
    /// Outros modelos do mesmo personagem da história: a versão de cutscene (ex.: smoke → cssmoke)
    /// e variações de missão (ex.: ryder → ryder2, ryder3). Assim a troca vale no jogo e nas cenas.
    /// Só personagens da história/especiais, para não ligar pedestres genéricos por engano.
    /// </summary>
    public static List<GameModel> LinkedModels(GameModel target, IEnumerable<GameModel> catalog)
    {
        if (!IsStoryLike(target)) return new();
        var name = BaseName(target);
        if (name.Length < 4) return new();
        return catalog
            .Where(m => IsStoryLike(m) && m.Key != target.Key)
            .Where(m =>
            {
                var other = BaseName(m);
                return other.Length >= 4 && (other.StartsWith(name) || name.StartsWith(other));
            })
            .OrderBy(m => m.Category == CutsceneCategory ? 0 : 1)
            .ThenBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static GameModel Special(string name, string? imgName = null)
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
            SourceImg = imgName,
            Category = cutscene ? CutsceneCategory : SpecialCategory,
            DisplayName = display,
            Description = cutscene ? "Modelo usado nas cenas da história" : "Personagem especial de missão (não está no peds.ide)",
        };
    }

    private static readonly Dictionary<string, string> CategoryEn = new()
    {
        [CjCategory] = "Player (CJ)",
        [SpecialCategory] = "Missions / Specials",
        [CutsceneCategory] = "Cutscenes",
        ["História"] = "Story",
        ["Tríades"] = "Triads",
        ["Máfia"] = "Mafia",
        ["Polícia / Militar"] = "Police / Military",
        ["Serviços (médicos, bombeiros)"] = "Services (medics, firefighters)",
        ["Traficantes"] = "Dealers",
        ["Prostitutas"] = "Prostitutes",
        ["Criminosos"] = "Criminals",
        ["Pedestres (mulheres)"] = "Pedestrians (women)",
        ["Pedestres (homens)"] = "Pedestrians (men)",
        ["Gangues"] = "Gangs",
        ["Outros"] = "Other",
    };

    /// <summary>Nome da categoria no idioma atual (no catálogo/cache ela fica sempre em português).</summary>
    public static string CategoryLabel(string category) =>
        L.IsEnglish && CategoryEn.TryGetValue(category, out var en) ? en : category;

    /// <summary>Nome amigável no idioma atual.</summary>
    public static string DisplayNameFor(GameModel m)
    {
        if (m.Kind == ModelKind.Player) return m.DisplayName;
        var lower = m.ModelName.ToLowerInvariant();
        if (Names.Value.TryGetValue(lower, out var info) && info.Name is not null)
            return L.IsEnglish ? info.En ?? info.Name : info.Name;
        if (m.Id < 0 && lower.StartsWith("cs") && lower.Length > 2 && Names.Value.TryGetValue(lower[2..], out info) && info.Name is not null)
            return (L.IsEnglish ? info.En ?? info.Name : info.Name) + " (cutscene)";
        return m.DisplayName;
    }

    /// <summary>Descrição no idioma atual.</summary>
    public static string DescriptionFor(GameModel m)
    {
        if (m.Kind == ModelKind.Player)
            return L.T("Protagonista. O corpo é montado por peças de roupa do player.img.", "Protagonist. The body is built from player.img clothing parts.");
        if (m.Id < 0)
            return m.Category == CutsceneCategory
                ? L.T("Modelo usado nas cenas da história", "Model used in story cutscenes")
                : L.T("Personagem especial de missão (não está no peds.ide)", "Special mission character (not in peds.ide)");
        return Describe(m.ModelName, m.PedType);
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
            if (L.IsEnglish)
            {
                string raceEn = m[0] switch { 'b' => "Black", 'w' => "White", 'h' => "Hispanic", _ => "Asian" };
                string ageEn = m[2] switch { 'y' => "young", 'm' => "adult", _ => "old" };
                return $"{raceEn} {ageEn} {(female ? "woman" : "man")} · {m[3..]}";
            }
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
        return pedType.Length > 0 ? L.T($"Tipo: {pedType}", $"Type: {pedType}") : "";
    }
}
