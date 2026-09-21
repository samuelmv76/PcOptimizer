namespace PcOptimizer.Core.Abstractions;

public sealed class RemediationResult
{
    public bool Simulated { get; init; }

    public int Applied { get; init; }

    public int Failed { get; init; }

    public long BytesFreed { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
