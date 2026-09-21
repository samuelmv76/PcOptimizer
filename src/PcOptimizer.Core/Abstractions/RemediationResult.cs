namespace PcOptimizer.Core.Abstractions;

public sealed class RemediationResult
{
    public bool Simulated { get; init; }

    public int Applied { get; init; }

    public int Failed { get; init; }

    /// <summary>
    /// Elementos que no se pudieron aplicar ahora pero quedan programados
    /// para el proximo arranque. Ni correctos ni fallidos: pendientes.
    /// </summary>
    public int Deferred { get; init; }

    public long BytesFreed { get; init; }

    /// <summary>
    /// Algo de lo aplicado no surte efecto hasta reiniciar. La interfaz lo usa
    /// para ofrecer reiniciar en ese momento.
    /// </summary>
    public bool RequiresRestart { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
