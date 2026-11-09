using SaSkinManager.Core.Formats;
using SaSkinManager.Core.Game;
using Xunit;

namespace SaSkinManager.Core.Tests;

/// <summary>
/// Diagnóstico com o jogo de verdade (só leitura): defina REAL_GAME_DIR e REPORT_FILE.
/// Lista os modelos com esqueleto de cada IMG, o número de ossos e se contam como personagem.
/// </summary>
public class RealGameReport
{
    [Fact]
    public void Write_bone_report_when_requested()
    {
        var root = Environment.GetEnvironmentVariable("REAL_GAME_DIR");
        var report = Environment.GetEnvironmentVariable("REPORT_FILE");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(report)) return;

        var lines = new List<string>();
        foreach (var imgName in new[] { "gta3.img", "cutscene.img" })
        {
            var path = Path.Combine(root, "models", imgName);
            if (!File.Exists(path)) { lines.Add($"## {imgName}: não existe"); continue; }
            using var img = ImgArchive.Open(path);
            lines.Add($"## {imgName}");
            foreach (var e in img.Entries.Where(e => e.Name.EndsWith(".dff", StringComparison.OrdinalIgnoreCase)))
            {
                var prefix = img.ReadPrefix(e, DffInfo.PrefixBytes);
                if (!DffInfo.IsSkinned(prefix)) continue;
                var bones = DffInfo.BoneCount(prefix);
                var full = bones == 0 ? DffInfo.BoneCount(img.Read(e)) : bones;
                lines.Add($"{e.Name,-24} ossos(prefixo)={bones,3} ossos(inteiro)={full,3} personagem={(bones >= DffInfo.MinCharacterBones ? "SIM" : "não")}");
            }
        }
        var models = new CatalogScanner(GameInstall.Open(root), new AppPaths(Path.Combine(Path.GetTempPath(), "saskin-report-data"))).Scan(null, null);
        lines.Add($"## catálogo: {models.Count} modelos");
        lines.AddRange(models.Where(m => m.Id < 0).Select(m => $"{m.Category}: {m.ModelName} ({m.ImgName})"));
        File.WriteAllLines(report, lines);
    }
}

/// <summary>Diagnóstico da prévia 3D com o jogo de verdade: defina REAL_GAME_DIR e MESH_REPORT_FILE.</summary>
public class RealGameMeshReport
{
    [Fact]
    public void Write_mesh_report_when_requested()
    {
        var root = Environment.GetEnvironmentVariable("REAL_GAME_DIR");
        var report = Environment.GetEnvironmentVariable("MESH_REPORT_FILE");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(report)) return;
        var models = new CatalogScanner(GameInstall.Open(root), new AppPaths(Path.Combine(Path.GetTempPath(), "saskin-report-data"))).Scan(null, null);
        var lines = new List<string>();
        int ok = 0, fail = 0;
        var imgs = new Dictionary<string, ImgArchive>();
        foreach (var m in models.Where(m => m.Kind == ModelKind.Ped))
        {
            if (!imgs.TryGetValue(m.ImgName, out var img)) imgs[m.ImgName] = img = ImgArchive.Open(Path.Combine(root, "models", m.ImgName));
            try
            {
                var dff = DffMesh.Read(img.TryRead(m.ModelName + ".dff")!);
                var txd = img.TryRead(m.TxdName + ".txd");
                var texNames = txd is null ? new HashSet<string>() : TxdReader.DecodeAll(txd).Select(t => t.Name.ToLowerInvariant()).ToHashSet();
                var parts = dff.Geometries.SelectMany(g => g.Parts).ToList();
                var missing = parts.Where(p => p.Texture.Length > 0 && !texNames.Contains(p.Texture.ToLowerInvariant())).Select(p => p.Texture).ToList();
                var (min, max) = dff.Bounds();
                lines.Add($"{m.ModelName,-14} geo={dff.Geometries.Count} tris={dff.TriangleCount,5} parts={parts.Count} min={min} max={max} missingTex={string.Join(",", missing)}");
                ok++;
            }
            catch (Exception e) { lines.Add($"{m.ModelName,-14} ERRO {e.GetType().Name}: {e.Message}"); fail++; }
        }
        foreach (var i in imgs.Values) i.Dispose();
        lines.Insert(0, $"ok={ok} fail={fail}");
        File.WriteAllLines(report, lines);
    }
}
