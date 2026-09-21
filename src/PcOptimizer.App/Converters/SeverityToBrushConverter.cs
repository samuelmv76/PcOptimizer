using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.App.Converters;

/// <summary>
/// Color de la franja lateral de cada ficha. El color es una pista, no la
/// unica senal: el texto siempre dice que pasa, para quien no distingue colores.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info = Freeze("#8E8E93");
    private static readonly SolidColorBrush Suggestion = Freeze("#0A84FF");
    private static readonly SolidColorBrush Warning = Freeze("#FF9F0A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FindingSeverity.Warning => Warning,
            FindingSeverity.Suggestion => Suggestion,
            _ => Info
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
