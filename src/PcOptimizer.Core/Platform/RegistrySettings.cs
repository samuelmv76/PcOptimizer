using System.Globalization;
using Microsoft.Win32;

namespace PcOptimizer.Core.Platform;

/// <summary>
/// Un valor concreto del registro. Se escribe como texto en el diario de
/// cambios para poder volver a el mas tarde.
/// </summary>
public sealed record RegistryLocation(RegistryHive Hive, string SubKey, string Name)
{
    public const char NameSeparator = '!';

    public override string ToString() => $"{Hive}\\{SubKey}{NameSeparator}{Name}";

    public static RegistryLocation? Parse(string text)
    {
        var separator = text.LastIndexOf(NameSeparator);

        if (separator <= 0 || separator == text.Length - 1)
        {
            return null;
        }

        var path = text[..separator];
        var name = text[(separator + 1)..];
        var firstSlash = path.IndexOf('\\');

        if (firstSlash <= 0)
        {
            return null;
        }

        return Enum.TryParse<RegistryHive>(path[..firstSlash], out var hive)
            ? new RegistryLocation(hive, path[(firstSlash + 1)..], name)
            : null;
    }
}

/// <summary>
/// Lectura y escritura de valores sueltos del registro. Toda escritura
/// devuelve lo que habia antes, porque sin eso no hay vuelta atras.
/// </summary>
public static class RegistrySettings
{
    /// <summary>El valor actual, o null si la clave o el valor no existen.</summary>
    public static object? Read(RegistryLocation location)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Default);
            using var key = root.OpenSubKey(location.SubKey);
            return key?.GetValue(location.Name);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return null;
        }
    }

    public static int? ReadDword(RegistryLocation location)
        => Read(location) switch
        {
            int value => value,
            long value => (int)value,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

    /// <summary>
    /// Escribe el valor y devuelve el anterior en texto, o null si no existia.
    /// Lanza si no se puede escribir: quien llama decide como contarlo.
    /// </summary>
    public static string? Write(RegistryLocation location, object value, RegistryValueKind kind)
    {
        var previous = Read(location)?.ToString();

        using var root = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Default);
        using var key = root.CreateSubKey(location.SubKey, writable: true)
                        ?? throw new InvalidOperationException(
                            $"No se pudo abrir {location.Hive}\\{location.SubKey} para escritura.");

        key.SetValue(location.Name, value, kind);

        return previous;
    }

    /// <summary>Borra el valor. Usado al deshacer un ajuste que antes no existia.</summary>
    public static void Delete(RegistryLocation location)
    {
        using var root = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Default);
        using var key = root.OpenSubKey(location.SubKey, writable: true);

        key?.DeleteValue(location.Name, throwOnMissingValue: false);
    }

    /// <summary>
    /// Devuelve un valor a lo que era. Texto null significa que no existia,
    /// asi que deshacer es borrarlo.
    /// </summary>
    public static void Restore(RegistryLocation location, string? previousValue, string? valueKind)
    {
        if (previousValue is null)
        {
            Delete(location);
            return;
        }

        var kind = Enum.TryParse<RegistryValueKind>(valueKind, out var parsed)
            ? parsed
            : RegistryValueKind.String;

        object value = kind == RegistryValueKind.DWord
            ? int.Parse(previousValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : previousValue;

        Write(location, value, kind);
    }

    private static bool IsTolerable(Exception ex) =>
        ex is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException;
}
