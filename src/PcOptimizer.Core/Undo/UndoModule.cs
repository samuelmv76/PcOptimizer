using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Services;
using PcOptimizer.Core.Startup;

namespace PcOptimizer.Core.Undo;

/// <summary>
/// Deshace lo que esta aplicacion ha cambiado, usando el valor anterior que
/// guardo en el diario. Existe porque una herramienta que sabe desactivar y
/// no reactivar es una trampa: el usuario no puede probar nada sin miedo.
/// </summary>
public sealed class UndoModule : IOptimizerModule
{
    private readonly IChangeJournal _journal;
    private readonly IAuditLog _audit;

    public UndoModule(IChangeJournal? journal = null, IAuditLog? audit = null)
    {
        _journal = journal ?? NullChangeJournal.Instance;
        _audit = audit ?? NullAuditLog.Instance;
    }

    public string Id => "undo.changes";

    public string DisplayName => "Deshacer cambios";

    public string Description =>
        "Todo lo que esta aplicación ha cambiado en el equipo, con su valor anterior. Desde aquí se puede revertir.";

    public bool RequiresElevation => true;

    public string ApplyVerb => "Deshacer";

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = _journal.ReadAll()
            .Where(change => !change.Reverted)
            .Select(change => (Finding)new UndoFinding(change, CanRevert(change)))
            .ToList();

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    public Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var applied = 0;
        var failed = 0;
        var needsRestart = false;

        foreach (var finding in findings.OfType<UndoFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var change = finding.Change;

            if (!CanRevert(change))
            {
                failed++;
                errors.Add($"{finding.Title}: esta versión no sabe deshacer este tipo de cambio.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, "revert", change.Target, simulated: true);
                applied++;
                continue;
            }

            try
            {
                Revert(change);
                _journal.MarkReverted(change.Id);
                _audit.Record(Id, "revert", change.Target, simulated: false);
                applied++;
                needsRestart |= change.NeedsRestart;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, "revert", change.Target, ex.Message);
                errors.Add($"{finding.Title}: {ex.Message}");
            }
        }

        if (needsRestart)
        {
            errors.Add("Algunos cambios no vuelven a su sitio hasta que reinicies el equipo.");
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Errors = errors,
            RequiresRestart = needsRestart && !options.Simulate
        });
    }

    /// <summary>
    /// Si el diario trae lo necesario para revertir este cambio. Se comprueba
    /// antes de ofrecerlo: prometer una vuelta atras que luego falla es peor
    /// que no ofrecerla.
    /// </summary>
    private static readonly HashSet<string> RevertibleFirmwareVariables = new(StringComparer.Ordinal)
    {
        "BootNext",
        "OsIndications"
    };

    private static bool IsHexOrEmpty(string? value)
        => string.IsNullOrEmpty(value)
           || (value.Length % 2 == 0 && value.All(Uri.IsHexDigit));

    internal static bool CanRevert(ReversibleChange change) => change.Kind switch
    {
        ChangeKinds.RegistryValue => RegistryLocation.Parse(change.Target) is not null,

        ChangeKinds.StartupEntry => !string.IsNullOrEmpty(change.PreviousValue)
                                    && change.Target.Contains(':'),

        ChangeKinds.PowerPlan => Guid.TryParse(change.PreviousValue, out _),

        ChangeKinds.ServiceStartMode => !string.IsNullOrEmpty(change.PreviousValue)
                                        && !string.IsNullOrEmpty(change.Target),

        // Solo las variables que esta aplicacion escribe. El diario es un
        // fichero de texto: si alguien lo editara para apuntar a otra
        // variable del firmware, deshacer no debe obedecerle.
        ChangeKinds.FirmwareVariable => RevertibleFirmwareVariables.Contains(change.Target)
                                        && IsHexOrEmpty(change.PreviousValue),

        _ => false
    };

    private static void Revert(ReversibleChange change)
    {
        switch (change.Kind)
        {
            case ChangeKinds.RegistryValue:
            {
                var location = RegistryLocation.Parse(change.Target)
                               ?? throw new InvalidOperationException(
                                   $"Ruta de registro no reconocida: {change.Target}");

                RegistrySettings.Restore(location, change.PreviousValue, change.ValueKind);
                break;
            }

            case ChangeKinds.StartupEntry:
                StartupManager.Restore(change.Target, change.PreviousValue!);
                break;

            case ChangeKinds.PowerPlan:
                PowerPlans.SetActive(Guid.Parse(change.PreviousValue!));
                break;

            case ChangeKinds.FirmwareVariable:
                // Sin valor anterior, la variable no existia: se borra.
                UefiVariables.Write(change.Target, UefiVariables.FromHex(change.PreviousValue));
                break;

            case ChangeKinds.ServiceStartMode:
                ServiceManager.SetStartMode(change.Target, change.PreviousValue!);
                break;

            default:
                throw new NotSupportedException($"Tipo de cambio no soportado: {change.Kind}");
        }
    }
}
