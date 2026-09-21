using System.Management;

namespace PcOptimizer.Core.Platform;

/// <summary>
/// Envoltura minima sobre WMI. Centraliza el manejo de errores: hay equipos
/// donde una clase no existe, el servicio esta capado o la enumeracion falla
/// a mitad. Nada de eso debe tumbar un analisis, asi que aqui se traduce en
/// una secuencia vacia o mas corta.
/// </summary>
public static class Wmi
{
    public static IEnumerable<ManagementObject> Query(string query, string scope = "root\\CIMV2")
    {
        ManagementObjectSearcher? searcher = null;
        ManagementObjectCollection? results = null;

        try
        {
            searcher = new ManagementObjectSearcher(scope, query);
            results = searcher.Get();
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            results = null;
        }

        if (results is null)
        {
            searcher?.Dispose();
            yield break;
        }

        try
        {
            // Get() es perezoso: el error real suele aparecer aqui, no arriba.
            var enumerator = results.GetEnumerator();

            while (true)
            {
                object? current;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    current = enumerator.Current;
                }
                catch (Exception ex) when (IsTolerable(ex))
                {
                    break;
                }

                if (current is ManagementObject managementObject)
                {
                    yield return managementObject;
                }
            }
        }
        finally
        {
            results.Dispose();
            searcher?.Dispose();
        }
    }

    public static string GetString(this ManagementObject source, string property)
    {
        try
        {
            return source[property]?.ToString()?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return string.Empty;
        }
    }

    public static ulong GetUInt64(this ManagementObject source, string property)
    {
        try
        {
            var value = source[property];
            return value is null ? 0 : Convert.ToUInt64(value);
        }
        catch (Exception ex) when (IsTolerable(ex) || ex is InvalidCastException or FormatException or OverflowException)
        {
            return 0;
        }
    }

    public static uint GetUInt32(this ManagementObject source, string property)
        => (uint)Math.Min(source.GetUInt64(property), uint.MaxValue);

    public static bool? GetBool(this ManagementObject source, string property)
    {
        try
        {
            return source[property] as bool?;
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return null;
        }
    }

    private static bool IsTolerable(Exception ex) =>
        ex is ManagementException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or NotSupportedException;
}
