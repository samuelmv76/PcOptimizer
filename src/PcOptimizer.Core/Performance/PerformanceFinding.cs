using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Performance;

/// <summary>Que hay que hacer para aplicar un hallazgo de rendimiento.</summary>
public enum PerformanceAction
{
    /// <summary>Ya esta como deberia. Solo se informa.</summary>
    None,

    SetRegistryValue,

    SwitchPowerPlan
}

public sealed class PerformanceFinding : Finding
{
    private PerformanceFinding(string id, string title)
        : base(id, title)
    {
    }

    public PerformanceAction Action { get; private init; } = PerformanceAction.None;

    public RegistryLocation? Location { get; private init; }

    public int DesiredValue { get; private init; }

    public Guid PowerPlanId { get; private init; }

    public bool NeedsRestart { get; private init; }

    public static PerformanceFinding AlreadyCorrect(GamingTweak tweak) =>
        new($"performance:{tweak.Id}", tweak.Title)
        {
            Details = "Ya está como debería.",
            Severity = FindingSeverity.Info,
            SelectedByDefault = false
        };

    public static PerformanceFinding Pending(GamingTweak tweak, int? currentValue) =>
        new($"performance:{tweak.Id}", tweak.Title)
        {
            Action = PerformanceAction.SetRegistryValue,
            Location = tweak.Location,
            DesiredValue = tweak.DesiredValue,
            NeedsRestart = tweak.NeedsRestart,
            Details = Describe(tweak, currentValue),
            Recommendation = tweak.Note,
            Severity = tweak.Recommended ? FindingSeverity.Suggestion : FindingSeverity.Info,

            // Solo se marca solo lo que de verdad conviene en este equipo.
            // Lo demas se ofrece, pero la decision es del usuario.
            SelectedByDefault = tweak.Recommended
        };

    public static PerformanceFinding PowerPlan(Guid planId, string title, string details, string note, bool recommended) =>
        new("performance:power-plan", title)
        {
            Action = PerformanceAction.SwitchPowerPlan,
            PowerPlanId = planId,
            Details = details,
            Recommendation = note,
            Severity = recommended ? FindingSeverity.Suggestion : FindingSeverity.Info,
            SelectedByDefault = false
        };

    public static PerformanceFinding Information(string id, string title, string details, string note) =>
        new($"performance:{id}", title)
        {
            Details = details,
            Recommendation = note,
            Severity = FindingSeverity.Info,
            SelectedByDefault = false
        };

    private static string Describe(GamingTweak tweak, int? currentValue)
    {
        var current = currentValue is null
            ? "sin configurar"
            : currentValue.ToString();

        var restart = tweak.NeedsRestart ? " Requiere reiniciar." : string.Empty;

        return $"{tweak.Explanation} Ahora: {current}; debería ser {tweak.DesiredValue}.{restart}";
    }
}
