using System.Numerics;
using SaSkinManager.Core.Formats;
using Xunit;

namespace SaSkinManager.Core.Tests;

public class MeshTests
{
    private static byte[] F(params float[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] I(params int[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] U16(params ushort[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] Str(string s) { var b = new byte[(s.Length / 4 + 1) * 4]; System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b, 0); return b; }

    /// <summary>DFF com 1 frame (translação), 1 geometria triangular texturizada ("body") e 1 atomic.</summary>
    private static byte[] TriangleDff(bool binMesh, float frameY = 0)
    {
        byte[] C(uint type, byte[] payload) => Fake.Chunk(type, payload);
        var frame = F(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, frameY, 0).Concat(I(-1, 0)).ToArray();
        var frameList = C(0x0E, C(RwChunk.Struct, I(1).Concat(frame).ToArray()).Concat(C(RwChunk.Extension, Array.Empty<byte>())).ToArray());

        int flags = 0x02 | 0x04 | 0x10; // posições, 1 conjunto de UV, normais
        var geoStruct = I(flags, 1, 3, 1)
            .Concat(F(0, 0, 1, 0, 0, 1))                    // UVs
            .Concat(U16(1, 0, 0, 2))                         // triângulo: v1, v0, material, v2
            .Concat(F(0, 0, 0, 1)).Concat(I(1, 1))           // esfera, tem vértices, tem normais
            .Concat(F(0, 0, 0, 1, 0, 0, 0, 2, 0))            // posições
            .Concat(F(0, 0, 1, 0, 0, 1, 0, 0, 1))            // normais
            .ToArray();
        var texture = C(0x06, C(RwChunk.Struct, I(0x1106)).Concat(C(0x02, Str("body"))).Concat(C(0x02, Str(""))).Concat(C(RwChunk.Extension, Array.Empty<byte>())).ToArray());
        var material = C(0x07, C(RwChunk.Struct, I(0).Concat(new byte[] { 255, 255, 255, 255 }).Concat(I(0, 1)).Concat(F(1, 1, 1)).ToArray()).Concat(texture).ToArray());
        var matList = C(0x08, C(RwChunk.Struct, I(1, -1)).Concat(material).ToArray());
        var ext = binMesh ? C(0x50E, I(0, 1, 3, 3, 0, 0, 1, 2)) : Array.Empty<byte>();
        var geometry = C(0x0F, C(RwChunk.Struct, geoStruct).Concat(matList).Concat(C(RwChunk.Extension, ext)).ToArray());
        var geoList = C(0x1A, C(RwChunk.Struct, I(1)).Concat(geometry).ToArray());
        var atomic = C(0x14, C(RwChunk.Struct, I(0, 0, 5, 0)));
        return C(RwChunk.Clump, C(RwChunk.Struct, I(1, 0, 0)).Concat(frameList).Concat(geoList).Concat(atomic).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_triangle_uvs_and_texture(bool binMesh)
    {
        var m = DffMesh.Read(TriangleDff(binMesh));
        var g = Assert.Single(m.Geometries);
        Assert.Equal(3, g.Positions.Length);
        Assert.Equal(new Vector3(0, 2, 0), g.Positions[2]);
        Assert.Equal(new Vector2(1, 0), g.Uvs[1]);
        var part = Assert.Single(g.Parts);
        Assert.Equal("body", part.Texture);
        Assert.Equal(new[] { 0, 1, 2 }, part.Indices);
        Assert.Equal(1, m.TriangleCount);
    }

    [Fact]
    public void Applies_atomic_frame_transform()
    {
        var m = DffMesh.Read(TriangleDff(false, frameY: 5));
        var (min, max) = m.Bounds();
        Assert.Equal(5, min.Y, 3);
        Assert.Equal(7, max.Y, 3);
    }

    [Fact]
    public void Rejects_non_dff()
    {
        Assert.Throws<InvalidDataException>(() => DffMesh.Read(Fake.Txd8888("x", 4, 4, 1, 2, 3)));
    }
}

public class AdultFilterTests
{
    [Theory]
    [InlineData("gangrl1", true)]
    [InlineData("GANGRL2", true)]
    [InlineData("nurgrl2", true)]
    [InlineData("gangrl3", false)]
    [InlineData("COPGRL3", false)]
    [InlineData("sweet", false)]
    [InlineData("csgangrl1x", false)]
    public void Detects_hot_coffee_girlfriend_versions(string model, bool adult)
    {
        Assert.Equal(adult, SaSkinManager.Core.Game.PedCatalog.IsAdult(new SaSkinManager.Core.Game.GameModel { ModelName = model }));
    }
}
