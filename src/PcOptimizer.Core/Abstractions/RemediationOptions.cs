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

    /// <summary>
    /// Para un fichero que otro programa tiene abierto, pedir a Windows que
    /// lo borre en el proximo arranque. Es la via que usan los instaladores;
    /// requiere privilegios de administrador y no surte efecto hasta reiniciar.
    /// </summary>
    public bool DeleteLockedOnReboot { get; init; }

    public static RemediationOptions Preview { get; } = new() { Simulate = true };
}
