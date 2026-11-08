using System.Buffers.Binary;

namespace SaSkinManager.Core.Formats;

/// <summary>Cabeçalho de chunk RenderWare (12 bytes): tipo, tamanho e versão da biblioteca.</summary>
public readonly record struct RwChunk(uint Type, uint Size, uint LibraryId, int DataOffset)
{
    public const uint Struct = 0x01;
    public const uint Extension = 0x03;
    public const uint TextureNative = 0x15;
    public const uint TexDictionary = 0x16;
    public const uint Clump = 0x10;

    public const int HeaderSize = 12;

    public int End => DataOffset + (int)Size;

    public static bool TryRead(ReadOnlySpan<byte> data, int offset, out RwChunk chunk)
    {
        chunk = default;
        if (offset < 0 || offset + HeaderSize > data.Length) return false;
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
        uint lib = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8)..]);
        long end = (long)offset + HeaderSize + size;
        if (end > data.Length) return false;
        chunk = new RwChunk(type, size, lib, offset + HeaderSize);
        return true;
    }

    /// <summary>Tipo do primeiro chunk do arquivo, ou null se não for um arquivo RenderWare.</summary>
    public static uint? PeekType(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize ? BinaryPrimitives.ReadUInt32LittleEndian(data) : null;

    public static bool IsDff(ReadOnlySpan<byte> data) => PeekType(data) == Clump;
    public static bool IsTxd(ReadOnlySpan<byte> data) => PeekType(data) == TexDictionary;
}
