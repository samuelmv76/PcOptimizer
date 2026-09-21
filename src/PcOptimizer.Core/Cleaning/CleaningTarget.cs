namespace PcOptimizer.Core.Cleaning;

/// <summary>
/// Una carpeta candidata a limpieza. Todo lo que el limpiador puede tocar
/// esta declarado aqui: no hay rutas escritas a mano dentro de la logica.
/// </summary>
public sealed record CleaningTarget(string Name, string Path)
{
    public string Pattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Antiguedad minima del fichero para considerarlo basura. Evita borrar
    /// temporales que una aplicacion abierta esta usando ahora mismo.
    /// </summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromHours(24);
}
