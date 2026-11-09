using System.Windows;
using System.Windows.Media;
using SaSkinManager.Core;

namespace SaSkinManager.App.Services;

/// <summary>Uma cor de destaque disponível nas configurações.</summary>
public sealed record AccentOption(string Key, string Label, Color Color)
{
    public SolidColorBrush Swatch { get; } = new(Color);
    public override string ToString() => Label;
}

/// <summary>
/// Cores de destaque da interface. Os pincéis "Accent" e "AccentDim" são usados como DynamicResource
/// em todas as janelas e estilos, então trocar o pincel atualiza tudo na hora.
/// Todas as cores são claras o bastante para o texto escuro dos botões principais.
/// </summary>
public static class AccentColors
{
    public const string Default = "orange";

    private static readonly Color Bg = Color.FromRgb(0x16, 0x17, 0x1B);

    public static List<AccentOption> All() => new()
    {
        new("orange", L.T("Laranja", "Orange"), Color.FromRgb(0xF2, 0x9A, 0x2E)),
        new("green", L.T("Verde", "Green"), Color.FromRgb(0x3D, 0xD6, 0x7A)),
        new("yellow", L.T("Amarelo", "Yellow"), Color.FromRgb(0xF5, 0xC5, 0x18)),
        new("purple", L.T("Roxo", "Purple"), Color.FromRgb(0xB3, 0x88, 0xFF)),
        new("blue", L.T("Azul", "Blue"), Color.FromRgb(0x4F, 0xA8, 0xFF)),
        new("pink", L.T("Rosa", "Pink"), Color.FromRgb(0xFF, 0x7A, 0xC6)),
    };

    public static AccentOption Find(string? key) => All().FirstOrDefault(a => a.Key == key) ?? All()[0];

    /// <summary>Aplica a cor em todo o programa (janelas abertas incluídas).</summary>
    public static void Apply(string? key)
    {
        var c = Find(key).Color;
        var res = Application.Current.Resources;
        SetColor(res, "Accent", c);
        // fundo de item selecionado: a cor misturada com o fundo escuro
        SetColor(res, "AccentDim", Mix(c, Bg, 0.30));
    }

    // Os XAML usam {DynamicResource Accent}: trocar o pincel no dicionário atualiza todas as janelas.
    private static void SetColor(ResourceDictionary res, string name, Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        res[name] = brush;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R * t + b.R * (1 - t)),
        (byte)(a.G * t + b.G * (1 - t)),
        (byte)(a.B * t + b.B * (1 - t)));
}
