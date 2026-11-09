using System.Text.Json;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.Core.Game;

public sealed record ScanProgress(int Done, int Total, string Current);

/// <summary>
/// Escaneia o jogo (peds.ide + gta3.img), monta o catálogo e extrai as texturas para miniaturas.
/// O resultado fica em cache e só é refeito quando gta3.img ou peds.ide mudam.
/// </summary>
public sealed class CatalogScanner
{
    private const int CacheVersion = 5; // 2: especiais e cutscenes; 3: miniaturas maiores; 4: cutscene.img; 5: ignora objetos animados

    private readonly GameInstall _game;
    private readonly string _cacheDir;

    public CatalogScanner(GameInstall game, AppPaths paths)
    {
        _game = game;
        _cacheDir = Path.Combine(paths.GameDataDir(game.Root), "cache");
        ThumbsDir = Path.Combine(_cacheDir, "thumbs");
        Directory.CreateDirectory(ThumbsDir);
    }

    public string ThumbsDir { get; }
    private string CacheFile => Path.Combine(_cacheDir, "catalog.json");

    public string ThumbPath(GameModel m) => Path.Combine(ThumbsDir, m.ModelName.ToLowerInvariant() + ".png");

    private sealed class CacheData
    {
        public int Version { get; set; }
        public string Fingerprint { get; set; } = "";
        public List<GameModel> Models { get; set; } = new();
    }

    /// <summary>
    /// peds.ide (tamanho + data) e quantidade de entradas do gta3.img. Não usa a data do gta3.img
    /// porque as próprias trocas no modo IMG alteram esse arquivo.
    /// </summary>
    private string Fingerprint()
    {
        var b = new FileInfo(_game.PedsIdePath);
        int count, cuts = 0;
        using (var img = ImgArchive.Open(_game.Gta3ImgPath)) count = img.Entries.Count;
        if (File.Exists(_game.CutsceneImgPath))
            using (var img = ImgArchive.Open(_game.CutsceneImgPath)) cuts = img.Entries.Count;
        return $"{b.Length}:{b.LastWriteTimeUtc.Ticks}|img:{count}|cuts:{cuts}";
    }

    /// <summary>Catálogo do cache, ou null se não houver cache válido.</summary>
    public List<GameModel>? TryLoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var data = JsonSerializer.Deserialize<CacheData>(File.ReadAllText(CacheFile));
            if (data is null || data.Version != CacheVersion || data.Fingerprint != Fingerprint()) return null;
            return data.Models;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Escaneamento completo. <paramref name="saveThumb"/> recebe a textura decodificada e o caminho do PNG
    /// (a conversão para PNG fica na camada de interface).
    /// </summary>
    public List<GameModel> Scan(Action<DecodedTexture, string>? saveThumb, IProgress<ScanProgress>? progress, CancellationToken ct = default)
    {
        var models = PedCatalog.FromIdeFile(_game.PedsIdePath);
        using var img = ImgArchive.Open(_game.Gta3ImgPath);
        models.AddRange(PedCatalog.FindSpecials(img, models, progress, ct));

        // as versões de cutscene ficam num IMG separado (models\cutscene.img)
        ImgArchive? cutImg = null;
        if (File.Exists(_game.CutsceneImgPath))
        {
            try { cutImg = ImgArchive.Open(_game.CutsceneImgPath); }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException) { }
        }
        using var cutImgScope = cutImg;
        if (cutImg is not null)
            models.AddRange(PedCatalog.FindSpecials(cutImg, models, progress, ct, GameInstall.CutsceneImgName));
        if (saveThumb is not null)
        {
            for (int i = 0; i < models.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = models[i];
                progress?.Report(new ScanProgress(i, models.Count, m.ModelName));
                if (m.Kind != ModelKind.Ped) continue;
                try
                {
                    var source = m.ImgName == GameInstall.CutsceneImgName ? cutImg : img;
                    var txd = source?.TryRead(m.TxdName + ".txd");
                    var tex = txd is null ? null : TxdReader.DecodeBest(txd);
                    if (tex is not null) saveThumb(tex, ThumbPath(m));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException) { }
            }
        }
        progress?.Report(new ScanProgress(models.Count, models.Count, ""));

        File.WriteAllText(CacheFile, JsonSerializer.Serialize(
            new CacheData { Version = CacheVersion, Fingerprint = Fingerprint(), Models = models }, AppPaths.Json));
        return models;
    }

    public void ClearCache()
    {
        if (File.Exists(CacheFile)) File.Delete(CacheFile);
        foreach (var f in Directory.EnumerateFiles(ThumbsDir, "*.png")) File.Delete(f);
    }
}
