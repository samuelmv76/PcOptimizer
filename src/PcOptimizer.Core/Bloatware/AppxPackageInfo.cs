using System.Text.Json.Serialization;

namespace PcOptimizer.Core.Bloatware;

/// <summary>
/// Un paquete AppX tal y como lo devuelve Get-AppxPackage.
/// </summary>
public sealed record AppxPackageInfo
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("PackageFullName")]
    public string PackageFullName { get; init; } = string.Empty;

    [JsonPropertyName("Publisher")]
    public string Publisher { get; init; } = string.Empty;

    [JsonPropertyName("Version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("NonRemovable")]
    public bool? NonRemovable { get; init; }
}
