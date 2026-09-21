namespace PcOptimizer.Core.Safety;

/// <summary>
/// Diario de cambios reversibles. Distinto del registro de auditoria: aquel
/// cuenta lo que paso, este guarda lo necesario para deshacerlo.
/// </summary>
public interface IChangeJournal
{
    void Record(ReversibleChange change);

    IReadOnlyList<ReversibleChange> ReadAll();

    /// <summary>Marca un cambio como ya deshecho para no ofrecerlo dos veces.</summary>
    void MarkReverted(string changeId);
}

public sealed class NullChangeJournal : IChangeJournal
{
    public static NullChangeJournal Instance { get; } = new();

    public void Record(ReversibleChange change)
    {
    }

    public IReadOnlyList<ReversibleChange> ReadAll() => [];

    public void MarkReverted(string changeId)
    {
    }
}
