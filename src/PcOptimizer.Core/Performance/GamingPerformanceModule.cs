using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Performance;

/// <summary>
/// Ajustes de Windows que afectan a los juegos, decididos segun el hardware
/// que tiene el equipo. Todo lo que cambia queda anotado con su valor
/// anterior en el diario de cambios, asi que se puede deshacer.
/// </summary>
public sealed class GamingPerformanceModule : IOptimizerModule
{
    private readonly IAuditLog _audit;
    private readonly IChangeJournal _journal;
    private readonly Func<HardwareProfile> _profileSource;

    public GamingPerformanceModule(
        IAuditLog? audit = null,
        IChangeJournal? journal = null,
        Func<HardwareProfile>? profileSource = null)
    {
        _audit = audit ?? NullAuditLog.Instance;
        _journal = journal ?? NullChangeJournal.Instance;
        _profileSource = profileSource ?? (() => HardwareInventory.Capture());
    }

    public string Id => "performance.gaming";

    public string DisplayName => "Rendimiento en juegos";

    public string Description =>
        "Ajustes de Windows que afectan a los juegos, elegidos según el hardware de este equipo. Todo queda anotado para poder deshacerlo.";

    public bool RequiresElevation => true;

    public bool BenefitsFromRestorePoint => true;

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profile = _profileSource();
        var findings = new List<Finding>();

        foreach (var tweak in GamingTweaks.For(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = RegistrySettings.ReadDword(tweak.Location);

            findings.Add(current == tweak.DesiredValue
                ? PerformanceFinding.AlreadyCorrect(tweak)
                : PerformanceFinding.Pending(tweak, current));
        }

        findings.AddRange(CheckPowerPlan(profile));
        findings.AddRange(DescribeHardwareLimits(profile));

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<Finding> CheckPowerPlan(HardwareProfile profile)
    {
        var plans = PowerPlans.List();

        if (plans.Count == 0)
        {
            yield break;
        }

        var active = plans.FirstOrDefault(p => p.IsActive);

        if (active is null)
        {
            yield break;
        }

        if (active.Id == PowerPlans.HighPerformance)
        {
            yield return PerformanceFinding.Information(
                "power-plan",
                "Plan de energía",
                $"Activo: {active.Name}.",
                profile.IsPortable
                    ? "En un portátil este plan reduce bastante la autonomía y sube la temperatura. "
                      + "Si no estás jugando, el plan equilibrado es mejor idea."
                    : "Ya estás en el plan de máximo rendimiento.");

            yield break;
        }

        var highPerformance = plans.FirstOrDefault(p => p.Id == PowerPlans.HighPerformance);

        if (highPerformance is null)
        {
            yield return PerformanceFinding.Information(
                "power-plan",
                "Plan de energía",
                $"Activo: {active.Name}. El plan de alto rendimiento no está disponible en este equipo.",
                "Algunos fabricantes lo ocultan. Puedes recrearlo con: "
                + "powercfg -duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

            yield break;
        }

        var onBattery = profile.IsPortable;

        yield return PerformanceFinding.PowerPlan(
            PowerPlans.HighPerformance,
            "Plan de energía",
            $"Activo: {active.Name}. Se puede cambiar a {highPerformance.Name}.",
            onBattery
                ? "Ojo: es un portátil. El alto rendimiento impide que la CPU baje de frecuencia, "
                  + "así que gasta más batería y calienta más incluso navegando. "
                  + "Para jugar enchufado está bien; para el día a día, no."
                : "Evita que la CPU baje de frecuencia entre cargas. En equipos modernos la "
                  + "diferencia en juegos suele ser pequeña, y consume y calienta algo más.",
            recommended: !onBattery);
    }

    /// <summary>
    /// Lo que de verdad limita el rendimiento no suele ser un ajuste del
    /// registro. Si el cuello de botella es el hardware, hay que decirlo.
    /// </summary>
    private static IEnumerable<Finding> DescribeHardwareLimits(HardwareProfile profile)
    {
        if (profile.MemoryModules.Count == 1)
        {
            yield return PerformanceFinding.Information(
                "single-channel",
                "La RAM trabaja en canal simple",
                "Hay un solo módulo de memoria instalado.",
                "Añadir un segundo módulo igual habilita el doble canal. Es la mejora más "
                + "barata que existe, y con gráfica integrada puede ser enorme. "
                + "Ningún ajuste de Windows compensa esto.");
        }

        if (profile.Disks.Any(d => d.IsSystemDisk && d.MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase)))
        {
            yield return PerformanceFinding.Information(
                "system-hdd",
                "Windows está en un disco mecánico",
                "El disco del sistema es un HDD.",
                "Los tiempos de carga de los juegos y del propio Windows dependen de esto mucho "
                + "más que de cualquier ajuste de esta lista. Un SSD es la mejora real.");
        }

        if (profile.PrimaryGpu is { } gpu
            && gpu.DriverDate is { } issued
            && DateTime.Now - issued > TimeSpan.FromDays(365))
        {
            yield return PerformanceFinding.Information(
                "old-gpu-driver",
                "El driver de la gráfica tiene más de un año",
                $"{gpu.Name}, driver de {issued:dd/MM/yyyy}.",
                "Los juegos recientes suelen traer optimizaciones específicas en drivers nuevos. "
                + "Actualizarlo da más que todos los ajustes de esta página juntos.");
        }
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

        foreach (var finding in findings.OfType<PerformanceFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (finding.Action == PerformanceAction.None)
            {
                failed++;
                errors.Add($"{finding.Title}: no hay nada que aplicar, es informativo.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, DescribeAction(finding), finding.Title, simulated: true);
                applied++;
                continue;
            }

            try
            {
                Execute(finding);
                _audit.Record(Id, DescribeAction(finding), finding.Title, simulated: false);
                applied++;
                needsRestart |= finding.NeedsRestart;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, DescribeAction(finding), finding.Title, ex.Message);
                errors.Add($"{finding.Title}: {ex.Message}");
            }
        }

        if (needsRestart)
        {
            errors.Add("Algunos ajustes no surten efecto hasta que reinicies el equipo.");
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Errors = errors,
            RequiresRestart = needsRestart
        });
    }

    private void Execute(PerformanceFinding finding)
    {
        switch (finding.Action)
        {
            case PerformanceAction.SetRegistryValue:
                ApplyRegistryValue(finding);
                break;

            case PerformanceAction.SwitchPowerPlan:
                ApplyPowerPlan(finding);
                break;

            default:
                throw new NotSupportedException($"Acción no soportada: {finding.Action}");
        }
    }

    private void ApplyRegistryValue(PerformanceFinding finding)
    {
        var location = finding.Location
                       ?? throw new InvalidOperationException("El ajuste no indica qué valor cambiar.");

        var previous = RegistrySettings.Write(location, finding.DesiredValue, RegistryValueKind.DWord);

        _journal.Record(new ReversibleChange
        {
            ModuleId = Id,
            Kind = ChangeKinds.RegistryValue,
            Target = location.ToString(),
            PreviousValue = previous,
            NewValue = finding.DesiredValue.ToString(),
            ValueKind = nameof(RegistryValueKind.DWord),
            Description = finding.Title,
            NeedsRestart = finding.NeedsRestart
        });
    }

    private void ApplyPowerPlan(PerformanceFinding finding)
    {
        var previous = PowerPlans.GetActive();

        PowerPlans.SetActive(finding.PowerPlanId);

        _journal.Record(new ReversibleChange
        {
            ModuleId = Id,
            Kind = ChangeKinds.PowerPlan,
            Target = finding.PowerPlanId.ToString(),
            PreviousValue = previous?.ToString(),
            NewValue = finding.PowerPlanId.ToString(),
            Description = finding.Title
        });
    }

    private static string DescribeAction(PerformanceFinding finding) => finding.Action switch
    {
        PerformanceAction.SetRegistryValue => "set-registry-value",
        PerformanceAction.SwitchPowerPlan => "switch-power-plan",
        _ => "none"
    };
}
