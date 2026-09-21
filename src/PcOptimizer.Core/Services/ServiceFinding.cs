using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Services;

public sealed class ServiceFinding : Finding
{
    public ServiceFinding(
        string serviceName,
        string displayName,
        string currentStartMode,
        string state,
        ServiceVerdict verdict,
        string reason,
        bool delicate)
        : base(serviceName, string.IsNullOrWhiteSpace(displayName) ? serviceName : displayName)
    {
        ServiceName = serviceName;
        CurrentStartMode = currentStartMode;
        Verdict = verdict;

        var status = state.Equals("Running", StringComparison.OrdinalIgnoreCase)
            ? "en ejecución"
            : "parado";

        Details = string.IsNullOrEmpty(reason)
            ? $"{serviceName} - inicio {currentStartMode.ToLowerInvariant()}, {status}"
            : $"{reason} ({serviceName}, inicio {currentStartMode.ToLowerInvariant()}, {status})";

        Recommendation = delicate
            ? "Léelo dos veces antes de marcarlo: mucha gente usa esto sin saber que se llama así. "
              + "Pasarlo a Manual no lo rompe del todo, pero puede dejar de arrancar solo."
            : verdict == ServiceVerdict.SafeToDefer
                ? "Pasa a inicio Manual. Windows podrá arrancarlo si algo lo necesita."
                : string.Empty;

        Severity = delicate
            ? FindingSeverity.Warning
            : verdict == ServiceVerdict.SafeToDefer
                ? FindingSeverity.Suggestion
                : FindingSeverity.Info;

        // Ningun servicio se marca solo. Es la parte de la aplicacion donde
        // un error del usuario duele mas, asi que se elige a mano.
        SelectedByDefault = false;
    }

    public string ServiceName { get; }

    public string CurrentStartMode { get; }

    public ServiceVerdict Verdict { get; }
}
