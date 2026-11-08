using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaSkinManager.Core.Formats;
using SaSkinManager.Core.Library;

namespace SaSkinManager.App.Services;

/// <summary>Converte texturas decodificadas em PNG e carrega miniaturas do disco.</summary>
public static class Thumbs
{
    public static void Save(DecodedTexture tex, string path, int max = 256)
    {
        var src = BitmapSource.Create(tex.Width, tex.Height, 96, 96, PixelFormats.Bgra32, null, tex.Bgra, tex.Width * 4);
        double s = Math.Min(1.0, (double)max / Math.Max(tex.Width, tex.Height));
        BitmapSource bmp = s < 1 ? new TransformedBitmap(src, new ScaleTransform(s, s)) : src;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public static ImageSource? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Gera thumb.png de uma skin da biblioteca a partir da maior textura dos seus .txd.</summary>
    public static void EnsureSkinThumb(SkinLibrary lib, LibrarySkin skin)
    {
        var path = lib.ThumbPath(skin);
        if (File.Exists(path)) return;
        DecodedTexture? best = null;
        foreach (var f in skin.Files.Where(f => f.EndsWith(".txd", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var t = TxdReader.DecodeBest(lib.ReadFile(skin, f));
                if (t is not null && (best is null || t.Width * t.Height > best.Width * best.Height)) best = t;
            }
            catch (IOException) { }
        }
        if (best is not null) Save(best, path);
    }
}
