using System.Buffers.Binary;
using System.Text;

namespace SaSkinManager.Core.Formats;

/// <summary>Textura decodificada em BGRA 32 bits (pronta para virar bitmap).</summary>
public sealed record DecodedTexture(string Name, int Width, int Height, byte[] Bgra);

/// <summary>
/// Leitor mínimo de TXD (texture dictionary RenderWare, PC D3D8/D3D9) para gerar miniaturas.
/// Suporta DXT1/3/5, 8888, 888, 1555, 565, 4444, 555, LUM8 e PAL8. Só o nível de mipmap 0.
/// </summary>
public static class TxdReader
{
    private const uint FourCcDxt1 = 0x31545844;
    private const uint FourCcDxt3 = 0x33545844;
    private const uint FourCcDxt5 = 0x35545844;

    /// <summary>Decodifica a maior textura do dicionário (normalmente o corpo/rosto do personagem).</summary>
    public static DecodedTexture? DecodeLargest(byte[] txd)
    {
        DecodedTexture? best = null;
        foreach (var t in DecodeAll(txd))
            if (best is null || t.Width * t.Height > best.Width * best.Height)
                best = t;
        return best;
    }

    public static IEnumerable<DecodedTexture> DecodeAll(byte[] txd)
    {
        if (!RwChunk.TryRead(txd, 0, out var dict) || dict.Type != RwChunk.TexDictionary)
            yield break;

        int pos = dict.DataOffset;
        while (RwChunk.TryRead(txd, pos, out var child) && child.End <= dict.End)
        {
            if (child.Type == RwChunk.TextureNative)
            {
                DecodedTexture? tex = null;
                try { tex = DecodeNative(txd, child); }
                catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException) { }
                if (tex is not null) yield return tex;
            }
            pos = child.End;
        }
    }

    private static DecodedTexture? DecodeNative(byte[] data, RwChunk native)
    {
        if (!RwChunk.TryRead(data, native.DataOffset, out var st) || st.Type != RwChunk.Struct) return null;
        var s = data.AsSpan(st.DataOffset, (int)st.Size);
        int p = 0;
        uint platform = U32(s, ref p);
        if (platform != 8 && platform != 9) return null; // PS2/Xbox não suportados
        p += 4; // filtro + endereçamento
        string name = ReadName(s.Slice(p, 32)); p += 64; // nome + nome da máscara
        uint rasterFormat = U32(s, ref p);
        uint d3dFormat = U32(s, ref p); // D3D9: formato/FourCC; D3D8: hasAlpha
        int width = BinaryPrimitives.ReadUInt16LittleEndian(s[p..]); p += 2;
        int height = BinaryPrimitives.ReadUInt16LittleEndian(s[p..]); p += 2;
        int depth = s[p++];
        p++; // níveis
        p++; // tipo do raster
        int flags = s[p++];

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

        int compression = 0;
        if (platform == 9)
        {
            compression = d3dFormat switch { FourCcDxt1 => 1, FourCcDxt3 => 3, FourCcDxt5 => 5, _ => 0 };
        }
        else
        {
            compression = flags; // D3D8: 0 = sem compressão, 1 = DXT1, 3 = DXT3
        }

        byte[]? palette = null;
        if ((rasterFormat & 0x2000) != 0)
        {
            palette = s.Slice(p, 256 * 4).ToArray();
            p += 256 * 4;
        }
        else if ((rasterFormat & 0x4000) != 0)
        {
            return null; // PAL4 é raro em skins; ignorado
        }

        int dataSize = (int)U32(s, ref p);
        if (dataSize < 0 || p + dataSize > s.Length) throw new InvalidDataException();
        var pix = s.Slice(p, dataSize);

        byte[] bgra = compression switch
        {
            1 => Dxt.DecodeDxt1(pix, width, height),
            3 => Dxt.DecodeDxt3(pix, width, height),
            5 => Dxt.DecodeDxt5(pix, width, height),
            _ => palette is not null
                ? DecodePal8(pix, palette, width, height)
                : DecodeRaw(pix, rasterFormat & 0xF00, depth, width, height),
        };
        return new DecodedTexture(name, width, height, bgra);
    }

    private static byte[] DecodePal8(ReadOnlySpan<byte> pix, byte[] pal, int w, int h)
    {
        if (pix.Length < w * h) throw new InvalidDataException();
        var o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int c = pix[i] * 4;
            o[i * 4 + 0] = pal[c + 2];
            o[i * 4 + 1] = pal[c + 1];
            o[i * 4 + 2] = pal[c + 0];
            o[i * 4 + 3] = pal[c + 3];
        }
        return o;
    }

    private static byte[] DecodeRaw(ReadOnlySpan<byte> pix, uint fmt, int depth, int w, int h)
    {
        int n = w * h;
        var o = new byte[n * 4];
        switch (fmt)
        {
            case 0x500: // 8888 (B,G,R,A na memória)
            case 0x600: // 888 (B,G,R,X)
                if (pix.Length < n * 4) throw new InvalidDataException();
                pix[..(n * 4)].CopyTo(o);
                if (fmt == 0x600) for (int i = 0; i < n; i++) o[i * 4 + 3] = 255;
                break;
            case 0x100: // 1555
            case 0x200: // 565
            case 0x300: // 4444
            case 0xA00: // 555
                if (pix.Length < n * 2) throw new InvalidDataException();
                for (int i = 0; i < n; i++)
                {
                    int v = pix[i * 2] | (pix[i * 2 + 1] << 8);
                    byte r, g, b, a;
                    switch (fmt)
                    {
                        case 0x100:
                            a = (byte)((v & 0x8000) != 0 ? 255 : 0);
                            r = X5((v >> 10) & 31); g = X5((v >> 5) & 31); b = X5(v & 31);
                            break;
                        case 0x200:
                            a = 255; r = X5((v >> 11) & 31); g = X6((v >> 5) & 63); b = X5(v & 31);
                            break;
                        case 0x300:
                            a = (byte)(((v >> 12) & 15) * 17); r = (byte)(((v >> 8) & 15) * 17);
                            g = (byte)(((v >> 4) & 15) * 17); b = (byte)((v & 15) * 17);
                            break;
                        default:
                            a = 255; r = X5((v >> 10) & 31); g = X5((v >> 5) & 31); b = X5(v & 31);
                            break;
                    }
                    o[i * 4] = b; o[i * 4 + 1] = g; o[i * 4 + 2] = r; o[i * 4 + 3] = a;
                }
                break;
            case 0x400: // LUM8
                if (pix.Length < n) throw new InvalidDataException();
                for (int i = 0; i < n; i++) { o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = pix[i]; o[i * 4 + 3] = 255; }
                break;
            default:
                throw new InvalidDataException($"Formato de raster 0x{fmt:X} (profundidade {depth}) não suportado.");
        }
        return o;
    }

    private static byte X5(int v) => (byte)((v << 3) | (v >> 2));
    private static byte X6(int v) => (byte)((v << 2) | (v >> 4));

    private static uint U32(ReadOnlySpan<byte> s, ref int p)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(s[p..]);
        p += 4;
        return v;
    }

    private static string ReadName(ReadOnlySpan<byte> s)
    {
        int z = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(z < 0 ? s : s[..z]);
    }
}

/// <summary>Decodificador DXT (S3TC) para BGRA.</summary>
public static class Dxt
{
    public static byte[] DecodeDxt1(ReadOnlySpan<byte> src, int w, int h) => Decode(src, w, h, 1);
    public static byte[] DecodeDxt3(ReadOnlySpan<byte> src, int w, int h) => Decode(src, w, h, 3);
    public static byte[] DecodeDxt5(ReadOnlySpan<byte> src, int w, int h) => Decode(src, w, h, 5);

    private static byte[] Decode(ReadOnlySpan<byte> src, int w, int h, int kind)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        int blockSize = kind == 1 ? 8 : 16;
        if (src.Length < bw * bh * blockSize) throw new InvalidDataException("Dados DXT incompletos.");
        var o = new byte[w * h * 4];
        Span<byte> colors = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[16];
        Span<byte> a8 = stackalloc byte[8];

        for (int by = 0; by < bh; by++)
        for (int bx = 0; bx < bw; bx++)
        {
            var blk = src.Slice((by * bw + bx) * blockSize, blockSize);
            var col = kind == 1 ? blk : blk[8..];
            alphas.Fill(255);

            if (kind == 3)
            {
                for (int i = 0; i < 16; i++)
                {
                    int nib = (blk[i / 2] >> ((i & 1) * 4)) & 15;
                    alphas[i] = (byte)(nib * 17);
                }
            }
            else if (kind == 5)
            {
                a8[0] = blk[0]; a8[1] = blk[1];
                if (a8[0] > a8[1])
                    for (int i = 1; i < 7; i++) a8[i + 1] = (byte)(((7 - i) * a8[0] + i * a8[1]) / 7);
                else
                {
                    for (int i = 1; i < 5; i++) a8[i + 1] = (byte)(((5 - i) * a8[0] + i * a8[1]) / 5);
                    a8[6] = 0; a8[7] = 255;
                }
                ulong bits = 0;
                for (int i = 0; i < 6; i++) bits |= (ulong)blk[2 + i] << (8 * i);
                for (int i = 0; i < 16; i++) alphas[i] = a8[(int)((bits >> (3 * i)) & 7)];
            }

            int c0 = col[0] | (col[1] << 8);
            int c1 = col[2] | (col[3] << 8);
            Rgb(c0, colors[..4]);
            Rgb(c1, colors.Slice(4, 4));
            bool fourColor = kind != 1 || c0 > c1;
            for (int ch = 0; ch < 3; ch++)
            {
                if (fourColor)
                {
                    colors[8 + ch] = (byte)((2 * colors[ch] + colors[4 + ch]) / 3);
                    colors[12 + ch] = (byte)((colors[ch] + 2 * colors[4 + ch]) / 3);
                }
                else
                {
                    colors[8 + ch] = (byte)((colors[ch] + colors[4 + ch]) / 2);
                    colors[12 + ch] = 0;
                }
            }
            colors[3] = colors[7] = colors[11] = 255;
            colors[15] = (byte)(fourColor ? 255 : 0);

            uint idx = (uint)(col[4] | (col[5] << 8) | (col[6] << 16) | (col[7] << 24));
            for (int i = 0; i < 16; i++)
            {
                int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                if (x >= w || y >= h) continue;
                int c = (int)((idx >> (2 * i)) & 3) * 4;
                int d = (y * w + x) * 4;
                o[d] = colors[c + 2];     // B
                o[d + 1] = colors[c + 1]; // G
                o[d + 2] = colors[c];     // R
                o[d + 3] = kind == 1 ? colors[c + 3] : alphas[i];
            }
        }
        return o;
    }

    private static void Rgb(int v, Span<byte> rgba)
    {
        int r = (v >> 11) & 31, g = (v >> 5) & 63, b = v & 31;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }
}
