namespace PcOptimizer.Core.Abstractions;

public sealed class RemediationOptions
{
    /// <summary>
    /// Cuando es true no se modifica nada: el modulo calcula el resultado
    /// pero no toca disco, registro ni servicios. Es el valor por defecto
    /// a proposito: hay que pedir explicitamente la ejecucion real.
    /// </summary>
    public bool Simulate { get; init; } = true;

    /// <summary>Crear un punto de restauracion antes de aplicar cambios reales.</summary>
    public bool CreateRestorePoint { get; init; }

    public static RemediationOptions Preview { get; } = new() { Simulate = true };
}
