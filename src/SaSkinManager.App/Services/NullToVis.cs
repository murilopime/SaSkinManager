using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaSkinManager.App.Services;

/// <summary>Visible quando o valor não é null (com ConverterParameter=invert, o contrário).</summary>
public sealed class NullToVis : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is not null;
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
