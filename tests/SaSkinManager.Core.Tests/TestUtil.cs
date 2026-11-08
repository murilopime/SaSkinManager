using System.Text;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "saskin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

internal static class Fake
{
    public static byte[] Chunk(uint type, byte[] payload)
    {
        var o = new byte[12 + payload.Length];
        BitConverter.GetBytes(type).CopyTo(o, 0);
        BitConverter.GetBytes((uint)payload.Length).CopyTo(o, 4);
        BitConverter.GetBytes(0x1803FFFFu).CopyTo(o, 8);
        payload.CopyTo(o, 12);
        return o;
    }

    /// <summary>DFF falso: só um Clump com bytes aleatórios.</summary>
    public static byte[] Dff(int size, int seed)
    {
        var payload = new byte[size];
        new Random(seed).NextBytes(payload);
        return Chunk(RwChunk.Clump, payload);
    }

    /// <summary>TXD D3D9 com uma textura 8888 de cor sólida.</summary>
    public static byte[] Txd8888(string name, int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var st = new MemoryStream();
        var bw = new BinaryWriter(st);
        bw.Write(9u); // platform D3D9
        bw.Write(0x1106u);
        var n = new byte[32]; Encoding.ASCII.GetBytes(name).CopyTo(n, 0); bw.Write(n);
        bw.Write(new byte[32]);
        bw.Write(0x0500u); // 8888
        bw.Write(21u); // D3DFMT_A8R8G8B8
        bw.Write((ushort)w); bw.Write((ushort)h);
        bw.Write((byte)32); bw.Write((byte)1); bw.Write((byte)4); bw.Write((byte)1);
        bw.Write((uint)(w * h * 4));
        for (int i = 0; i < w * h; i++) { bw.Write(b); bw.Write(g); bw.Write(r); bw.Write(a); }
        return Dict(Chunk(RwChunk.TextureNative, Chunk(RwChunk.Struct, st.ToArray()).Concat(Chunk(RwChunk.Extension, Array.Empty<byte>())).ToArray()));
    }

    /// <summary>TXD D3D9 com uma textura DXT1 4x4: um bloco com as duas cores dadas.</summary>
    public static byte[] TxdDxt1(ushort c0, ushort c1, uint indices)
    {
        var st = new MemoryStream();
        var bw = new BinaryWriter(st);
        bw.Write(9u);
        bw.Write(0x1106u);
        bw.Write(new byte[64]);
        bw.Write(0x0200u);
        bw.Write(0x31545844u); // "DXT1"
        bw.Write((ushort)4); bw.Write((ushort)4);
        bw.Write((byte)16); bw.Write((byte)1); bw.Write((byte)4); bw.Write((byte)8);
        bw.Write(8u);
        bw.Write(c0); bw.Write(c1); bw.Write(indices);
        return Dict(Chunk(RwChunk.TextureNative, Chunk(RwChunk.Struct, st.ToArray())));
    }

    private static byte[] Dict(params byte[][] natives)
    {
        var header = new byte[4];
        BitConverter.GetBytes((ushort)natives.Length).CopyTo(header, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(header, 2);
        var body = Chunk(RwChunk.Struct, header).Concat(natives.SelectMany(x => x)).ToArray();
        return Chunk(RwChunk.TexDictionary, body);
    }

    public const string PedsIde = """
        # peds.ide de teste
        peds
        0, null, null, PLAYER1, STAT_PLAYER, man, 0, 0, null, 9, 9
        7, male01, male01, CIVMALE, STAT_STREET_GUY, man, 0, 0, null, 9, 9
        102, ballas1, ballas1, GANG1, STAT_GANG1, gang1, 0, 0, null, 9, 9
        105, fam1, fam1, GANG2, STAT_GANG2, gang1, 0, 0, null, 9, 9
        280, lapd1, lapd1, COP, STAT_COP, man, 0, 0, null, 9, 9
        270, sweet, sweet, CIVMALE, STAT_STREET_GUY, man, 0, 0, null, 9, 9
        12, bfyri, bfyri, CIVFEMALE, STAT_TOUGH_GIRL, woman, 0, 0, null, 9, 9
        end
        """;

    /// <summary>
    /// DFF estruturado: Clump{Struct, FrameList{Struct, Extension{HAnim?}}, GeometryList grande}.
    /// A GeometryList de 100 KB garante que o HAnim só é achado pelo começo do arquivo.
    /// </summary>
    public static byte[] PedDff(bool skinned)
    {
        var ext = skinned ? Chunk(DffInfo.HAnimPlugin, new byte[20]) : Array.Empty<byte>();
        var frameList = Chunk(0x0E, Chunk(RwChunk.Struct, new byte[60]).Concat(Chunk(RwChunk.Extension, ext)).ToArray());
        var geo = new byte[100_000];
        new Random(7).NextBytes(geo);
        var geoList = Chunk(0x1A, Chunk(RwChunk.Struct, geo));
        return Chunk(RwChunk.Clump, Chunk(RwChunk.Struct, new byte[12]).Concat(frameList).Concat(geoList).ToArray());
    }

    /// <summary>Monta uma pasta de jogo falsa com gta_sa.exe, peds.ide, gta3.img e player.img.</summary>
    public static string Game(TempDir t, bool modloader)
    {
        var root = t.Combine("game");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "models"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "data"));
        File.WriteAllText(System.IO.Path.Combine(root, "gta_sa.exe"), "");
        File.WriteAllText(System.IO.Path.Combine(root, "data", "peds.ide"), PedsIde);
        ImgArchive.Create(System.IO.Path.Combine(root, "models", "gta3.img"), new[]
        {
            ("male01.dff", Dff(3000, 1)), ("male01.txd", Txd8888("male01", 8, 8, 10, 20, 30)),
            ("ballas1.dff", Dff(5000, 2)), ("ballas1.txd", Txd8888("ballas1", 16, 16, 200, 0, 200)),
            ("fam1.dff", Dff(1000, 3)), ("fam1.txd", Txd8888("fam1", 8, 8, 0, 200, 0)),
            ("sweet.dff", Dff(2000, 4)), ("sweet.txd", Txd8888("sweet", 8, 8, 0, 150, 0)),
            // especiais (fora do peds.ide), cutscene e um objeto sem esqueleto
            ("cesar.dff", PedDff(skinned: true)), ("cesar.txd", Txd8888("cesar", 8, 8, 90, 60, 30)),
            ("csryder.dff", PedDff(skinned: true)), ("csryder.txd", Txd8888("csryder", 8, 8, 30, 30, 30)),
            ("lamppost.dff", PedDff(skinned: false)), ("lamppost.txd", Txd8888("lamppost", 8, 8, 80, 80, 80)),
            ("semtxd.dff", PedDff(skinned: true)),
        });
        ImgArchive.Create(System.IO.Path.Combine(root, "models", "player.img"), new[]
        {
            ("torso.dff", Dff(4000, 10)), ("torso.txd", Txd8888("torso", 8, 8, 255, 255, 255)),
            ("legs.dff", Dff(4000, 11)),
        });
        if (modloader)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "modloader"));
            File.WriteAllText(System.IO.Path.Combine(root, "modloader.asi"), "");
        }
        return root;
    }
}
