using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Startup;

/// <summary>
/// Lista los programas que arrancan con Windows y permite desactivarlos.
/// Desactivar no borra: la entrada se copia a una clave de respaldo propia
/// para poder restaurarla despues.
/// </summary>
public sealed class StartupManager : IOptimizerModule
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string BackupKey = @"Software\PcOptimizer\DisabledStartup";

    private readonly IAuditLog _audit;
    private readonly IChangeJournal _journal;

    public StartupManager(IAuditLog? audit = null, IChangeJournal? journal = null)
    {
        _audit = audit ?? NullAuditLog.Instance;
        _journal = journal ?? NullChangeJournal.Instance;
    }

    public string Id => "startup.manager";

    public string DisplayName => "Programas al inicio";

    public string Description =>
        "Programas que arrancan con Windows. Desactivar no borra nada: la entrada se guarda en una clave de respaldo para poder restaurarla.";

    public bool RequiresElevation => true;

    public bool BenefitsFromRestorePoint => true;

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        ReadRunKey(Registry.CurrentUser, StartupLocation.CurrentUserRun, findings);
        ReadRunKey(Registry.LocalMachine, StartupLocation.LocalMachineRun, findings);
        ReadStartupFolder(findings);

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

        foreach (var entry in findings.OfType<StartupEntry>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!options.Simulate)
                {
                    Disable(entry);

                    _journal.Record(new ReversibleChange
                    {
                        ModuleId = Id,
                        Kind = ChangeKinds.StartupEntry,
                        Target = entry.Id,
                        PreviousValue = entry.Command,
                        NewValue = null,
                        Description = $"Arranque desactivado: {entry.Name}"
                    });
                }

                _audit.Record(Id, "disable-startup", entry.Id, options.Simulate);
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, "disable-startup", entry.Id, ex.Message);
                errors.Add($"{entry.Name}: {ex.Message}");
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

    private static void ReadRunKey(RegistryKey root, StartupLocation location, List<Finding> findings)
    {
        using var key = root.OpenSubKey(RunKey);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            var command = key.GetValue(name)?.ToString() ?? string.Empty;
            findings.Add(new StartupEntry(location, name, command));
        }
    }

    private static void ReadStartupFolder(List<Finding> findings)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                findings.Add(new StartupEntry(
                    StartupLocation.StartupFolder,
                    Path.GetFileNameWithoutExtension(file),
                    file));
            }
        }
    }

    /// <summary>
    /// Vuelve a activar una entrada desactivada por esta aplicacion. El
    /// identificador es el mismo que se guardo en el diario de cambios:
    /// "Ubicacion:Nombre".
    /// </summary>
    public static void Restore(string entryId, string command)
    {
        var separator = entryId.IndexOf(':');

        if (separator <= 0
            || !Enum.TryParse<StartupLocation>(entryId[..separator], out var location))
        {
            throw new InvalidOperationException($"Entrada de arranque no reconocida: {entryId}");
        }

        var name = entryId[(separator + 1)..];

        switch (location)
        {
            case StartupLocation.CurrentUserRun:
                RestoreRegistryEntry(Registry.CurrentUser, location, name, command);
                break;

            case StartupLocation.LocalMachineRun:
                RestoreRegistryEntry(Registry.LocalMachine, location, name, command);
                break;

            case StartupLocation.StartupFolder:
                RestoreShortcut(command);
                break;

            default:
                throw new NotSupportedException($"Ubicación no soportada: {location}");
        }
    }

    private static void RestoreRegistryEntry(RegistryKey root, StartupLocation location, string name, string command)
    {
        using var key = root.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("No se pudo abrir la clave Run para escritura.");

        key.SetValue(name, command, RegistryValueKind.String);

        using var backup = root.OpenSubKey($@"{BackupKey}\{location}", writable: true);
        backup?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void RestoreShortcut(string originalPath)
    {
        var backupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcOptimizer",
            "DisabledStartup");

        var backup = Path.Combine(backupFolder, Path.GetFileName(originalPath));

        if (!File.Exists(backup))
        {
            throw new InvalidOperationException(
                $"No se encuentra la copia de seguridad del acceso directo: {backup}");
        }

        var folder = Path.GetDirectoryName(originalPath);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.Move(backup, originalPath, overwrite: true);
    }

    private static void Disable(StartupEntry entry)
    {
        switch (entry.Location)
        {
            case StartupLocation.CurrentUserRun:
                DisableRegistryEntry(Registry.CurrentUser, entry);
                break;

            case StartupLocation.LocalMachineRun:
                DisableRegistryEntry(Registry.LocalMachine, entry);
                break;

            case StartupLocation.StartupFolder:
                DisableShortcut(entry);
                break;

            default:
                throw new NotSupportedException($"Ubicación no soportada: {entry.Location}");
        }
    }

    private static void DisableRegistryEntry(RegistryKey root, StartupEntry entry)
    {
        using var backup = root.CreateSubKey($@"{BackupKey}\{entry.Location}")
            ?? throw new InvalidOperationException("No se pudo crear la clave de respaldo.");
        backup.SetValue(entry.Name, entry.Command);

        using var key = root.OpenSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("No se pudo abrir la clave Run para escritura.");
        key.DeleteValue(entry.Name, throwOnMissingValue: false);
    }

    private static void DisableShortcut(StartupEntry entry)
    {
        var backupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcOptimizer",
            "DisabledStartup");

        Directory.CreateDirectory(backupFolder);

        var destination = Path.Combine(backupFolder, Path.GetFileName(entry.Command));
        File.Move(entry.Command, destination, overwrite: true);
    }
}
