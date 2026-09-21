using System.Management;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Services;

/// <summary>
/// Servicios de Windows que arrancan solos. Lo maximo que hace este modulo es
/// pasarlos a inicio Manual: nunca los desactiva y nunca los para. Asi, si
/// algo los necesita, Windows puede levantarlos y el equipo no se queda
/// cojo por una decision tomada a ciegas.
/// </summary>
public sealed class ServiceManager : IOptimizerModule
{
    public const string Manual = "Manual";

    private readonly IAuditLog _audit;
    private readonly IChangeJournal _journal;

    public ServiceManager(IAuditLog? audit = null, IChangeJournal? journal = null)
    {
        _audit = audit ?? NullAuditLog.Instance;
        _journal = journal ?? NullChangeJournal.Instance;
    }

    public string Id => "services.manager";

    public string DisplayName => "Servicios de Windows";

    public string Description =>
        "Servicios que arrancan con el equipo. Esta aplicación solo los pasa a inicio Manual, nunca los desactiva: si algo los necesita, Windows los levanta.";

    public bool RequiresElevation => true;

    public bool BenefitsFromRestorePoint => true;

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        foreach (var service in Wmi.Query("SELECT * FROM Win32_Service"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (service)
            {
                var name = service.GetString("Name");

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var startMode = service.GetString("StartMode");

                // Solo interesan los que arrancan solos: los que ya estan en
                // Manual o Disabled no cuestan nada en el arranque.
                if (!startMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var verdict = ServiceCatalog.Classify(name);

                if (verdict == ServiceVerdict.Protected)
                {
                    continue;
                }

                findings.Add(new ServiceFinding(
                    name,
                    service.GetString("DisplayName"),
                    startMode,
                    service.GetString("State"),
                    verdict,
                    ServiceCatalog.DescribeReason(name),
                    ServiceCatalog.IsDelicate(name)));
            }
        }

        // Primero lo catalogado como seguro, luego el resto, por nombre.
        return Task.FromResult<IReadOnlyList<Finding>>(findings
            .OrderByDescending(f => ((ServiceFinding)f).Verdict == ServiceVerdict.SafeToDefer)
            .ThenBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    public Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var applied = 0;
        var failed = 0;

        foreach (var finding in findings.OfType<ServiceFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Segunda comprobacion: un servicio protegido no se toca aunque
            // la interfaz lo hubiera ofrecido por error.
            if (ServiceCatalog.Classify(finding.ServiceName) == ServiceVerdict.Protected)
            {
                failed++;
                errors.Add($"{finding.Title}: Windows depende de el, no se toca.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, "set-start-mode", finding.ServiceName, simulated: true);
                applied++;
                continue;
            }

            try
            {
                SetStartMode(finding.ServiceName, Manual);

                _journal.Record(new ReversibleChange
                {
                    ModuleId = Id,
                    Kind = ChangeKinds.ServiceStartMode,
                    Target = finding.ServiceName,
                    PreviousValue = finding.CurrentStartMode,
                    NewValue = Manual,
                    Description = $"Servicio a inicio manual: {finding.Title}"
                });

                _audit.Record(Id, "set-start-mode", finding.ServiceName, simulated: false);
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, "set-start-mode", finding.ServiceName, ex.Message);
                errors.Add($"{finding.Title}: {ex.Message}");
            }
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Errors = errors
        });
    }

    /// <summary>
    /// Cambia el modo de inicio. Los valores que acepta WMI son "Automatic",
    /// "Manual" y "Disabled"; ojo, WMI devuelve "Auto" al leer pero exige
    /// "Automatic" al escribir.
    /// </summary>
    public static void SetStartMode(string serviceName, string startMode)
    {
        var normalized = startMode.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            ? "Automatic"
            : startMode;

        using var service = new ManagementObject(
            new ManagementPath($"Win32_Service.Name='{Escape(serviceName)}'"));

        var parameters = service.GetMethodParameters("ChangeStartMode");
        parameters["StartMode"] = normalized;

        var result = service.InvokeMethod("ChangeStartMode", parameters, null);
        var code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);

        if (code != 0)
        {
            throw new InvalidOperationException(DescribeWmiError(code));
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string DescribeWmiError(uint code) => code switch
    {
        2 => "acceso denegado: hace falta ejecutar como administrador",
        5 => "el servicio no acepta el cambio en su estado actual",
        15 => "el servicio está bloqueado",
        16 => "el servicio está marcado para borrarse",
        22 => "modo de inicio no válido",
        _ => $"Windows devolvió el código {code}"
    };
}
