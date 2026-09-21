using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Programs;

/// <summary>
/// Un programa clasico de escritorio, leido del registro de desinstalacion.
/// </summary>
public sealed record InstalledProgram
{
    public required string Name { get; init; }

    public string Publisher { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>Tamano estimado en bytes, segun lo que declaro el instalador. 0 si no lo dice.</summary>
    public long EstimatedBytes { get; init; }

    /// <summary>Comando de desinstalacion, tal cual esta en el registro.</summary>
    public string UninstallCommand { get; init; } = string.Empty;

    /// <summary>Comando que desinstala sin preguntar, si el programa lo ofrece.</summary>
    public string QuietUninstallCommand { get; init; } = string.Empty;

    /// <summary>Clave del registro de la que salio, para poder localizarlo.</summary>
    public string RegistryKey { get; init; } = string.Empty;

    /// <summary>
    /// Si se puede desinstalar sin que el usuario tenga que seguir un asistente.
    /// </summary>
    public bool SupportsSilentUninstall =>
        !string.IsNullOrWhiteSpace(QuietUninstallCommand)
        || CanUseMsiSilently;

    public bool CanUseMsiSilently =>
        !string.IsNullOrWhiteSpace(UninstallCommand)
        && CommandLine.IsMsiExec(CommandLine.Split(UninstallCommand).Executable);
}
