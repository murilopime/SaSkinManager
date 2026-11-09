using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SaSkinManager.Core.Formats;

/// <summary>Um pedaço da malha que usa um único material (textura).</summary>
public sealed class MeshPart
{
    public string Texture { get; init; } = "";
    /// <summary>Cor do material (RGBA).</summary>
    public uint Color { get; init; } = 0xFFFFFFFF;
    /// <summary>Índices de triângulos (3 por triângulo) no array de vértices da geometria.</summary>
    public List<int> Indices { get; } = new();
}

/// <summary>Geometria pronta para desenhar: posições (Z para cima, como no jogo), UVs e partes por material.</summary>
public sealed class MeshGeometry
{
    public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();
    public Vector2[] Uvs { get; init; } = Array.Empty<Vector2>();
    public Vector3[]? Normals { get; init; }
    public List<MeshPart> Parts { get; } = new();
}

public sealed class DffModel
{
    public List<MeshGeometry> Geometries { get; } = new();

    public int TriangleCount => Geometries.Sum(g => g.Parts.Sum(p => p.Indices.Count / 3));

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var g in Geometries)
        foreach (var v in g.Positions)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return Geometries.Count == 0 || min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }
}

/// <summary>
/// Leitor da geometria de um DFF (RenderWare Clump) para a prévia 3D: vértices, UVs, triângulos e
/// nome da textura de cada material. Usa a pose de repouso (os peds do SA já guardam os vértices
/// em pé, no espaço do modelo), então não precisa aplicar o esqueleto.
/// </summary>
public static class DffMesh
{
    private const uint FrameList = 0x0E;
    private const uint Geometry = 0x0F;
    private const uint Clump = 0x10;
    private const uint Atomic = 0x14;
    private const uint GeometryList = 0x1A;
    private const uint MaterialList = 0x08;
    private const uint Material = 0x07;
    private const uint Texture = 0x06;
    private const uint String = 0x02;
    private const uint BinMesh = 0x50E;

    private const int FlagTriStrip = 0x01;
    private const int FlagPositions = 0x02;
    private const int FlagTextured = 0x04;
    private const int FlagPrelit = 0x08;
    private const int FlagNormals = 0x10;
    private const int FlagTextured2 = 0x80;
    private const int FlagNative = 0x01000000;

    public static DffModel Read(byte[] dff)
    {
        if (!RwChunk.TryRead(dff, 0, out var clump) || clump.Type != Clump)
            throw new InvalidDataException(L.T("Não é um arquivo .dff válido.", "Not a valid .dff file."));

        var geometries = new List<MeshGeometry?>();
        var atomics = new List<(int Frame, int Geometry)>();
        var frames = new List<Matrix4x4>();
        foreach (var c in Children(dff, clump))
        {
            if (c.Type == FrameList)
            {
                try { frames = ReadFrames(dff, c); }
                catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException) { frames = new(); }
            }
            else if (c.Type == GeometryList)
            {
                foreach (var g in Children(dff, c))
                    if (g.Type == Geometry)
                    {
                        MeshGeometry? geo = null;
                        try { geo = ReadGeometry(dff, g); }
                        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException) { }
                        geometries.Add(geo);
                    }
            }
            else if (c.Type == Atomic)
            {
                var st = Children(dff, c).FirstOrDefault(x => x.Type == RwChunk.Struct);
                if (st.Size >= 8)
                    atomics.Add(((int)BinaryPrimitives.ReadUInt32LittleEndian(dff.AsSpan(st.DataOffset)),
                                 (int)BinaryPrimitives.ReadUInt32LittleEndian(dff.AsSpan(st.DataOffset + 4))));
            }
        }

        var model = new DffModel();
        var used = atomics.Count > 0 ? atomics : Enumerable.Range(0, geometries.Count).Select(i => (Frame: -1, Geometry: i)).ToList();
        var done = new HashSet<int>();
        foreach (var (frame, i) in used)
        {
            if (i < 0 || i >= geometries.Count || geometries[i] is not { } g || g.Parts.Count == 0 || !done.Add(i)) continue;
            // a geometria fica no espaço do frame do atomic: leva para o espaço do modelo
            if (frame >= 0 && frame < frames.Count && !frames[frame].IsIdentity) g = Transformed(g, frames[frame]);
            model.Geometries.Add(g);
        }
        if (model.Geometries.Count == 0)
            throw new InvalidDataException(L.T("O .dff não tem geometria para mostrar.", "The .dff has no geometry to show."));
        return model;
    }

    /// <summary>Matriz de cada frame já no espaço do modelo (multiplicada pelos pais).</summary>
    private static List<Matrix4x4> ReadFrames(byte[] d, RwChunk list)
    {
        var st = Children(d, list).FirstOrDefault(k => k.Type == RwChunk.Struct);
        var result = new List<Matrix4x4>();
        if (st.Size < 4) return result;
        var s = d.AsSpan(st.DataOffset, (int)st.Size);
        int p = 0;
        int n = (int)U32(s, ref p);
        if (n < 0 || n > 4096) return result;
        for (int i = 0; i < n; i++)
        {
            float[] f = new float[12];
            for (int k = 0; k < 12; k++) f[k] = F32(s, ref p);
            int parent = (int)U32(s, ref p);
            p += 4; // flags
            // linhas: right, up, at, posição (vetor linha: v * M)
            var local = new Matrix4x4(f[0], f[1], f[2], 0, f[3], f[4], f[5], 0, f[6], f[7], f[8], 0, f[9], f[10], f[11], 1);
            result.Add(parent >= 0 && parent < i ? local * result[parent] : local);
        }
        return result;
    }

    private static MeshGeometry Transformed(MeshGeometry g, Matrix4x4 m)
    {
        var t = new MeshGeometry
        {
            Positions = g.Positions.Select(v => Vector3.Transform(v, m)).ToArray(),
            Uvs = g.Uvs,
            Normals = g.Normals?.Select(n => Vector3.Normalize(Vector3.TransformNormal(n, m))).ToArray(),
        };
        t.Parts.AddRange(g.Parts);
        return t;
    }

    private static MeshGeometry ReadGeometry(byte[] d, RwChunk geo)
    {
        var kids = Children(d, geo).ToList();
        var st = kids.First(k => k.Type == RwChunk.Struct);
        var s = d.AsSpan(st.DataOffset, (int)st.Size);
        int p = 0;
        int flags = (int)U32(s, ref p);
        int numTris = (int)U32(s, ref p);
        int numVerts = (int)U32(s, ref p);
        int numMorph = (int)U32(s, ref p);
        if (numVerts <= 0 || numVerts > 1_000_000 || numTris < 0 || numTris > 2_000_000)
            throw new InvalidDataException();
        if (Version(st.LibraryId) < 0x34000) p += 12; // ambiente/especular/difusa (versões antigas)

        var uvs = new Vector2[numVerts];
        var tris = new List<(int A, int B, int C, int Mat)>();
        if ((flags & FlagNative) == 0)
        {
            if ((flags & FlagPrelit) != 0) p += numVerts * 4;
            int sets = (flags >> 16) & 0xFF;
            if (sets == 0) sets = (flags & FlagTextured2) != 0 ? 2 : (flags & FlagTextured) != 0 ? 1 : 0;
            for (int set = 0; set < sets; set++)
                for (int i = 0; i < numVerts; i++)
                {
                    float u = F32(s, ref p), v = F32(s, ref p);
                    if (set == 0) uvs[i] = new Vector2(u, v);
                }
            for (int i = 0; i < numTris; i++)
            {
                int b = U16(s, ref p), a = U16(s, ref p), mat = U16(s, ref p), c = U16(s, ref p);
                tris.Add((a, b, c, mat));
            }
        }

        Vector3[]? positions = null;
        Vector3[]? normals = null;
        if (numMorph > 0)
        {
            p += 16; // esfera de contorno
            int hasVerts = (int)U32(s, ref p);
            int hasNormals = (int)U32(s, ref p);
            if (hasVerts != 0)
            {
                positions = new Vector3[numVerts];
                for (int i = 0; i < numVerts; i++) positions[i] = new Vector3(F32(s, ref p), F32(s, ref p), F32(s, ref p));
            }
            if (hasNormals != 0)
            {
                normals = new Vector3[numVerts];
                for (int i = 0; i < numVerts; i++) normals[i] = new Vector3(F32(s, ref p), F32(s, ref p), F32(s, ref p));
            }
        }
        if (positions is null) throw new InvalidDataException();

        // materiais
        var mats = new List<(string Tex, uint Color)>();
        var matList = kids.FirstOrDefault(k => k.Type == MaterialList);
        if (matList.Type == MaterialList) mats = ReadMaterials(d, matList);

        var geometry = new MeshGeometry { Positions = positions, Uvs = uvs, Normals = normals };
        MeshPart PartFor(int mat)
        {
            var (tex, col) = mat >= 0 && mat < mats.Count ? mats[mat] : ("", 0xFFFFFFFFu);
            var part = geometry.Parts.FirstOrDefault(x => x.Texture == tex && x.Color == col);
            if (part is null) geometry.Parts.Add(part = new MeshPart { Texture = tex, Color = col });
            return part;
        }

        // o Bin Mesh (listas/strips por material) é o mais confiável quando existe
        var ext = kids.FirstOrDefault(k => k.Type == RwChunk.Extension);
        var bin = ext.Type == RwChunk.Extension ? Children(d, ext).FirstOrDefault(k => k.Type == BinMesh) : default;
        bool fromBin = false;
        if (bin.Type == BinMesh && (flags & FlagNative) == 0)
        {
            try { fromBin = ReadBinMesh(d, bin, numVerts, PartFor); }
            catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException) { fromBin = false; }
            if (!fromBin) geometry.Parts.Clear();
        }
        if (!fromBin)
        {
            foreach (var (a, b, c, mat) in tris)
            {
                if (a >= numVerts || b >= numVerts || c >= numVerts) continue;
                var part = PartFor(mat);
                part.Indices.Add(a); part.Indices.Add(b); part.Indices.Add(c);
            }
        }
        geometry.Parts.RemoveAll(x => x.Indices.Count == 0);
        return geometry;
    }

    private static bool ReadBinMesh(byte[] d, RwChunk bin, int numVerts, Func<int, MeshPart> partFor)
    {
        var s = d.AsSpan(bin.DataOffset, (int)bin.Size);
        int p = 0;
        uint type = U32(s, ref p); // 0 = lista de triângulos, 1 = strip
        int splits = (int)U32(s, ref p);
        p += 4; // total de índices
        if (splits <= 0 || splits > 4096) return false;
        bool any = false;
        for (int k = 0; k < splits; k++)
        {
            int n = (int)U32(s, ref p);
            int mat = (int)U32(s, ref p);
            if (n < 0 || p + n * 4 > s.Length) return false;
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                idx[i] = (int)U32(s, ref p);
                if (idx[i] >= numVerts) return false;
            }
            var part = partFor(mat);
            if (type == 1)
            {
                for (int i = 2; i < n; i++)
                {
                    int a = idx[i - 2], b = idx[i - 1], c = idx[i];
                    if (a == b || b == c || a == c) continue; // triângulo degenerado
                    if ((i & 1) == 0) { part.Indices.Add(a); part.Indices.Add(b); part.Indices.Add(c); }
                    else { part.Indices.Add(b); part.Indices.Add(a); part.Indices.Add(c); }
                }
            }
            else
            {
                for (int i = 0; i + 2 < n; i += 3) { part.Indices.Add(idx[i]); part.Indices.Add(idx[i + 1]); part.Indices.Add(idx[i + 2]); }
            }
            any |= n > 0;
        }
        return any;
    }

    private static List<(string, uint)> ReadMaterials(byte[] d, RwChunk list)
    {
        var result = new List<(string, uint)>();
        var kids = Children(d, list).ToList();
        var st = kids.FirstOrDefault(k => k.Type == RwChunk.Struct);
        var refs = new List<int>();
        if (st.Size >= 4)
        {
            var s = d.AsSpan(st.DataOffset, (int)st.Size);
            int p = 0;
            int n = (int)U32(s, ref p);
            for (int i = 0; i < n && p + 4 <= s.Length; i++) refs.Add((int)U32(s, ref p));
        }
        var unique = new List<(string, uint)>();
        foreach (var m in kids.Where(k => k.Type == Material)) unique.Add(ReadMaterial(d, m));
        int next = 0;
        foreach (int r in refs)
        {
            if (r < 0) result.Add(next < unique.Count ? unique[next++] : ("", 0xFFFFFFFF));
            else result.Add(r < result.Count ? result[r] : ("", 0xFFFFFFFF));
        }
        if (refs.Count == 0) result.AddRange(unique);
        return result;
    }

    private static (string, uint) ReadMaterial(byte[] d, RwChunk mat)
    {
        string tex = "";
        uint color = 0xFFFFFFFF;
        foreach (var k in Children(d, mat))
        {
            if (k.Type == RwChunk.Struct && k.Size >= 8)
                color = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(k.DataOffset + 4));
            else if (k.Type == Texture)
            {
                var name = Children(d, k).FirstOrDefault(x => x.Type == String);
                if (name.Type == String) tex = ReadString(d.AsSpan(name.DataOffset, (int)name.Size));
            }
        }
        return (tex, color);
    }

    /// <summary>Versão da biblioteca RenderWare (ex.: 0x36003 para o SA).</summary>
    private static uint Version(uint libId) =>
        (libId & 0xFFFF0000) != 0 ? ((libId >> 14) & 0x3FF00) + 0x30000 | ((libId >> 16) & 0x3F) : libId << 8;

    private static IEnumerable<RwChunk> Children(byte[] d, RwChunk parent)
    {
        int pos = parent.DataOffset;
        while (RwChunk.TryRead(d, pos, out var c) && c.End <= parent.End)
        {
            yield return c;
            if (c.End <= pos) yield break;
            pos = c.End;
        }
    }

    private static uint U32(ReadOnlySpan<byte> s, ref int p) { uint v = BinaryPrimitives.ReadUInt32LittleEndian(s[p..]); p += 4; return v; }
    private static int U16(ReadOnlySpan<byte> s, ref int p) { int v = BinaryPrimitives.ReadUInt16LittleEndian(s[p..]); p += 2; return v; }
    private static float F32(ReadOnlySpan<byte> s, ref int p) { float v = BinaryPrimitives.ReadSingleLittleEndian(s[p..]); p += 4; return v; }

    private static string ReadString(ReadOnlySpan<byte> s)
    {
        int z = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(z < 0 ? s : s[..z]).Trim();
    }
}
