namespace PcOptimizer.Core.Abstractions;

/// <summary>
/// Base para modulos que solo leen. Aplicar nunca cambia nada, asi que la
/// implementacion es comun y no se puede olvidar por descuido en un modulo nuevo.
/// </summary>
public abstract class DiagnosticModule : IOptimizerModule
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public virtual string Description => string.Empty;

    public virtual bool RequiresElevation => false;

    public ModuleKind Kind => ModuleKind.Diagnostic;

    public abstract Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default);

    public Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Errors = ["Este modulo solo informa: no aplica cambios."]
        });
}
