using System.Buffers.Binary;

namespace SaSkinManager.Core.Formats;

/// <summary>
/// Inspeção rápida de DFF para saber se é um personagem. Personagens têm um esqueleto completo
/// (plugin HAnim 0x11E com ~32 ossos ou mais). Objetos animados das cutscenes (cartas, portas, cadeiras)
/// também podem ter HAnim, mas com poucos ossos. Carros, objetos comuns e armas não têm.
/// Funciona com só o começo do arquivo: o HAnim fica na FrameList, no início.
/// </summary>
public static class DffInfo
{
    public const uint SkinPlugin = 0x116;
    public const uint HAnimPlugin = 0x11E;

    /// <summary>Quantos bytes do começo do DFF bastam para achar o HAnim na maioria dos personagens.</summary>
    public const int PrefixBytes = 64 * 1024;

    /// <summary>
    /// Mínimo de ossos para contar como personagem. Medido no jogo real: os peds têm 32 ossos
    /// (as versões de cutscene têm 33–61, por causa do rosto); carros e objetos animados das
    /// cutscenes têm de 1 a 30.
    /// </summary>
    public const int MinCharacterBones = 32;

    // Chunks que contêm outros chunks (o Struct 0x01 é dado bruto e não é percorrido).
    private static readonly HashSet<uint> Containers = new()
    {
        0x10, // Clump
        0x0E, // FrameList
        0x1A, // GeometryList
        0x0F, // Geometry
        0x14, // Atomic
        0x08, // MaterialList
        0x07, // Material
        0x06, // Texture
        0x03, // Extension
    };

    /// <summary>Tem qualquer esqueleto (HAnim ou Skin), mesmo de objeto.</summary>
    public static bool IsSkinned(ReadOnlySpan<byte> dff)
    {
        if (RwChunk.PeekType(dff) != RwChunk.Clump) return false;
        var r = Walk(dff, 0, dff.Length, 0);
        return r.Found;
    }

    /// <summary>Maior número de ossos declarado num HAnim do arquivo (0 se não tiver).</summary>
    public static int BoneCount(ReadOnlySpan<byte> dff)
    {
        if (RwChunk.PeekType(dff) != RwChunk.Clump) return 0;
        return Walk(dff, 0, dff.Length, 0).Bones;
    }

    /// <summary>É um personagem (esqueleto completo), e não um objeto animado.</summary>
    public static bool IsCharacter(ReadOnlySpan<byte> dff) => BoneCount(dff) >= MinCharacterBones;

    private readonly record struct WalkResult(bool Found, int Bones)
    {
        public WalkResult Merge(WalkResult o) => new(Found || o.Found, Math.Max(Bones, o.Bones));
    }

    /// <summary>Percorre a árvore tolerando arquivo cortado (pode receber só o prefixo).</summary>
    private static WalkResult Walk(ReadOnlySpan<byte> d, int start, int end, int depth)
    {
        var result = new WalkResult(false, 0);
        if (depth > 8) return result;
        int pos = start;
        while (pos + RwChunk.HeaderSize <= Math.Min(end, d.Length))
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(d[pos..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(d[(pos + 4)..]);
            int data = pos + RwChunk.HeaderSize;
            if (type == SkinPlugin) result = result.Merge(new(true, 0));
            if (type == HAnimPlugin)
            {
                // HAnim: versão (4), id do nó (4), número de ossos (4); só o nó raiz declara os ossos
                int bones = 0;
                if (size >= 12 && data + 12 <= d.Length)
                {
                    uint n = BinaryPrimitives.ReadUInt32LittleEndian(d[(data + 8)..]);
                    if (n <= 512) bones = (int)n;
                }
                result = result.Merge(new(true, bones));
            }
            long childEnd = (long)data + size;
            if (size > int.MaxValue || childEnd > end) childEnd = end; // cortado ou corrompido
            if (Containers.Contains(type))
                result = result.Merge(Walk(d, data, (int)childEnd, depth + 1));
            if (childEnd <= pos) break;
            pos = (int)childEnd;
        }
        return result;
    }
}
