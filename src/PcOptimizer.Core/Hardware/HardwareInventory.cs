using System.Globalization;
using System.Management;
using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Hardware;

/// <summary>
/// Lee que hay dentro del equipo: procesador, graficas, memoria, discos y
/// version de Windows. Es la base de los modulos que recomiendan ajustes:
/// sin conocer el hardware no se puede recomendar nada concreto.
/// </summary>
public sealed class HardwareInventory : DiagnosticModule
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public const string ModuleId = "hardware.inventory";

    private readonly HardwareProfileCache? _cache;

    /// <param name="cache">
    /// Si se da, analizar desde esta pagina refresca la lectura compartida, y
    /// el resto de modulos ven el hardware actualizado sin volver a leerlo.
    /// </param>
    public HardwareInventory(HardwareProfileCache? cache = null) => _cache = cache;

    public override string Id => ModuleId;

    public override string DisplayName => "Hardware del equipo";

    public override string Description =>
        "Qué hay dentro del PC: procesador, gráfica, memoria y discos. Solo lectura.";

    public override Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var profile = _cache?.Refresh() ?? Capture(cancellationToken);
        return Task.FromResult(Describe(profile));
    }

    /// <summary>
    /// Captura el perfil sin pasar por la interfaz de modulo, para que otros
    /// modulos puedan reutilizarlo.
    /// </summary>
    public static HardwareProfile Capture(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new HardwareProfile
        {
            Cpu = ReadCpu(),
            Gpus = ReadGpus(),
            MemoryModules = ReadMemory(),
            Disks = ReadDisks(),
            Os = ReadOs(),
            IsPortable = ReadHasBattery(),
            Motherboard = ReadMotherboard(),
            BiosVersion = ReadBios(out var biosDate),
            BiosDate = biosDate
        };
    }

    public static IReadOnlyList<Finding> Describe(HardwareProfile profile)
    {
        var findings = new List<Finding>();

        if (profile.Cpu is { } cpu)
        {
            findings.Add(new HardwareFinding(
                "Procesador",
                cpu.Name,
                $"{cpu.PhysicalCores} núcleos / {cpu.LogicalCores} hilos, {cpu.MaxClockMhz} MHz nominales"));
        }

        foreach (var gpu in profile.Gpus)
        {
            var memory = gpu.DedicatedMemoryBytes > 0
                ? ByteSize.Format(gpu.DedicatedMemoryBytes)
                : "memoria compartida";

            var driver = string.IsNullOrEmpty(gpu.DriverVersion)
                ? string.Empty
                : $", driver {gpu.DriverVersion}";

            var driverAge = gpu.DriverDate is { } date
                ? $" ({date:dd/MM/yyyy})"
                : string.Empty;

            var driverIsOld = gpu.DriverDate is { } issued
                              && DateTime.Now - issued > TimeSpan.FromDays(365);

            findings.Add(new HardwareFinding("Gráfica", gpu.Name, $"{memory}{driver}{driverAge}")
            {
                Severity = driverIsOld ? FindingSeverity.Suggestion : FindingSeverity.Info,
                Recommendation = driverIsOld
                    ? "El driver tiene más de un año. Actualizarlo suele dar rendimiento "
                      + "y corregir fallos en juegos recientes."
                    : string.Empty
            });
        }

        if (profile.MemoryModules.Count > 0)
        {
            var total = ByteSize.Format(profile.TotalMemoryBytes);
            var speeds = profile.MemoryModules
                .Select(m => m.ConfiguredSpeedMhz > 0 ? m.ConfiguredSpeedMhz : m.NominalSpeedMhz)
                .Distinct()
                .Where(s => s > 0)
                .ToList();

            var speedText = speeds.Count > 0
                ? $"{string.Join(" / ", speeds)} MHz"
                : "velocidad desconocida";

            findings.Add(new HardwareFinding(
                "Memoria",
                $"{total} de RAM",
                $"{profile.MemoryModules.Count} módulos a {speedText}"));

            if (profile.MemoryModules.Count == 1)
            {
                findings.Add(new HardwareFinding(
                    "Memoria",
                    "Un solo módulo de RAM",
                    "La memoria trabaja en canal simple.")
                {
                    Severity = FindingSeverity.Suggestion,
                    Recommendation = "Añadir un segundo módulo igual habilita el doble canal. "
                                     + "En juegos con gráfica integrada la diferencia es grande."
                });
            }
        }

        foreach (var disk in profile.Disks)
        {
            var label = disk.IsSystemDisk ? $"{disk.Model} (disco del sistema)" : disk.Model;
            findings.Add(new HardwareFinding(
                "Almacenamiento",
                label,
                $"{ByteSize.Format(disk.SizeBytes)}, {disk.MediaType}, {disk.BusType}"));

            if (disk.IsSystemDisk && disk.MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new HardwareFinding(
                    "Almacenamiento",
                    "Windows está instalado en un disco mecánico",
                    "El disco del sistema es un HDD.")
                {
                    Severity = FindingSeverity.Warning,
                    Recommendation = "Pasar Windows a un SSD es, con diferencia, la mejora de rendimiento "
                                     + "más grande que puedes hacer en este equipo."
                });
            }
        }

        if (profile.Os is { } os)
        {
            var installed = os.InstalledOn is { } date ? $", instalado el {date:dd/MM/yyyy}" : string.Empty;
            findings.Add(new HardwareFinding(
                "Sistema",
                os.Caption,
                $"Versión {os.Version} (build {os.Build}), {os.Architecture}{installed}"));
        }

        if (!string.IsNullOrEmpty(profile.Motherboard))
        {
            var bios = string.IsNullOrEmpty(profile.BiosVersion)
                ? string.Empty
                : $"BIOS {profile.BiosVersion}";

            if (profile.BiosDate is { } biosDate)
            {
                bios += $" ({biosDate:dd/MM/yyyy})";
            }

            findings.Add(new HardwareFinding("Placa base", profile.Motherboard, bios));
        }

        return findings;
    }

    private static CpuInfo? ReadCpu()
    {
        foreach (var cpu in Wmi.Query("SELECT * FROM Win32_Processor"))
        {
            using (cpu)
            {
                return new CpuInfo(
                    cpu.GetString("Name"),
                    (int)cpu.GetUInt32("NumberOfCores"),
                    (int)cpu.GetUInt32("NumberOfLogicalProcessors"),
                    cpu.GetUInt32("MaxClockSpeed"),
                    cpu.GetBool("VirtualizationFirmwareEnabled"));
            }
        }

        return null;
    }

    private static IReadOnlyList<GpuInfo> ReadGpus()
    {
        var vram = ReadDedicatedVideoMemory();
        var gpus = new List<GpuInfo>();

        foreach (var gpu in Wmi.Query("SELECT * FROM Win32_VideoController"))
        {
            using (gpu)
            {
                var name = gpu.GetString("Name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // AdapterRAM es un uint32: cualquier grafica de 4 GB o mas se
                // reporta mal. El registro guarda el valor real en 64 bits.
                var memory = vram.TryGetValue(name, out var fromRegistry)
                    ? fromRegistry
                    : (long)gpu.GetUInt64("AdapterRAM");

                gpus.Add(new GpuInfo(
                    name,
                    memory,
                    gpu.GetString("DriverVersion"),
                    ParseWmiDate(gpu.GetString("DriverDate"))));
            }
        }

        return gpus;
    }

    private static Dictionary<string, long> ReadDedicatedVideoMemory()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (root is null)
            {
                return result;
            }

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                // Las subclaves de adaptador son numericas (0000, 0001...).
                if (!subKeyName.All(char.IsDigit))
                {
                    continue;
                }

                using var adapter = root.OpenSubKey(subKeyName);
                var description = adapter?.GetValue("DriverDesc")?.ToString();
                var size = adapter?.GetValue("HardwareInformation.qwMemorySize");

                if (description is null || size is null)
                {
                    continue;
                }

                result[description] = Convert.ToInt64(size, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidCastException or FormatException)
        {
            // Sin acceso al registro nos quedamos con el valor de WMI.
        }

        return result;
    }

    private static IReadOnlyList<MemoryModuleInfo> ReadMemory()
    {
        var modules = new List<MemoryModuleInfo>();

        foreach (var stick in Wmi.Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            using (stick)
            {
                modules.Add(new MemoryModuleInfo(
                    stick.GetString("DeviceLocator"),
                    (long)stick.GetUInt64("Capacity"),
                    stick.GetUInt32("Speed"),
                    stick.GetUInt32("ConfiguredClockSpeed"),
                    stick.GetString("Manufacturer")));
            }
        }

        return modules;
    }

    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        var systemDiskIndex = ReadSystemDiskIndex();
        var disks = new List<DiskInfo>();

        // MSFT_PhysicalDisk distingue SSD de HDD; Win32_DiskDrive no.
        foreach (var disk in Wmi.Query(
                     "SELECT * FROM MSFT_PhysicalDisk",
                     "root\\Microsoft\\Windows\\Storage"))
        {
            using (disk)
            {
                var deviceId = disk.GetString("DeviceId");
                var isSystem = systemDiskIndex is { } index
                               && int.TryParse(deviceId, out var parsed)
                               && parsed == index;

                disks.Add(new DiskInfo(
                    disk.GetString("FriendlyName"),
                    (long)disk.GetUInt64("Size"),
                    DescribeMediaType(disk.GetUInt32("MediaType")),
                    DescribeBusType(disk.GetUInt32("BusType")),
                    isSystem));
            }
        }

        if (disks.Count > 0)
        {
            return disks;
        }

        // Respaldo para equipos donde el espacio de nombres Storage no responde.
        foreach (var disk in Wmi.Query("SELECT * FROM Win32_DiskDrive"))
        {
            using (disk)
            {
                disks.Add(new DiskInfo(
                    disk.GetString("Model"),
                    (long)disk.GetUInt64("Size"),
                    "tipo desconocido",
                    disk.GetString("InterfaceType"),
                    (int)disk.GetUInt32("Index") == systemDiskIndex));
            }
        }

        return disks;
    }

    private static int? ReadSystemDiskIndex()
    {
        foreach (var partition in Wmi.Query(
                     "SELECT DiskIndex FROM Win32_DiskPartition WHERE BootPartition = TRUE"))
        {
            using (partition)
            {
                return (int)partition.GetUInt32("DiskIndex");
            }
        }

        return null;
    }

    private static OsInfo? ReadOs()
    {
        foreach (var os in Wmi.Query("SELECT * FROM Win32_OperatingSystem"))
        {
            using (os)
            {
                return new OsInfo(
                    os.GetString("Caption"),
                    os.GetString("Version"),
                    os.GetString("BuildNumber"),
                    os.GetString("OSArchitecture"),
                    ParseWmiDate(os.GetString("InstallDate")));
            }
        }

        return null;
    }

    /// <summary>Con bateria se asume portatil. Basta para ajustar consejos.</summary>
    private static bool ReadHasBattery()
    {
        foreach (var battery in Wmi.Query("SELECT DeviceID FROM Win32_Battery"))
        {
            using (battery)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadMotherboard()
    {
        foreach (var board in Wmi.Query("SELECT * FROM Win32_BaseBoard"))
        {
            using (board)
            {
                var manufacturer = board.GetString("Manufacturer");
                var product = board.GetString("Product");
                return $"{manufacturer} {product}".Trim();
            }
        }

        return string.Empty;
    }

    private static string ReadBios(out DateTime? releaseDate)
    {
        foreach (var bios in Wmi.Query("SELECT * FROM Win32_BIOS"))
        {
            using (bios)
            {
                releaseDate = ParseWmiDate(bios.GetString("ReleaseDate"));
                return bios.GetString("SMBIOSBIOSVersion");
            }
        }

        releaseDate = null;
        return string.Empty;
    }

    private static DateTime? ParseWmiDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string DescribeMediaType(uint value) => value switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "memoria persistente",
        _ => "tipo desconocido"
    };

    private static string DescribeBusType(uint value) => value switch
    {
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        11 => "SATA",
        17 => "NVMe",
        _ => "bus desconocido"
    };
}
