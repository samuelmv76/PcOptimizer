using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Cleaning;

/// <summary>
/// Borra ficheros temporales antiguos de un conjunto cerrado de carpetas.
/// Nunca recorre carpetas de usuario como Documentos o Descargas.
/// </summary>
public sealed class TempFileCleaner : IOptimizerModule
{
    private readonly IReadOnlyList<CleaningTarget> _targets;
    private readonly IAuditLog _audit;
    private readonly TimeProvider _time;

    public TempFileCleaner(
        IEnumerable<CleaningTarget> targets,
        IAuditLog? audit = null,
        TimeProvider? timeProvider = null)
    {
        _targets = targets.ToList();
        _audit = audit ?? NullAuditLog.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Id => "cleaning.temp";

    public string DisplayName => "Limpieza de temporales y cache";

    public string Description =>
        "Borra ficheros temporales con mas de 24 horas de un conjunto cerrado de carpetas del sistema. Nunca entra en Documentos ni en Descargas.";

    public bool RequiresElevation => true;

    public bool SupportsRebootDeletion => true;

    public static IReadOnlyList<CleaningTarget> DefaultTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return new List<CleaningTarget>
        {
            new("Temporales del usuario", Path.GetTempPath()),
            new("Temporales de Windows", Path.Combine(windows, "Temp")),
            new("Cache de Internet", Path.Combine(local, "Microsoft", "Windows", "INetCache")),
            new("Volcados de fallos", Path.Combine(local, "CrashDumps")),
            new("Informes de error de Windows", Path.Combine(local, "Microsoft", "Windows", "WER"))
        };
    }

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var target in _targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(target.Path))
            {
                continue;
            }

            foreach (var file in SafeDirectory.EnumerateFiles(
                         target.Path, target.Pattern, target.Recursive, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);
                    if (now - info.LastWriteTimeUtc < target.MinimumAge)
                    {
                        continue;
                    }

                    findings.Add(new FileFinding(file, target.Name, info.Length));
                }
                catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
                {
                    // Un fichero bloqueado o sin permisos no es un error: no es candidato.
                }
            }
        }

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
        var deferred = 0;
        long freed = 0;

        foreach (var finding in findings.OfType<FileFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Entre el analisis y el borrado pueden pasar minutos: un temporal
            // que ya no esta no es un fallo, simplemente ya no hay nada que hacer.
            if (!File.Exists(finding.FullPath))
            {
                continue;
            }

            if (options.Simulate)
            {
                _audit.Record(Id, "delete-file", finding.FullPath, simulated: true);
                applied++;
                freed += finding.ReclaimableBytes;
                continue;
            }

            if (FileDeletion.TryDelete(finding.FullPath, out var error))
            {
                _audit.Record(Id, "delete-file", finding.FullPath, simulated: false);
                applied++;
                freed += finding.ReclaimableBytes;
            }
            else if (options.DeleteLockedOnReboot
                     && FileDeletion.IsInUse(error)
                     && FileDeletion.TryScheduleDeleteOnReboot(finding.FullPath, out _))
            {
                _audit.Record(Id, "delete-file-on-reboot", finding.FullPath, simulated: false);
                deferred++;
                errors.Add($"{Path.GetFileName(finding.FullPath)}: en uso, se borrara al reiniciar.");
            }
            else
            {
                failed++;
                _audit.RecordFailure(Id, "delete-file", finding.FullPath, error ?? "motivo desconocido");
                errors.Add($"{Path.GetFileName(finding.FullPath)}: {error}");
            }
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            Deferred = deferred,
            BytesFreed = freed,
            Errors = errors
        });
    }
}
