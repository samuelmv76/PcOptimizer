namespace PcOptimizer.Core.Abstractions;

/// <summary>
/// Contrato de todo modulo de optimizacion. El ciclo es siempre el mismo:
/// analizar -> mostrar -> el usuario elige -> simular -> aplicar.
/// </summary>
public interface IOptimizerModule
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Una frase que explica que hace el modulo, para la cabecera de la pagina.</summary>
    string Description => string.Empty;

    /// <summary>Si el modulo necesita ejecutarse elevado para funcionar por completo.</summary>
    bool RequiresElevation { get; }

    /// <summary>
    /// Si el modulo corrige cosas o solo informa. Por defecto corrige, para que
    /// los modulos existentes no tengan que declararlo.
    /// </summary>
    ModuleKind Kind => ModuleKind.Actionable;

    /// <summary>
    /// Si un punto de restauracion sirve de algo antes de aplicar. Restaurar
    /// sistema cubre registro, servicios y programas instalados; NO cubre
    /// ficheros de usuario ni temporales, asi que ofrecerlo en un modulo de
    /// limpieza solo llena el disco de puntos inutiles.
    /// </summary>
    bool BenefitsFromRestorePoint => false;

    /// <summary>
    /// Si el modulo sabe aprovechar DeleteLockedOnReboot. Solo tiene sentido
    /// en modulos que borran ficheros.
    /// </summary>
    bool SupportsRebootDeletion => false;

    /// <summary>
    /// Como se llama la accion en el boton. "Aplicar" no describe bien
    /// desinstalar un programa ni deshacer un cambio.
    /// </summary>
    string ApplyVerb => "Aplicar";

    Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default);

    Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default);
}
