namespace PcOptimizer.Core.Hardware;

/// <summary>
/// Una sola lectura del hardware para toda la aplicacion. Leerlo por WMI
/// cuesta segundos, y el inventario, el diagnostico de firmware y el modulo
/// de rendimiento lo necesitan los tres: sin esto, "Analizar todo" lo leeria
/// tres veces seguidas para obtener lo mismo.
/// </summary>
public sealed class HardwareProfileCache
{
    private readonly Func<HardwareProfile> _capture;
    private readonly TimeSpan _maxAge;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private HardwareProfile? _profile;
    private DateTimeOffset _capturedAt;

    public HardwareProfileCache(
        Func<HardwareProfile>? capture = null,
        TimeSpan? maxAge = null,
        TimeProvider? timeProvider = null)
    {
        _capture = capture ?? (() => HardwareInventory.Capture());

        // El hardware no cambia con el equipo encendido, pero un driver de
        // grafica si puede actualizarse. Cinco minutos equilibra las dos cosas.
        _maxAge = maxAge ?? TimeSpan.FromMinutes(5);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// El perfil guardado si es reciente; si no, una lectura nueva. Si varios
    /// modulos lo piden a la vez, solo uno lee y los demas esperan su resultado.
    /// </summary>
    public HardwareProfile Get()
    {
        lock (_gate)
        {
            if (_profile is not null && _time.GetUtcNow() - _capturedAt < _maxAge)
            {
                return _profile;
            }

            return CaptureLocked();
        }
    }

    /// <summary>Lectura nueva, ignorando lo guardado. La usa la pagina de hardware.</summary>
    public HardwareProfile Refresh()
    {
        lock (_gate)
        {
            return CaptureLocked();
        }
    }

    private HardwareProfile CaptureLocked()
    {
        _profile = _capture();
        _capturedAt = _time.GetUtcNow();
        return _profile;
    }
}
