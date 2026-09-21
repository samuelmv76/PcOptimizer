using System.Globalization;
using System.Windows.Data;

namespace PcOptimizer.App.Converters;

/// <summary>
/// Icono de cada apartado, de la fuente de iconos del sistema (Segoe Fluent
/// Icons en Windows 11, Segoe MDL2 Assets en Windows 10: mismos codigos).
/// Es una decision de interfaz, asi que vive aqui y no en el nucleo.
/// </summary>
public sealed class ModuleGlyphConverter : IValueConverter
{
    public const string Settings = "";

    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.Ordinal)
    {
        ["summary"] = "",              // Inicio
        ["hardware.inventory"] = "",   // Monitor
        ["performance.gaming"] = "",   // Mando
        ["cleaning.temp"] = "",        // Papelera
        ["disk.space"] = "",           // Disco duro
        ["bloatware.appx"] = "",       // Tienda
        ["programs.installed"] = "",   // Todas las aplicaciones
        ["startup.manager"] = "",      // Encendido
        ["services.manager"] = "",     // Administrar
        ["firmware.diagnostics"] = "", // Diagnostico
        ["firmware.settings"] = "",    // Reiniciar / arranque
        ["undo.changes"] = "",         // Deshacer
        ["settings"] = Settings
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string id && Glyphs.TryGetValue(id, out var glyph) ? glyph : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
