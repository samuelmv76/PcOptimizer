using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Disk;

/// <summary>
/// Ensena donde se esta yendo el espacio. Solo ofrece borrar lo que la
/// aplicacion puede borrar de forma fiable; lo demas se informa con
/// instrucciones, en vez de intentarlo y fallar a medias.
/// </summary>
public sealed class DiskSpaceAnalyzer : IOptimizerModule
{
    private readonly IAuditLog _audit;

    public DiskSpaceAnalyzer(IAuditLog? audit = null) => _audit = audit ?? NullAuditLog.Instance;

    public string Id => "disk.space";

    public string DisplayName => "Espacio en disco";

    public string Description =>
        "Dónde se está yendo el espacio. Solo ofrece borrar lo que se puede borrar con seguridad; del resto te dice cómo hacerlo tú.";

    public bool RequiresElevation => true;

    public bool SupportsRebootDeletion => true;

    public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        findings.AddRange(DescribeDrives());
        findings.AddRange(FindReclaimable(cancellationToken));
        findings.AddRange(DescribeLargeLocations(cancellationToken));

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<Finding> DescribeDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            long free;
            long total;

            try
            {
                free = drive.AvailableFreeSpace;
                total = drive.TotalSize;
            }
            catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
            {
                continue;
            }

            if (total <= 0)
            {
                continue;
            }

            var percentFree = free * 100.0 / total;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "sin etiqueta" : drive.VolumeLabel;
            var runningLow = percentFree < 10;

            yield return new SpaceFinding(
                $"drive:{drive.Name}",
                $"Unidad {drive.Name.TrimEnd('\\')} ({label})",
                SpaceAction.None,
                total - free)
            {
                Details = $"{ByteSize.Format(free)} libres de {ByteSize.Format(total)} ({percentFree:0.#}%)",
                Severity = runningLow ? FindingSeverity.Warning : FindingSeverity.Info,
                Recommendation = runningLow
                    ? "Con menos del 10% libre Windows empieza a ir peor: las actualizaciones fallan "
                      + "y, en un SSD, escribir se vuelve más lento."
                    : string.Empty
            };
        }
    }

    private static IEnumerable<Finding> FindReclaimable(CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var candidates = new (string Id, string Title, string Path, string Explanation)[]
        {
            (
                "update-cache",
                "Caché de Windows Update",
                Path.Combine(windows, "SoftwareDistribution", "Download"),
                "Instaladores ya aplicados. Windows los vuelve a descargar si los necesita."
            ),
            (
                "delivery-optimization",
                "Caché de Optimización de entrega",
                Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local",
                    "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
                "Trozos de actualizaciones guardados para compartir con otros equipos de la red."
            ),
            (
                "windows-logs",
                "Registros de instalación de Windows",
                Path.Combine(windows, "Logs", "CBS"),
                "Registros de instalación de componentes. Solo sirven para diagnosticar fallos pasados."
            )
        };

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(candidate.Path))
            {
                continue;
            }

            var size = SafeDirectory.GetDirectorySize(candidate.Path, cancellationToken);

            if (size < 1024 * 1024)
            {
                continue;
            }

            yield return new SpaceFinding(
                candidate.Id,
                candidate.Title,
                SpaceAction.DeleteFolderContents,
                size,
                candidate.Path)
            {
                Details = candidate.Explanation,
                Severity = FindingSeverity.Suggestion
            };
        }

        var (recycleBytes, recycleItems) = RecycleBin.Query();

        if (recycleBytes > 0)
        {
            yield return new SpaceFinding(
                "recycle-bin",
                "Papelera de reciclaje",
                SpaceAction.EmptyRecycleBin,
                recycleBytes)
            {
                Details = $"{recycleItems} elementos. Al vaciarla no se pueden recuperar.",
                Severity = FindingSeverity.Suggestion
            };
        }
    }

    /// <summary>
    /// Sitios que ocupan mucho pero que esta aplicacion no borra: o hace falta
    /// una herramienta de Windows, o borrarlos a mano rompe cosas.
    /// </summary>
    private static IEnumerable<Finding> DescribeLargeLocations(CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(windows) ?? "C:\\";

        var previousInstall = Path.Combine(systemDrive, "Windows.old");
        if (Directory.Exists(previousInstall))
        {
            yield return new SpaceFinding(
                "windows-old",
                "Instalación anterior de Windows (Windows.old)",
                SpaceAction.None,
                SafeDirectory.GetDirectorySize(previousInstall, cancellationToken))
            {
                Details = previousInstall,
                Severity = FindingSeverity.Suggestion,
                Recommendation = "Es tu Windows anterior, guardado para poder volver atrás. Borrarlo a mano "
                                 + "falla por permisos y deja restos: hazlo desde Configuración > Sistema > "
                                 + "Almacenamiento > Archivos temporales. Ojo: al quitarlo pierdes la opción "
                                 + "de revertir la actualización."
            };
        }

        foreach (var name in new[] { "$Windows.~BT", "$Windows.~WS" })
        {
            var path = Path.Combine(systemDrive, name);
            if (!Directory.Exists(path))
            {
                continue;
            }

            yield return new SpaceFinding(
                $"upgrade-leftover:{name}",
                $"Restos de actualización ({name})",
                SpaceAction.None,
                SafeDirectory.GetDirectorySize(path, cancellationToken))
            {
                Details = path,
                Severity = FindingSeverity.Suggestion,
                Recommendation = "Archivos de una actualización de versión. Se quitan desde Configuración > "
                                 + "Sistema > Almacenamiento > Archivos temporales."
            };
        }

        var componentStore = Path.Combine(windows, "WinSxS");
        if (Directory.Exists(componentStore))
        {
            yield return new SpaceFinding(
                "winsxs",
                "Almacén de componentes (WinSxS)",
                SpaceAction.None,
                0)
            {
                Details = "Guarda las versiones anteriores de cada componente de Windows.",
                Severity = FindingSeverity.Info,
                Recommendation = "Nunca lo borres a mano: Windows dejaría de poder actualizarse o repararse. "
                                 + "Para reducirlo, ejecuta en una consola de administrador: "
                                 + "Dism.exe /Online /Cleanup-Image /StartComponentCleanup"
            };
        }

        var hibernation = Path.Combine(systemDrive, "hiberfil.sys");
        var hibernationSize = TryGetFileSize(hibernation);
        if (hibernationSize > 0)
        {
            yield return new SpaceFinding(
                "hiberfil",
                "Archivo de hibernación (hiberfil.sys)",
                SpaceAction.None,
                hibernationSize)
            {
                Details = $"{ByteSize.Format(hibernationSize)} reservados en {systemDrive}",
                Severity = FindingSeverity.Info,
                Recommendation = "Se puede desactivar con 'powercfg /h off' en una consola de administrador, "
                                 + "pero perderás la hibernación y el Inicio rápido. En un portátil no compensa."
            };
        }

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyVideos,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.DesktopDirectory
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                continue;
            }

            var size = SafeDirectory.GetDirectorySize(path, cancellationToken);

            // Por debajo de 1 GB no aporta nada saberlo.
            if (size < 1024L * 1024 * 1024)
            {
                continue;
            }

            yield return new SpaceFinding(
                $"user-folder:{folder}",
                Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
                SpaceAction.None,
                size)
            {
                Details = $"{ByteSize.Format(size)} en {path}",
                Severity = FindingSeverity.Info,
                Recommendation = "Son tus archivos: esta aplicación no los toca. Se lista para que veas "
                                 + "donde está el espacio."
            };
        }
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
        {
            return 0;
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
        long freed = 0;

        foreach (var finding in findings.OfType<SpaceFinding>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (finding.Action == SpaceAction.None)
            {
                failed++;
                errors.Add($"{finding.Title}: esta aplicación no lo borra, mira la recomendación.");
                continue;
            }

            try
            {
                var bytes = options.Simulate
                    ? finding.ReclaimableBytes
                    : Execute(finding, cancellationToken);

                _audit.Record(Id, finding.Action.ToString(), finding.Path, options.Simulate);
                applied++;
                freed += bytes;
            }
            catch (Exception ex)
            {
                failed++;
                _audit.RecordFailure(Id, finding.Action.ToString(), finding.Path, ex.Message);
                errors.Add($"{finding.Title}: {ex.Message}");
            }
        }

        return Task.FromResult(new RemediationResult
        {
            Simulated = options.Simulate,
            Applied = applied,
            Failed = failed,
            BytesFreed = freed,
            Errors = errors
        });
    }

    private static long Execute(SpaceFinding finding, CancellationToken cancellationToken)
    {
        if (finding.Action == SpaceAction.EmptyRecycleBin)
        {
            var (before, _) = RecycleBin.Query();
            RecycleBin.Empty();
            return before;
        }

        return DeleteFolderContents(finding.Path, cancellationToken);
    }

    /// <summary>
    /// Vacia una carpeta sin borrarla: varias de estas carpetas las espera
    /// Windows y recrearlas no siempre restaura los permisos correctos.
    /// </summary>
    private static long DeleteFolderContents(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long freed = 0;

        foreach (var file in SafeDirectory.EnumerateFiles(path, "*", recursive: true, cancellationToken))
        {
            long size;

            try
            {
                size = new FileInfo(file).Length;
            }
            catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
            {
                continue;
            }

            if (FileDeletion.TryDelete(file, out _))
            {
                freed += size;
            }

            // Un fichero en uso se queda. No es motivo para abortar el resto.
        }

        foreach (var directory in SafeDirectory.EnumerateTopLevelDirectories(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileDeletion.TryDeleteDirectory(directory, out _);
        }

        return freed;
    }
}
