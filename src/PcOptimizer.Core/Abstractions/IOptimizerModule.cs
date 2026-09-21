namespace PcOptimizer.Core.Abstractions;

/// <summary>
/// Contrato de todo modulo de optimizacion. El ciclo es siempre el mismo:
/// analizar -> mostrar -> el usuario elige -> simular -> aplicar.
/// </summary>
public interface IOptimizerModule
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Si el modulo necesita ejecutarse elevado para funcionar por completo.</summary>
    bool RequiresElevation { get; }

    /// <summary>
    /// Si el modulo corrige cosas o solo informa. Por defecto corrige, para que
    /// los modulos existentes no tengan que declararlo.
    /// </summary>
    ModuleKind Kind => ModuleKind.Actionable;

    Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default);

    Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default);
}
