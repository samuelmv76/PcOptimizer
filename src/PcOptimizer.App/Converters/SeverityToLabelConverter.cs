using System.Globalization;
using System.Windows.Data;
using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.App.Converters;

public sealed class SeverityToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FindingSeverity.Warning => "Atención",
            FindingSeverity.Suggestion => "Sugerencia",
            _ => "Información"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
