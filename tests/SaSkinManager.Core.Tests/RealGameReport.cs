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
