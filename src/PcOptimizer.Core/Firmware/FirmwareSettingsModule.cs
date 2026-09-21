using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Firmware;

/// <summary>
/// Cambios de arranque que se aplican al reiniciar, por las vias que UEFI y
/// Windows preven para ello: abrir la BIOS, arrancar una vez desde otro
/// disco, e integridad de memoria. No escribe la configuracion propia de la
/// placa (XMP, virtualizacion...): en placas de PC montado no hay forma
/// segura de hacerlo desde Windows, y el diagnostico dice donde cambiarlo.
/// </summary>
public sealed class FirmwareSettingsModule : IOptimizerModule
{
    public const string ModuleId = "firmware.settings";

    private static readonly RegistryLocation MemoryIntegrity = new(
        RegistryHive.LocalMachine,
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
        "Enabled");

    private readonly IAuditLog _audit;
    private readonly IChangeJournal _journal;

    public FirmwareSettingsModule(IAuditLog? audit = null, IChangeJournal? journal = null)
    {
        _audit = audit ?? NullAuditLog.Instance;
        _journal = journal ?? NullChangeJournal.Instance;
    }

    public string Id => ModuleId;

    public string DisplayName => "Arranque y BIOS";

    public string Description =>
        "Cambios que se aplican al reiniciar: abrir la BIOS, arrancar una vez desde otro disco o USB, e integridad de memoria. Todo queda anotado para deshacerlo.";

    public bool RequiresElevation => true;

    public bool BenefitsFromRestorePoint => true;

    public string ApplyVerb => "Programar";

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<Finding>();

        if (!UefiVariables.IsAvailable())
        {
            findings.Add(new FirmwareSettingFinding("legacy", "Firmware no accesible", FirmwareAction.None)
            {
                Details = "El equipo arranca en modo Legacy, o la aplicación no tiene permisos de administrador.",
                Severity = FindingSeverity.Info,
                Recommendation = "Las opciones de arranque necesitan UEFI. La integridad de memoria sí se puede cambiar."
            });
        }
        else
        {
            findings.AddRange(DescribeSetupAccess());
            findings.AddRange(DescribeBootEntries());
        }

        findings.Add(DescribeMemoryIntegrity());

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<Finding> DescribeSetupAccess()
    {
        if (!BootEntries.FirmwareSupportsBootToSetup())
        {
            yield return new FirmwareSettingFinding("setup", "Abrir la BIOS al reiniciar", FirmwareAction.None)
            {
                Details = "Tu firmware no admite que Windows le pida abrir la configuración.",
                Severity = FindingSeverity.Info,
                Recommendation = "Entra pulsando Supr o F2 justo al encender, antes de que aparezca el logo de Windows."
            };
            yield break;
        }

        if (BootEntries.IsBootToSetupPending())
        {
            yield return new FirmwareSettingFinding("setup", "Abrir la BIOS al reiniciar", FirmwareAction.None)
            {
                Details = "Ya está programado: el próximo reinicio entrará en la BIOS.",
                Severity = FindingSeverity.Info,
                Recommendation = "Para cancelarlo, ve a Deshacer cambios."
            };
            yield break;
        }

        yield return new FirmwareSettingFinding("setup", "Abrir la BIOS al reiniciar", FirmwareAction.OpenSetupOnRestart)
        {
            Details = "En el próximo reinicio el equipo entra directo en la configuración de la placa.",
            Severity = FindingSeverity.Suggestion,
            Recommendation = "Es la forma de cambiar XMP, virtualización o Secure Boot en una placa de PC montado. "
                             + "Firmware y BIOS te dice en qué menú está cada ajuste."
        };
    }

    private static IEnumerable<Finding> DescribeBootEntries()
    {
        var current = BootEntries.ReadBootCurrent();

        foreach (var entry in BootEntries.Read())
        {
            var isCurrent = entry.Number == current;

            yield return new FirmwareSettingFinding($"boot:{entry.Number:X4}", $"Arrancar una vez desde: {entry.Description}",
                isCurrent ? FirmwareAction.None : FirmwareAction.BootOnce)
            {
                BootNumber = entry.Number,
                Details = isCurrent
                    ? "Es desde donde ha arrancado Windows ahora mismo."
                    : "Solo en el próximo reinicio. Después vuelve al orden de siempre, sin tocar nada.",
                Severity = FindingSeverity.Info,
                Recommendation = isCurrent
                    ? string.Empty
                    : "Útil para arrancar un USB de instalación o de rescate sin cambiar el orden en la BIOS. "
                      + "Si marcas varias, gana la última."
            };
        }
    }

    private static Finding DescribeMemoryIntegrity()
    {
        var enabled = RegistrySettings.ReadDword(MemoryIntegrity) == 1;

        return new FirmwareSettingFinding(
            "hvci",
            enabled ? "Desactivar la integridad de memoria" : "Activar la integridad de memoria",
            FirmwareAction.SetMemoryIntegrity)
        {
            Enable = !enabled,
            Details = enabled
                ? "Ahora está activada. Se aplica al reiniciar."
                : "Ahora está desactivada. Se aplica al reiniciar.",
            Severity = enabled ? FindingSeverity.Warning : FindingSeverity.Info,
            Recommendation = enabled
                ? "Desactivarla puede recuperar unos pocos fotogramas en juegos, a cambio de menos protección frente a "
                  + "drivers maliciosos. Algunos anticheats la exigen activada. Decisión tuya, y se puede deshacer."
                : "Protege frente a drivers maliciosos. Si algún driver antiguo es incompatible, Windows no la activará "
                  + "y te lo dirá Seguridad de Windows."
        };
    }

    public Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var applied = 0;
        var failed = 0;
        var restart = false;

        foreach (var finding in findings.OfType<FirmwareSettingFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (finding.Action == FirmwareAction.None)
            {
                failed++;
                errors.Add($"{finding.Title}: es informativo, no hay nada que programar.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, finding.Action.ToString(), finding.Title, simulated: true);
                applied++;
                continue;
            }

            try
            {
                Execute(finding);
                _audit.Record(Id, finding.Action.ToString(), finding.Title, simulated: false);
                applied++;
                restart = true;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, finding.Action.ToString(), finding.Title, ex.Message);
                errors.Add($"{finding.Title}: {ex.Message}");
            }
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Errors = errors,
            RequiresRestart = restart
        });
    }

    private void Execute(FirmwareSettingFinding finding)
    {
        switch (finding.Action)
        {
            case FirmwareAction.OpenSetupOnRestart:
            {
                var before = UefiVariables.Read("OsIndications");
                var value = BootEntries.DecodeUInt64(before) | BootEntries.BootToFirmwareUi;
                WriteVariable("OsIndications", before, BootEntries.EncodeUInt64(value), finding.Title);
                break;
            }

            case FirmwareAction.BootOnce:
                WriteVariable("BootNext", UefiVariables.Read("BootNext"),
                    BootEntries.EncodeBootNext(finding.BootNumber), finding.Title);
                break;

            case FirmwareAction.SetMemoryIntegrity:
            {
                var previous = RegistrySettings.Write(MemoryIntegrity, finding.Enable ? 1 : 0, RegistryValueKind.DWord);

                _journal.Record(new ReversibleChange
                {
                    ModuleId = Id,
                    Kind = ChangeKinds.RegistryValue,
                    Target = MemoryIntegrity.ToString(),
                    PreviousValue = previous,
                    NewValue = finding.Enable ? "1" : "0",
                    ValueKind = nameof(RegistryValueKind.DWord),
                    Description = finding.Title,
                    NeedsRestart = true
                });
                break;
            }

            default:
                throw new NotSupportedException($"Acción no soportada: {finding.Action}");
        }
    }

    private void WriteVariable(string name, byte[]? before, byte[] after, string description)
    {
        UefiVariables.Write(name, after);

        _journal.Record(new ReversibleChange
        {
            ModuleId = Id,
            Kind = ChangeKinds.FirmwareVariable,
            Target = name,
            PreviousValue = before is null ? null : UefiVariables.ToHex(before),
            NewValue = UefiVariables.ToHex(after),
            Description = description,
            NeedsRestart = true
        });
    }
}
