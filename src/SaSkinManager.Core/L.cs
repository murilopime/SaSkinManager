using System.Globalization;

namespace SaSkinManager.Core;

/// <summary>
/// Idioma do programa. Os textos ficam no próprio código, em pares português/inglês:
/// <c>L.T("Pasta não encontrada.", "Folder not found.")</c>.
/// </summary>
public static class L
{
    public const string Auto = "auto";
    public const string Portuguese = "pt";
    public const string English = "en";

    /// <summary>Idioma em uso: "pt" ou "en".</summary>
    public static string Lang { get; private set; } = Resolve(Auto);

    public static bool IsEnglish => Lang == English;

    /// <summary>Aplica a preferência salva ("auto", "pt" ou "en").</summary>
    public static void Set(string? preference) => Lang = Resolve(preference);

    /// <summary>"auto" segue o idioma do Windows: português se for pt-*, senão inglês.</summary>
    public static string Resolve(string? preference) => preference switch
    {
        Portuguese => Portuguese,
        English => English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? Portuguese : English,
    };

    public static string T(string pt, string en) => IsEnglish ? en : pt;
}
