using System.Windows.Markup;
using SaSkinManager.Core;

namespace SaSkinManager.App.Services;

/// <summary>
/// Texto traduzido no XAML: <c>Text="{s:Tr Pt='Personagens', En='Characters'}"</c>.
/// O valor é lido quando a janela é criada; trocar o idioma reabre a janela principal.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string pt, string en)
    {
        Pt = pt;
        En = en;
    }

    public string Pt { get; set; } = "";
    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Pt, En);
}
