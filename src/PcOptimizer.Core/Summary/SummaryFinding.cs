using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Summary;

/// <summary>La ficha de un apartado en la pagina de resumen.</summary>
public sealed class SummaryFinding : Finding
{
    public SummaryFinding(string id, string title)
        : base($"summary:{id}", title)
    {
        SelectedByDefault = false;
    }

    /// <summary>Cuantos hallazgos del apartado merecen atencion.</summary>
    public int PendingCount { get; init; }
}
