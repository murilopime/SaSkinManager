using System.Buffers.Binary;

namespace SaSkinManager.Core.Formats;

/// <summary>
/// Inspeção rápida de DFF: um modelo de personagem tem esqueleto (plugins HAnim 0x11E e/ou Skin 0x116).
/// Carros, objetos e armas não têm. Funciona mesmo com só o começo do arquivo (o HAnim fica na FrameList, no início).
/// </summary>
public static class DffInfo
{
    public const uint SkinPlugin = 0x116;
    public const uint HAnimPlugin = 0x11E;

    /// <summary>Quantos bytes do começo do DFF bastam para achar o HAnim na maioria dos personagens.</summary>
    public const int PrefixBytes = 64 * 1024;

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

    public static bool IsSkinned(ReadOnlySpan<byte> dff)
    {
        if (RwChunk.PeekType(dff) != RwChunk.Clump) return false;
        return Walk(dff, 0, dff.Length, 0);
    }

    /// <summary>Percorre a árvore tolerando arquivo cortado (pode receber só o prefixo).</summary>
    private static bool Walk(ReadOnlySpan<byte> d, int start, int end, int depth)
    {
        if (depth > 8) return false;
        int pos = start;
        while (pos + RwChunk.HeaderSize <= Math.Min(end, d.Length))
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(d[pos..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(d[(pos + 4)..]);
            if (type == HAnimPlugin || type == SkinPlugin) return true;
            long childEnd = (long)pos + RwChunk.HeaderSize + size;
            if (size > int.MaxValue || childEnd > end) childEnd = end; // cortado ou corrompido
            if (Containers.Contains(type) && Walk(d, pos + RwChunk.HeaderSize, (int)childEnd, depth + 1))
                return true;
            if (childEnd <= pos) break;
            pos = (int)childEnd;
        }
        return false;
    }
}
