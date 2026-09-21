using System.Diagnostics;
using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Programs;

/// <summary>
/// Programas de escritorio instalados, leidos del registro de desinstalacion.
/// Es donde vive el bloatware del fabricante: antivirus de prueba, paneles de
/// HP o Lenovo, juegos con publicidad. Las apps de la Store las lleva otro
/// modulo, porque se quitan de otra forma.
///
/// Desinstalar NO se puede deshacer desde esta aplicacion: lo dice la interfaz
/// y por eso nada viene marcado por defecto.
/// </summary>
public sealed class InstalledProgramsModule : IOptimizerModule
{
    private static readonly (RegistryHive Hive, string Path)[] UninstallKeys =
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
    ];

    private readonly IAuditLog _audit;
    private readonly TimeSpan _uninstallTimeout;

    public InstalledProgramsModule(IAuditLog? audit = null, TimeSpan? uninstallTimeout = null)
    {
        _audit = audit ?? NullAuditLog.Instance;
        _uninstallTimeout = uninstallTimeout ?? TimeSpan.FromMinutes(10);
    }

    public string Id => "programs.installed";

    public string DisplayName => "Programas instalados";

    public string Description =>
        "Programas de escritorio, incluido el bloatware del fabricante. Los drivers y runtimes no aparecen: no se ofrecen para desinstalar.";

    public bool RequiresElevation => true;

    public string ApplyVerb => "Desinstalar";

    public bool BenefitsFromRestorePoint => true;

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var programs = Read(cancellationToken);
        return Task.FromResult(Classify(programs));
    }

    internal static IReadOnlyList<Finding> Classify(IEnumerable<InstalledProgram> programs)
    {
        var findings = new List<Finding>();

        foreach (var program in programs)
        {
            var verdict = ProgramCatalog.Classify(program);

            if (verdict == ProgramVerdict.Protected)
            {
                continue;
            }

            findings.Add(new ProgramFinding(program, verdict, ProgramCatalog.DescribeReason(program)));
        }

        return findings
            .OrderByDescending(f => ((ProgramFinding)f).Verdict == ProgramVerdict.Bloatware)
            .ThenBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<InstalledProgram> Read(CancellationToken cancellationToken)
    {
        var programs = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, path) in UninstallKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RegistryKey? root = null;
            RegistryKey? container = null;

            try
            {
                root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                container = root.OpenSubKey(path);
            }
            catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
            {
                root?.Dispose();
                continue;
            }

            using (root)
            using (container)
            {
                if (container is null)
                {
                    continue;
                }

                foreach (var name in container.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var program = ReadEntry(container, name, $"{hive}\\{path}\\{name}");

                    // Un programa de 32 bits aparece en dos vistas del
                    // registro: se lista una sola vez.
                    if (program is not null && seen.Add($"{program.Name}|{program.Version}"))
                    {
                        programs.Add(program);
                    }
                }
            }
        }

        return programs;
    }

    private static InstalledProgram? ReadEntry(RegistryKey container, string subKeyName, string fullPath)
    {
        try
        {
            using var entry = container.OpenSubKey(subKeyName);

            if (entry is null)
            {
                return null;
            }

            var name = entry.GetValue("DisplayName")?.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // Componentes del sistema y actualizaciones: no son programas que
            // el usuario reconozca ni deba quitar desde aqui.
            if (entry.GetValue("SystemComponent") is int systemComponent && systemComponent == 1)
            {
                return null;
            }

            if (entry.GetValue("ParentKeyName") is not null)
            {
                return null;
            }

            var releaseType = entry.GetValue("ReleaseType")?.ToString();

            if (releaseType is "Security Update" or "Update" or "Hotfix")
            {
                return null;
            }

            var uninstall = entry.GetValue("UninstallString")?.ToString() ?? string.Empty;
            var quiet = entry.GetValue("QuietUninstallString")?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(uninstall) && string.IsNullOrWhiteSpace(quiet))
            {
                return null;
            }

            var sizeInKb = entry.GetValue("EstimatedSize") as int? ?? 0;

            return new InstalledProgram
            {
                Name = name.Trim(),
                Publisher = entry.GetValue("Publisher")?.ToString()?.Trim() ?? string.Empty,
                Version = entry.GetValue("DisplayVersion")?.ToString()?.Trim() ?? string.Empty,
                EstimatedBytes = sizeInKb > 0 ? sizeInKb * 1024L : 0,
                UninstallCommand = uninstall,
                QuietUninstallCommand = quiet,
                RegistryKey = fullPath
            };
        }
        catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
        {
            return null;
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

        foreach (var finding in findings.OfType<ProgramFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Segunda comprobacion: un driver o runtime no se desinstala
            // aunque la interfaz lo hubiera ofrecido por error.
            if (ProgramCatalog.Classify(finding.Program) == ProgramVerdict.Protected)
            {
                failed++;
                errors.Add($"{finding.Title}: es un driver o componente del sistema, no se toca.");
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, "uninstall", finding.Program.Name, simulated: true);
                applied++;
                continue;
            }

            try
            {
                Uninstall(finding.Program, cancellationToken);
                _audit.Record(Id, "uninstall", finding.Program.Name, simulated: false);
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, "uninstall", finding.Program.Name, ex.Message);
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

    private void Uninstall(InstalledProgram program, CancellationToken cancellationToken)
    {
        var (executable, arguments) = BuildUninstallCommand(program);

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("No declara una forma de desinstalarse.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = program.SupportsSilentUninstall
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("No se pudo lanzar el desinstalador.");

        if (!process.WaitForExit((int)_uninstallTimeout.TotalMilliseconds))
        {
            throw new InvalidOperationException(
                "El desinstalador sigue abierto. Termínalo tú y vuelve a analizar.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 0 es correcto; 3010 significa "hecho, pero reinicia".
        if (process.ExitCode is not (0 or 3010))
        {
            throw new InvalidOperationException(
                $"El desinstalador terminó con el código {process.ExitCode}.");
        }
    }

    /// <summary>
    /// Elige la forma mas silenciosa disponible. Si el programa lo instalo
    /// Windows Installer se le puede pedir silencio aunque no declare una
    /// cadena propia; si no, se lanza su asistente tal cual.
    /// </summary>
    internal static (string Executable, string Arguments) BuildUninstallCommand(InstalledProgram program)
    {
        if (!string.IsNullOrWhiteSpace(program.QuietUninstallCommand))
        {
            return CommandLine.Split(program.QuietUninstallCommand);
        }

        var (executable, arguments) = CommandLine.Split(program.UninstallCommand);

        if (CommandLine.IsMsiExec(executable))
        {
            // Las cadenas de msiexec suelen venir como /I{GUID}: para
            // desinstalar hace falta /X.
            var normalized = arguments.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
            return (executable, $"{normalized} /quiet /norestart".Trim());
        }

        return (executable, arguments);
    }
}
