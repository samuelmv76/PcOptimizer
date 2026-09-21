namespace PcOptimizer.Core.Hardware;

public sealed record CpuInfo(
    string Name,
    int PhysicalCores,
    int LogicalCores,
    uint MaxClockMhz,
    bool? VirtualizationEnabledInFirmware);

public sealed record GpuInfo(
    string Name,
    long DedicatedMemoryBytes,
    string DriverVersion,
    DateTime? DriverDate)
{
    /// <summary>
    /// Graficas integradas comunes. Se usa para no recomendar ajustes de GPU
    /// dedicada a quien no la tiene.
    /// </summary>
    public bool IsLikelyIntegrated =>
        Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            && !Name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Vega", StringComparison.OrdinalIgnoreCase)
            && Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase);
}

public sealed record MemoryModuleInfo(
    string Slot,
    long CapacityBytes,
    uint NominalSpeedMhz,
    uint ConfiguredSpeedMhz,
    string Manufacturer)
{
    /// <summary>
    /// El modulo esta funcionando por debajo de lo que sabe hacer: el perfil
    /// XMP/EXPO no esta activado en la BIOS.
    /// </summary>
    public bool RunningBelowRatedSpeed =>
        NominalSpeedMhz > 0 && ConfiguredSpeedMhz > 0 && ConfiguredSpeedMhz < NominalSpeedMhz;
}

public sealed record DiskInfo(
    string Model,
    long SizeBytes,
    string MediaType,
    string BusType,
    bool IsSystemDisk);

public sealed record OsInfo(
    string Caption,
    string Version,
    string Build,
    string Architecture,
    DateTime? InstalledOn);

/// <summary>
/// Fotografia del equipo. La capturan los modulos de diagnostico y la usan
/// los que recomiendan ajustes: sin saber que hay dentro no se puede
/// recomendar nada sensato.
/// </summary>
public sealed record HardwareProfile
{
    public CpuInfo? Cpu { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];

    public IReadOnlyList<MemoryModuleInfo> MemoryModules { get; init; } = [];

    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];

    public OsInfo? Os { get; init; }

    /// <summary>
    /// Si el equipo lleva bateria. Cambia las recomendaciones: en un portatil
    /// el plan de alto rendimiento se come la autonomia y calienta.
    /// </summary>
    public bool IsPortable { get; init; }

    public string Motherboard { get; init; } = string.Empty;

    public string BiosVersion { get; init; } = string.Empty;

    public DateTime? BiosDate { get; init; }

    public long TotalMemoryBytes => MemoryModules.Sum(m => m.CapacityBytes);

    /// <summary>
    /// Equipo modesto: disco del sistema mecanico, menos de 8 GB de RAM o
    /// grafica integrada. Es donde los ajustes visuales si se notan.
    /// </summary>
    public bool IsModestHardware =>
        Disks.Any(d => d.IsSystemDisk && d.MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase))
        || (TotalMemoryBytes > 0 && TotalMemoryBytes < 8L * 1024 * 1024 * 1024)
        || (PrimaryGpu is { } gpu && gpu.IsLikelyIntegrated);

    public GpuInfo? PrimaryGpu =>
        Gpus.FirstOrDefault(g => !g.IsLikelyIntegrated) ?? Gpus.FirstOrDefault();
}
