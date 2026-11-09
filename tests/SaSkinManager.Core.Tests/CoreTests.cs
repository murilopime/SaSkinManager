using System.IO.Compression;
using System.Security.Cryptography;
using SaSkinManager.Core.Formats;
using SaSkinManager.Core.Game;
using SaSkinManager.Core.Library;
using SaSkinManager.Core.Swap;

namespace SaSkinManager.Core.Tests;

public class ImgArchiveTests
{
    [Fact]
    public void Reads_entries_and_data()
    {
        using var t = new TempDir();
        var path = t.Combine("a.img");
        var a = Fake.Dff(100, 1);
        var b = Fake.Dff(5000, 2);
        ImgArchive.Create(path, new[] { ("a.dff", a), ("b.dff", b) });

        using var img = ImgArchive.Open(path);
        Assert.Equal(2, img.Entries.Count);
        Assert.Equal(1u, img.Find("A.DFF")!.SizeInSectors);
        Assert.Equal(3u, img.Find("b.dff")!.SizeInSectors);
        Assert.Equal(b, img.TryRead("b.dff")![..b.Length]);
    }

    [Fact]
    public void Replace_smaller_in_place_and_larger_appended()
    {
        using var t = new TempDir();
        var path = t.Combine("a.img");
        ImgArchive.Create(path, new[] { ("a.dff", Fake.Dff(5000, 1)), ("b.dff", Fake.Dff(100, 2)) });
        var small = Fake.Dff(10, 3);
        var big = Fake.Dff(9000, 4);

        using (var img = ImgArchive.Open(path, writable: true))
        {
            uint oldA = img.Find("a.dff")!.Offset;
            img.Replace("a.dff", small);
            Assert.Equal(oldA, img.Find("a.dff")!.Offset);
            uint oldB = img.Find("b.dff")!.Offset;
            img.Replace("b.dff", big);
            Assert.NotEqual(oldB, img.Find("b.dff")!.Offset);
        }
        using (var img = ImgArchive.Open(path))
        {
            Assert.Equal(small, img.TryRead("a.dff")![..small.Length]);
            Assert.Equal(big, img.TryRead("b.dff")![..big.Length]);
            Assert.Equal(5u, img.Find("b.dff")!.SizeInSectors);
        }
    }

    [Fact]
    public void Rejects_non_ver2()
    {
        using var t = new TempDir();
        var path = t.Combine("x.img");
        File.WriteAllBytes(path, new byte[4096]);
        Assert.Throws<NotSupportedException>(() => ImgArchive.Open(path));
    }
}

public class IdeAndCatalogTests
{
    [Fact]
    public void Parses_peds_and_categorizes()
    {
        var list = PedCatalog.FromIde(IdeFile.Parse(Fake.PedsIde));
        Assert.Equal(ModelKind.Player, list[0].Kind); // CJ primeiro
        Assert.DoesNotContain(list, m => m.ModelName == "null");
        Assert.Equal("Ballas", list.Single(m => m.ModelName == "ballas1").Category);
        Assert.Equal("Grove Street Families", list.Single(m => m.ModelName == "fam1").Category);
        Assert.Equal("Polícia / Militar", list.Single(m => m.ModelName == "lapd1").Category);
        var sweet = list.Single(m => m.ModelName == "sweet");
        Assert.Equal("Sweet Johnson", sweet.DisplayName);
        Assert.Equal("História", sweet.Category);
        Assert.StartsWith("Mulher jovem negra", list.Single(m => m.ModelName == "bfyri").Description);
    }

    [Fact]
    public void Detects_skinned_dff_even_from_prefix()
    {
        var ped = Fake.PedDff(skinned: true);
        Assert.True(ped.Length > DffInfo.PrefixBytes);
        Assert.True(DffInfo.IsSkinned(ped));
        Assert.True(DffInfo.IsSkinned(ped.AsSpan(0, DffInfo.PrefixBytes)));
        Assert.False(DffInfo.IsSkinned(Fake.PedDff(skinned: false)));
        Assert.True(DffInfo.IsCharacter(ped.AsSpan(0, DffInfo.PrefixBytes)));
        Assert.Equal(32, DffInfo.BoneCount(ped));
        var prop = Fake.PedDff(skinned: true, bones: 3);
        Assert.True(DffInfo.IsSkinned(prop));
        Assert.False(DffInfo.IsCharacter(prop)); // objeto animado de cutscene não é personagem
        Assert.False(DffInfo.IsCharacter(Fake.PedDff(skinned: true, bones: 25))); // carro de cutscene (ex.: cscopcarsf)
        Assert.True(DffInfo.IsCharacter(Fake.PedDff(skinned: true, bones: 61))); // ped de cutscene com rosto
        Assert.False(DffInfo.IsSkinned(Fake.Txd8888("x", 4, 4, 1, 1, 1)));
    }

    [Fact]
    public void Finds_special_and_cutscene_characters_in_img()
    {
        using var t = new TempDir();
        var root = Fake.Game(t, modloader: false);
        using var img = ImgArchive.Open(Path.Combine(root, "models", "gta3.img"));
        var known = PedCatalog.FromIde(IdeFile.Parse(Fake.PedsIde));
        var specials = PedCatalog.FindSpecials(img, known);

        var cesar = specials.Single(m => m.ModelName == "cesar");
        Assert.Equal(PedCatalog.SpecialCategory, cesar.Category);
        Assert.Equal("Cesar Vialpando", cesar.DisplayName);
        Assert.Equal(("cesar", -1), (cesar.TxdName, cesar.Id));

        var cs = specials.Single(m => m.ModelName == "csryder");
        Assert.Equal(PedCatalog.CutsceneCategory, cs.Category);
        Assert.Equal("Ryder (cutscene)", cs.DisplayName);

        Assert.DoesNotContain(specials, m => m.ModelName is "lamppost" or "semtxd" or "sweet" or "male01");
    }
}

public class TxdTests
{
    [Fact]
    public void Decodes_8888_and_picks_largest()
    {
        var tex = TxdReader.DecodeLargest(Fake.Txd8888("x", 4, 2, 10, 20, 30, 40))!;
        Assert.Equal((4, 2), (tex.Width, tex.Height));
        Assert.Equal(new byte[] { 30, 20, 10, 40 }, tex.Bgra[..4]);
    }

    [Fact]
    public void Decodes_dxt1()
    {
        // c0 = vermelho puro (0xF800), c1 = azul puro (0x001F); todos os pixels índice 0 exceto o 1º com índice 1
        var tex = TxdReader.DecodeLargest(Fake.TxdDxt1(0xF800, 0x001F, 0b01))!;
        Assert.Equal(16 * 4, tex.Bgra.Length);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, tex.Bgra[..4]);     // azul (BGRA)
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, tex.Bgra[4..8]);   // vermelho
    }

    [Fact]
    public void Garbage_returns_null()
    {
        Assert.Null(TxdReader.DecodeLargest(new byte[] { 1, 2, 3 }));
        Assert.Null(TxdReader.DecodeLargest(Fake.Dff(50, 1)));
    }
}

public class SwapTests
{
    private static (GameInstall game, SkinLibrary lib, SwapService svc, List<GameModel> models) Setup(TempDir t, bool modloader)
    {
        var root = Fake.Game(t, modloader);
        var paths = new AppPaths(t.Combine("appdata"));
        var game = GameInstall.Open(root);
        var lib = new SkinLibrary(paths);
        var svc = new SwapService(game, lib, paths) { IsGameRunning = () => false };
        return (game, lib, svc, PedCatalog.FromIdeFile(game.PedsIdePath));
    }

    private static string WriteSkin(TempDir t, string name, int seed)
    {
        var dir = t.Combine("downloads", name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".dff"), Fake.Dff(7000, seed));
        File.WriteAllBytes(Path.Combine(dir, name + ".txd"), Fake.Txd8888(name, 8, 8, 1, 2, 3));
        return dir;
    }

    [Fact]
    public void Rename_skin_is_kept_after_reopening_library()
    {
        using var t = new TempDir();
        var paths = new AppPaths(t.Combine("appdata"));
        var lib = new SkinLibrary(paths);
        var skin = lib.Import(new[] { WriteSkin(t, "niko", 5) }, false).Imported.Single();
        lib.Rename(skin, "Niko Bellic (GTA IV)");
        Assert.Equal("Niko Bellic (GTA IV)", new SkinLibrary(paths).Skins.Single().Name);
    }

    [Fact]
    public void Library_backup_round_trip()
    {
        using var t = new TempDir();
        var lib = new SkinLibrary(new AppPaths(t.Combine("pc1")));
        var niko = lib.Import(new[] { WriteSkin(t, "niko", 5) }, false).Imported.Single();
        lib.Rename(niko, "Niko Bellic");
        lib.Import(new[] { WriteSkin(t, "tommy", 6) }, false);
        var zip = t.Combine("backup.zip");
        lib.ExportBackup(zip);
        Assert.True(SkinLibrary.IsBackup(zip));

        var other = new SkinLibrary(new AppPaths(t.Combine("pc2")));
        Assert.Equal((2, 0), other.ImportBackup(zip));
        Assert.Contains(other.Skins, s => s.Name == "Niko Bellic");
        Assert.Equal(lib.ReadFile(niko, "niko.dff"), other.ReadFile(other.Skins.Single(s => s.Id == niko.Id), "niko.dff"));
        Assert.Equal((0, 2), other.ImportBackup(zip)); // de novo: tudo já existe
        Assert.Equal(2, new SkinLibrary(new AppPaths(t.Combine("pc2"))).Skins.Count);
    }

    [Fact]
    public void Import_pairs_folder_and_zip()
    {
        using var t = new TempDir();
        var (_, lib, _, _) = Setup(t, false);
        var dir = WriteSkin(t, "niko", 5);
        var zip = t.Combine("tommy.zip");
        var src = WriteSkin(t, "tommy", 6);
        ZipFile.CreateFromDirectory(src, zip);

        var r1 = lib.Import(new[] { dir });
        var r2 = lib.Import(new[] { zip });
        Assert.Single(r1.Imported);
        Assert.Single(r2.Imported);
        Assert.Equal("tommy", r2.Imported[0].Name);
        Assert.Equal(SkinKind.Ped, r1.Imported[0].Kind);
        Assert.Equal(2, new SkinLibrary(new AppPaths(t.Combine("appdata"))).Skins.Count);
    }

    [Fact]
    public void Modloader_mode_writes_renamed_files_and_reverts()
    {
        using var t = new TempDir();
        var (game, lib, svc, models) = Setup(t, true);
        var skin = lib.Import(new[] { WriteSkin(t, "niko", 5) }).Imported[0];
        var target = models.Single(m => m.ModelName == "ballas1");
        var imgHash = Hash(game.Gta3ImgPath);

        var rec = svc.Apply(target, skin, SwapMode.Auto);
        Assert.Equal(SwapMode.Modloader, rec.Mode);
        var dir = Path.Combine(game.ModloaderDir, "SaSkinManager", "ballas1");
        Assert.True(File.Exists(Path.Combine(dir, "ballas1.dff")));
        Assert.True(File.Exists(Path.Combine(dir, "ballas1.txd")));
        Assert.Equal(imgHash, Hash(game.Gta3ImgPath)); // IMG intocado

        svc.Revert(rec);
        Assert.False(Directory.Exists(Path.Combine(game.ModloaderDir, "SaSkinManager")));
        Assert.Empty(svc.Manifest.Records);
    }

    [Fact]
    public void Img_mode_replaces_backs_up_and_restores_original_content()
    {
        using var t = new TempDir();
        var (game, lib, svc, models) = Setup(t, false);
        var skin = lib.Import(new[] { WriteSkin(t, "niko", 5) }).Imported[0];
        var target = models.Single(m => m.ModelName == "fam1");
        byte[] origDff, origTxd;
        using (var img = ImgArchive.Open(game.Gta3ImgPath)) { origDff = img.TryRead("fam1.dff")!; origTxd = img.TryRead("fam1.txd")!; }

        var rec = svc.Apply(target, skin, SwapMode.Auto);
        Assert.Equal(SwapMode.Img, rec.Mode);
        Assert.True(File.Exists(SwapService.FullBackupPath(game.Gta3ImgPath)));
        using (var img = ImgArchive.Open(game.Gta3ImgPath))
        {
            var dff = lib.ReadFile(skin, skin.DffFile!);
            Assert.Equal(dff, img.TryRead("fam1.dff")![..dff.Length]);
        }

        // aplicar outra skin por cima e depois desfazer deve voltar ao ORIGINAL
        var skin2 = lib.Import(new[] { WriteSkin(t, "tommy", 9) }).Imported[0];
        rec = svc.Apply(target, skin2, SwapMode.Img);
        svc.Revert(rec);
        using (var img = ImgArchive.Open(game.Gta3ImgPath))
        {
            Assert.Equal(origDff, img.TryRead("fam1.dff")![..origDff.Length]);
            Assert.Equal(origTxd, img.TryRead("fam1.txd")![..origTxd.Length]);
        }
        Assert.Empty(svc.Manifest.Records);
    }

    [Fact]
    public void Img_mode_blocked_while_game_running()
    {
        using var t = new TempDir();
        var (_, lib, svc, models) = Setup(t, false);
        svc.IsGameRunning = () => true;
        var skin = lib.Import(new[] { WriteSkin(t, "niko", 5) }).Imported[0];
        Assert.Throws<SwapException>(() => svc.Apply(models.Single(m => m.ModelName == "fam1"), skin, SwapMode.Img));
    }

    [Fact]
    public void Cj_requires_pack_and_uses_player_img()
    {
        using var t = new TempDir();
        var (game, lib, svc, models) = Setup(t, true);
        var cj = models[0];
        var pedSkin = lib.Import(new[] { WriteSkin(t, "niko", 5) }).Imported[0];
        Assert.Throws<SwapException>(() => svc.Apply(cj, pedSkin, SwapMode.Auto));

        var packDir = t.Combine("downloads", "cjpack");
        Directory.CreateDirectory(packDir);
        File.WriteAllBytes(Path.Combine(packDir, "torso.dff"), Fake.Dff(100, 1));
        File.WriteAllBytes(Path.Combine(packDir, "torso.txd"), Fake.Txd8888("torso", 4, 4, 1, 1, 1));
        var pack = lib.Import(new[] { packDir }, asCjPack: true).Imported[0];

        svc.Apply(cj, pack, SwapMode.Modloader);
        Assert.True(File.Exists(Path.Combine(game.ModloaderDir, "SaSkinManager", "cj", "player.img", "torso.dff")));

        var rec = svc.Apply(cj, pack, SwapMode.Img); // substitui a troca anterior
        Assert.Equal("player.img", rec.ImgName);
        Assert.Single(svc.Manifest.Records);
        Assert.False(Directory.Exists(Path.Combine(game.ModloaderDir, "SaSkinManager")));
        svc.RevertAll();
        Assert.Empty(svc.Manifest.Records);
    }

    [Fact]
    public void Scanner_builds_catalog_thumbs_and_cache()
    {
        using var t = new TempDir();
        var (game, _, _, _) = Setup(t, false);
        var scanner = new CatalogScanner(game, new AppPaths(t.Combine("appdata")));
        Assert.Null(scanner.TryLoadCache());
        var thumbs = new List<string>();
        var models = scanner.Scan((tex, path) => thumbs.Add(Path.GetFileName(path)), null);
        Assert.Contains("ballas1.png", thumbs);
        Assert.Equal(models.Count, scanner.TryLoadCache()!.Count);

        // versão de cutscene vem do cutscene.img, com miniatura, e fica ligada ao Sweet
        var cs = models.Single(m => m.ModelName == "cssweet");
        Assert.Equal("cutscene.img", cs.ImgName);
        Assert.Equal("cutscene.img", scanner.TryLoadCache()!.Single(m => m.ModelName == "cssweet").ImgName);
        Assert.Contains("cssweet.png", thumbs);
        Assert.DoesNotContain(models, m => m.ModelName == "cscard"); // objeto de cutscene fica de fora
        var sweet = models.Single(m => m.ModelName == "sweet");
        Assert.Contains(PedCatalog.LinkedModels(sweet, models), m => m.ModelName == "cssweet");
    }

    /// <summary>Gera uma pasta de jogo falsa para testar a interface: defina DEMO_GAME_DIR e rode só este teste.</summary>
    [Fact]
    public void Generate_demo_game_when_requested()
    {
        var target = Environment.GetEnvironmentVariable("DEMO_GAME_DIR");
        if (string.IsNullOrEmpty(target)) return;
        using var t = new TempDir();
        var root = Fake.Game(t, modloader: true);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(root, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, true);
        }
        Directory.CreateDirectory(Path.Combine(target, "modloader"));
        WriteSkin(t, "niko", 5);
        foreach (var f in Directory.EnumerateFiles(t.Combine("downloads", "niko")))
        {
            Directory.CreateDirectory(Path.Combine(target, "_skin_niko"));
            File.Copy(f, Path.Combine(target, "_skin_niko", Path.GetFileName(f)), true);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
