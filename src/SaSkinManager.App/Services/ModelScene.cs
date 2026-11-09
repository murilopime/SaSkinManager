using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SaSkinManager.Core.Formats;

namespace SaSkinManager.App.Services;

/// <summary>
/// Converte um DFF + TXD em um Model3D do WPF (congelado, pode ser criado fora da thread da interface)
/// e renderiza miniaturas 3D.
/// </summary>
public static class ModelScene
{
    /// <summary>Monta o modelo centralizado na origem, em pé no eixo Y e virado para +Z (para a câmera).</summary>
    public static Model3DGroup Build(byte[] dff, byte[]? txd)
    {
        var mesh = DffMesh.Read(dff);
        var textures = new Dictionary<string, (ImageBrush Brush, bool Alpha)>(StringComparer.OrdinalIgnoreCase);
        if (txd is not null)
        {
            foreach (var t in TxdReader.DecodeAll(txd))
            {
                if (textures.ContainsKey(t.Name)) continue;
                var bmp = BitmapSource.Create(t.Width, t.Height, 96, 96, PixelFormats.Bgra32, null, t.Bgra, t.Width * 4);
                bmp.Freeze();
                var brush = new ImageBrush(bmp)
                {
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1),
                    TileMode = TileMode.Tile,
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                brush.Freeze();
                textures[t.Name] = (brush, HasAlpha(t.Bgra));
            }
        }

        var orient = Orientation(mesh);
        var (min, max) = mesh.Bounds();
        var center = Vector3.Transform((min + max) / 2, orient);

        var opaque = new List<GeometryModel3D>();
        var blended = new List<GeometryModel3D>();
        foreach (var g in mesh.Geometries)
        foreach (var part in g.Parts)
        {
            var geo = new MeshGeometry3D();
            // só os vértices usados por esta parte, reindexados
            var map = new Dictionary<int, int>();
            var positions = new Point3DCollection();
            var uvs = new PointCollection();
            var normals = g.Normals is null ? null : new Vector3DCollection();
            var indices = new Int32Collection(part.Indices.Count);
            foreach (int i in part.Indices)
            {
                if (!map.TryGetValue(i, out int j))
                {
                    j = positions.Count;
                    map[i] = j;
                    var v = Vector3.Transform(g.Positions[i], orient) - center;
                    positions.Add(new Point3D(v.X, v.Y, v.Z));
                    var uv = i < g.Uvs.Length ? g.Uvs[i] : Vector2.Zero;
                    uvs.Add(new System.Windows.Point(uv.X, uv.Y));
                    if (normals is not null)
                    {
                        var n = Vector3.TransformNormal(g.Normals![i], orient);
                        normals.Add(new Vector3D(n.X, n.Y, n.Z));
                    }
                }
                indices.Add(j);
            }
            geo.Positions = positions;
            geo.TextureCoordinates = uvs;
            if (normals is not null) geo.Normals = normals;
            geo.TriangleIndices = indices;
            geo.Freeze();

            Brush brush;
            bool alpha = false;
            if (part.Texture.Length > 0 && textures.TryGetValue(part.Texture, out var tex))
            {
                brush = tex.Brush;
                alpha = tex.Alpha;
            }
            else
            {
                uint c = part.Color;
                brush = new SolidColorBrush(Color.FromRgb((byte)c, (byte)(c >> 8), (byte)(c >> 16)));
                brush.Freeze();
            }
            var material = new DiffuseMaterial(brush);
            uint mc = part.Color;
            if (part.Texture.Length > 0 && (mc & 0xFFFFFF) != 0xFFFFFF && (mc & 0xFFFFFF) != 0)
                material.Color = Color.FromRgb((byte)mc, (byte)(mc >> 8), (byte)(mc >> 16));
            material.Freeze();
            var model = new GeometryModel3D(geo, material) { BackMaterial = material };
            model.Freeze();
            (alpha ? blended : opaque).Add(model);
        }

        var group = new Model3DGroup();
        // as partes com transparência (cabelo, óculos) vão por último para misturar com o resto
        foreach (var m in opaque) group.Children.Add(m);
        foreach (var m in blended) group.Children.Add(m);
        group.Freeze();
        return group;
    }

    /// <summary>Null se o arquivo não puder ser lido como modelo.</summary>
    public static Model3DGroup? TryBuild(byte[]? dff, byte[]? txd)
    {
        if (dff is null) return null;
        try { return Build(dff, txd); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Os peds do jogo ficam em pé no eixo Y e olham para +Z (para a câmera). Modelos exportados de outros programas
    /// às vezes ficam em pé no Z ou no X: o eixo mais comprido vira o "em pé".
    /// </summary>
    private static Matrix4x4 Orientation(DffModel mesh)
    {
        var (min, max) = mesh.Bounds();
        var size = max - min;
        Matrix4x4 up = Matrix4x4.Identity;
        if (size.Z > size.Y * 1.2f && size.Z >= size.X) up = Matrix4x4.CreateRotationX(-MathF.PI / 2); // Z para cima
        else if (size.X > size.Y * 1.2f && size.X > size.Z) up = Matrix4x4.CreateRotationZ(MathF.PI / 2); // X para cima
        return up;
    }

    private static bool HasAlpha(byte[] bgra)
    {
        int count = 0;
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] < 250 && ++count > bgra.Length / 400) return true;
        return false;
    }

    /// <summary>Luzes fixas da cena (não giram com o modelo).</summary>
    public static Model3DGroup Lights()
    {
        var g = new Model3DGroup();
        g.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x78, 0x78)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0xB4, 0xB4, 0xB4), new Vector3D(-0.4, -0.5, -1)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(0x40, 0x40, 0x48), new Vector3D(0.6, 0.2, 1)));
        g.Freeze();
        return g;
    }

    /// <summary>Distância da câmera para o modelo caber no quadro.</summary>
    public static double FitDistance(Rect3D bounds, double aspect, double fovDeg)
    {
        if (bounds.IsEmpty) return 3;
        double h = bounds.SizeY;
        double w = Math.Max(bounds.SizeX, bounds.SizeZ); // girando, a largura muda: usa a maior
        double half = Math.Max(h / 2, w / 2 / Math.Max(aspect, 0.1));
        double t = Math.Tan(fovDeg * Math.PI / 360);
        return half / t + Math.Max(bounds.SizeZ, bounds.SizeX) / 2 + 0.05;
    }

    public const double Fov = 30;

    /// <summary>Renderiza uma miniatura (com supersampling) e salva em PNG. Precisa de uma thread STA.</summary>
    public static void RenderThumb(Model3D model, string path, int size, double yawDeg = 20)
    {
        int ss = size * 2;
        var bounds = model.Bounds;
        double dist = FitDistance(bounds, 1, Fov) * 0.92;
        var cam = new PerspectiveCamera(new Point3D(0, bounds.IsEmpty ? 0 : bounds.SizeY * 0.02, dist), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), Fov);
        var vp = new Viewport3DVisual { Camera = cam, Viewport = new Rect(0, 0, ss, ss) };
        vp.Children.Add(new ModelVisual3D { Content = Lights() });
        vp.Children.Add(new ModelVisual3D
        {
            Content = model,
            Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yawDeg)),
        });
        var rtb = new RenderTargetBitmap(ss, ss, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(vp);
        var small = new TransformedBitmap(rtb, new ScaleTransform(0.5, 0.5));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(small));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }
}
