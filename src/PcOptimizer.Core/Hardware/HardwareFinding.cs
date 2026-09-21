using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Hardware;

/// <summary>
/// Una linea del inventario. No hay nada que corregir: existe para que el
/// usuario (y los modulos de recomendacion) sepan que hay dentro del equipo.
/// </summary>
public sealed class HardwareFinding : Finding
{
    public HardwareFinding(string category, string title, string details)
        : base($"hardware:{category}:{title}", title)
    {
        Category = category;
        Details = details;
        Severity = FindingSeverity.Info;
        SelectedByDefault = false;
    }

    public string Category { get; }
}
