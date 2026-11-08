using System.Buffers.Binary;
using System.Text;

namespace SaSkinManager.Core.Formats;

public sealed class ImgEntry
{
    internal ImgEntry(int index, uint offset, ushort streamingSize, ushort archiveSize, byte[] rawName)
    {
        Index = index;
        Offset = offset;
        StreamingSize = streamingSize;
        ArchiveSize = archiveSize;
        RawName = rawName;
        int len = Array.IndexOf(rawName, (byte)0);
        Name = Encoding.ASCII.GetString(rawName, 0, len < 0 ? rawName.Length : len);
    }

    public int Index { get; }
    public string Name { get; }
    /// <summary>Posição em setores de 2048 bytes.</summary>
    public uint Offset { get; internal set; }
    public ushort StreamingSize { get; internal set; }
    public ushort ArchiveSize { get; internal set; }
    internal byte[] RawName { get; }

    public uint SizeInSectors => StreamingSize != 0 ? StreamingSize : ArchiveSize;
    public long ByteOffset => Offset * (long)ImgArchive.SectorSize;
    public long ByteLength => SizeInSectors * (long)ImgArchive.SectorSize;
}

/// <summary>
/// Arquivo IMG versão 2 (GTA San Andreas): "VER2", uint32 quantidade, depois N entradas de 32 bytes
/// (uint32 offset em setores, uint16 tamanho streaming, uint16 tamanho no arquivo, char[24] nome).
/// Os dados ficam alinhados em setores de 2048 bytes.
/// </summary>
public sealed class ImgArchive : IDisposable
{
    public const int SectorSize = 2048;
    public const int EntrySize = 32;
    public const int HeaderSize = 8;
    private static readonly byte[] Ver2 = "VER2"u8.ToArray();

    private readonly FileStream _fs;
    private readonly List<ImgEntry> _entries;
    private readonly Dictionary<string, ImgEntry> _byName;

    public string Path { get; }
    public bool Writable { get; }
    public IReadOnlyList<ImgEntry> Entries => _entries;

    private ImgArchive(string path, FileStream fs, bool writable, List<ImgEntry> entries)
    {
        Path = path;
        _fs = fs;
        Writable = writable;
        _entries = entries;
        _byName = new Dictionary<string, ImgEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) _byName.TryAdd(e.Name, e);
    }

    public static ImgArchive Open(string path, bool writable = false)
    {
        var fs = new FileStream(path, FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            writable ? FileShare.Read : FileShare.ReadWrite);
        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            fs.ReadExactly(header);
            if (!header[..4].SequenceEqual(Ver2))
                throw new NotSupportedException(
                    $"'{System.IO.Path.GetFileName(path)}' não é um IMG versão 2 (GTA San Andreas PC). " +
                    "A Definitive Edition e versões mobile usam outro formato.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (count > 200_000 || HeaderSize + (long)count * EntrySize > fs.Length)
                throw new InvalidDataException("Tabela do IMG corrompida.");

            var table = new byte[count * EntrySize];
            fs.ReadExactly(table);
            var entries = new List<ImgEntry>((int)count);
            for (int i = 0; i < count; i++)
            {
                var s = table.AsSpan(i * EntrySize, EntrySize);
                entries.Add(new ImgEntry(i,
                    BinaryPrimitives.ReadUInt32LittleEndian(s),
                    BinaryPrimitives.ReadUInt16LittleEndian(s[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(s[6..]),
                    s[8..32].ToArray()));
            }
            return new ImgArchive(path, fs, writable, entries);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public ImgEntry? Find(string name) => _byName.GetValueOrDefault(name);

    public bool Contains(string name) => _byName.ContainsKey(name);

    public byte[] Read(ImgEntry entry)
    {
        long len = entry.ByteLength;
        if (entry.ByteOffset + len > _fs.Length) len = Math.Max(0, _fs.Length - entry.ByteOffset);
        var buf = new byte[len];
        _fs.Position = entry.ByteOffset;
        _fs.ReadExactly(buf);
        return buf;
    }

    public byte[]? TryRead(string name) => Find(name) is { } e ? Read(e) : null;

    /// <summary>Lê só os primeiros <paramref name="maxBytes"/> de uma entrada (inspeção rápida).</summary>
    public byte[] ReadPrefix(ImgEntry entry, int maxBytes)
    {
        long len = Math.Min(entry.ByteLength, maxBytes);
        if (entry.ByteOffset + len > _fs.Length) len = Math.Max(0, _fs.Length - entry.ByteOffset);
        var buf = new byte[len];
        _fs.Position = entry.ByteOffset;
        _fs.ReadExactly(buf);
        return buf;
    }

    /// <summary>
    /// Substitui o conteúdo de uma entrada existente. Se couber nos setores atuais, grava no mesmo lugar;
    /// senão anexa no fim do arquivo e atualiza a tabela.
    /// </summary>
    public void Replace(string name, ReadOnlySpan<byte> data)
    {
        if (!Writable) throw new InvalidOperationException("IMG aberto somente para leitura.");
        var entry = Find(name) ?? throw new KeyNotFoundException($"'{name}' não existe em {System.IO.Path.GetFileName(Path)}.");

        long sectors = (data.Length + SectorSize - 1) / SectorSize;
        if (sectors == 0) sectors = 1;
        if (sectors > ushort.MaxValue) throw new InvalidDataException($"'{name}' é grande demais para um IMG.");

        long offsetSectors = sectors <= entry.SizeInSectors
            ? entry.Offset
            : (_fs.Length + SectorSize - 1) / SectorSize;
        if (offsetSectors > uint.MaxValue) throw new InvalidDataException("IMG grande demais.");

        _fs.Position = offsetSectors * SectorSize;
        _fs.Write(data);
        int pad = (int)(sectors * SectorSize - data.Length);
        if (pad > 0) _fs.Write(new byte[pad]);

        entry.Offset = (uint)offsetSectors;
        entry.StreamingSize = (ushort)sectors;
        entry.ArchiveSize = 0;
        WriteEntry(entry);
        _fs.Flush(true);
    }

    private void WriteEntry(ImgEntry e)
    {
        Span<byte> s = stackalloc byte[EntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(s, e.Offset);
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..], e.StreamingSize);
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..], e.ArchiveSize);
        e.RawName.CopyTo(s[8..]);
        _fs.Position = HeaderSize + (long)e.Index * EntrySize;
        _fs.Write(s);
    }

    /// <summary>Cria um IMG VER2 novo (usado em testes e ferramentas).</summary>
    public static void Create(string path, IEnumerable<(string Name, byte[] Data)> files)
    {
        var list = files.ToList();
        long dirBytes = HeaderSize + (long)list.Count * EntrySize;
        long sector = (dirBytes + SectorSize - 1) / SectorSize;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write(Ver2);
        w.Write((uint)list.Count);
        var offsets = new List<long>();
        long cur = sector;
        foreach (var (name, data) in list)
        {
            long secs = Math.Max(1, (data.Length + SectorSize - 1) / SectorSize);
            w.Write((uint)cur);
            w.Write((ushort)secs);
            w.Write((ushort)0);
            var nameBytes = new byte[24];
            Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 23), nameBytes, 0);
            w.Write(nameBytes);
            offsets.Add(cur);
            cur += secs;
        }
        for (int i = 0; i < list.Count; i++)
        {
            fs.Position = offsets[i] * SectorSize;
            w.Write(list[i].Data);
        }
        fs.SetLength(cur * SectorSize);
    }

    public void Dispose() => _fs.Dispose();
}
