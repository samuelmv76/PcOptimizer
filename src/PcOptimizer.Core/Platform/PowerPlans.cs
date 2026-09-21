using System.Runtime.InteropServices;
using System.Text;

namespace PcOptimizer.Core.Platform;

public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>
/// Planes de energia de Windows. Se usa la API de powrprof en vez de parsear
/// la salida de powercfg: esa salida cambia con el idioma y la pagina de
/// codigos, y un optimizador que solo funciona en ingles no sirve de nada.
/// </summary>
public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private const uint AccessScheme = 16;
    private const uint ErrorMoreData = 234;
    private const uint Success = 0;

    public static IReadOnlyList<PowerPlan> List()
    {
        var active = GetActive();
        var plans = new List<PowerPlan>();

        for (uint index = 0; ; index++)
        {
            var size = (uint)Marshal.SizeOf<Guid>();
            var buffer = new byte[size];

            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                AccessScheme, index, buffer, ref size);

            if (result != Success)
            {
                break;
            }

            var id = new Guid(buffer);
            plans.Add(new PowerPlan(id, ReadFriendlyName(id), id == active));
        }

        return plans;
    }

    public static Guid? GetActive()
    {
        var pointer = IntPtr.Zero;

        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out pointer) != Success || pointer == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStructure<Guid>(pointer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                LocalFree(pointer);
            }
        }
    }

    /// <summary>Activa un plan. Lanza si Windows lo rechaza.</summary>
    public static void SetActive(Guid id)
    {
        var scheme = id;
        var result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);

        if (result != Success)
        {
            throw new InvalidOperationException(
                $"Windows no pudo activar el plan de energía (código {result}).");
        }
    }

    private static string ReadFriendlyName(Guid id)
    {
        var scheme = id;
        uint size = 0;

        var probe = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);

        if (probe != ErrorMoreData && probe != Success)
        {
            return id.ToString();
        }

        var buffer = new byte[size];

        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != Success)
        {
            return id.ToString();
        }

        // La API devuelve UTF-16 terminada en nulo.
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
