using System.Text.Json;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Bloatware;

/// <summary>
/// Lista las aplicaciones preinstaladas (paquetes AppX) y permite quitarlas.
/// Lo que se puede quitar y lo que no esta en BloatwareCatalog, en listas
/// explicitas: este modulo solo aplica ese criterio.
/// </summary>
public sealed class BloatwareModule : IOptimizerModule
{
    private const string ListScript = """
        $ErrorActionPreference = 'Stop'
        $packages = Get-AppxPackage |
            Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } |
            ForEach-Object {
                [pscustomobject]@{
                    Name            = $_.Name
                    PackageFullName = $_.PackageFullName
                    Publisher       = $_.Publisher
                    Version         = $_.Version.ToString()
                    NonRemovable    = [bool]$_.NonRemovable
                }
            }
        ConvertTo-Json -InputObject @($packages) -Compress -Depth 3
        """;

    private readonly IPowerShellRunner _powerShell;
    private readonly IAuditLog _audit;

    public BloatwareModule(IPowerShellRunner? powerShell = null, IAuditLog? audit = null)
    {
        _powerShell = powerShell ?? new PowerShellRunner();
        _audit = audit ?? NullAuditLog.Instance;
    }

    public string Id => "bloatware.appx";

    public string DisplayName => "Aplicaciones preinstaladas";

    public string Description =>
        "Aplicaciones que vinieron con el equipo o con Windows. Las piezas del sistema no aparecen aquí: no se ofrecen para desinstalar.";

    public bool RequiresElevation => false;

    public string ApplyVerb => "Desinstalar";

    public bool BenefitsFromRestorePoint => true;

    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = await _powerShell.RunAsync(ListScript, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo leer la lista de aplicaciones: {Summarize(result.Error)}");
        }

        return Classify(ParsePackages(result.Output));
    }

    internal static IReadOnlyList<AppxPackageInfo> ParsePackages(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AppxPackageInfo>>(json) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"La lista de aplicaciones no se pudo interpretar: {ex.Message}");
        }
    }

    internal static IReadOnlyList<Finding> Classify(IEnumerable<AppxPackageInfo> packages)
    {
        var findings = new List<Finding>();

        foreach (var package in packages)
        {
            if (string.IsNullOrEmpty(package.Name) || string.IsNullOrEmpty(package.PackageFullName))
            {
                continue;
            }

            if (package.NonRemovable == true)
            {
                continue;
            }

            var classification = BloatwareCatalog.Classify(package.Name);

            if (classification == AppClassification.Protected)
            {
                continue;
            }

            findings.Add(new BloatwareFinding(
                package,
                classification,
                BloatwareCatalog.DescribeReason(package.Name),
                BloatwareCatalog.DescribeCaveat(package.Name)));
        }

        // Primero lo prescindible, luego el resto; dentro de cada grupo, por nombre.
        return findings
            .OrderByDescending(f => ((BloatwareFinding)f).Classification == AppClassification.Bloatware)
            .ThenBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<RemediationResult> ApplyAsync(
        IReadOnlyCollection<Finding> findings,
        RemediationOptions options,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var applied = 0;
        var failed = 0;

        foreach (var finding in findings.OfType<BloatwareFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Segunda comprobacion: aunque la interfaz nunca deberia ofrecerlo,
            // un paquete protegido no se desinstala pase lo que pase.
            if (BloatwareCatalog.Classify(finding.Package.Name) == AppClassification.Protected)
            {
                failed++;
                errors.Add($"{finding.Title}: es parte del sistema y no se desinstala.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, "remove-appx", finding.Package.PackageFullName, simulated: true);
                applied++;
                continue;
            }

            var result = await _powerShell
                .RunAsync(BuildRemovalScript(finding.Package.PackageFullName), cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                _audit.Record(Id, "remove-appx", finding.Package.PackageFullName, simulated: false);
                applied++;
            }
            else
            {
                var reason = Summarize(result.Error);
                failed++;
                _audit.RecordFailure(Id, "remove-appx", finding.Package.PackageFullName, reason);
                errors.Add($"{finding.Title}: {reason}");
            }
        }

        return new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Errors = errors
        };
    }

    /// <summary>
    /// Quita el paquete del usuario actual y, si se puede, tambien el paquete
    /// aprovisionado: sin eso Windows lo reinstala al crear una cuenta nueva.
    /// Aprovisionar requiere elevacion, asi que su fallo no es un fallo real.
    /// </summary>
    internal static string BuildRemovalScript(string packageFullName)
    {
        var quoted = PowerShellRunner.Quote(packageFullName);

        return $$"""
            $ErrorActionPreference = 'Stop'
            $full = {{quoted}}
            Remove-AppxPackage -Package $full
            try {
                Get-AppxProvisionedPackage -Online |
                    Where-Object { $_.PackageName -eq $full } |
                    Remove-AppxProvisionedPackage -Online -ErrorAction Stop | Out-Null
            } catch {
                Write-Output 'Aviso: no se pudo quitar el paquete aprovisionado (hace falta ejecutar como administrador).'
            }
            """;
    }

    private static string Summarize(string error)
    {
        var line = error
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrEmpty(line) ? "error desconocido" : line;
    }
}
