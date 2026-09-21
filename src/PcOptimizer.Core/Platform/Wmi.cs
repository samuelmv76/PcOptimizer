using System.Management;

namespace PcOptimizer.Core.Platform;

/// <summary>
/// Envoltura minima sobre WMI. Centraliza el manejo de errores: una consulta
/// que falla devuelve una secuencia vacia en vez de tumbar el analisis, porque
/// hay equipos donde ciertas clases no existen o el servicio esta capado.
/// </summary>
public static class Wmi
{
    public static IEnumerable<ManagementObject> Query(string query, string scope = "root\\CIMV2")
    {
        ManagementObjectCollection results;

        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            results = searcher.Get();
        }
        catch (ManagementException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        using (results)
        {
            foreach (var item in results)
            {
                if (item is ManagementObject managementObject)
                {
                    yield return managementObject;
                }
            }
        }
    }

    public static string GetString(this ManagementObject source, string property)
    {
        try
        {
            return source[property]?.ToString()?.Trim() ?? string.Empty;
        }
        catch (ManagementException)
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
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or FormatException or OverflowException)
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
        catch (ManagementException)
        {
            return null;
        }
    }
}
