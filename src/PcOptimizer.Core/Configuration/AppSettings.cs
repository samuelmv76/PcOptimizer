using System.Text.Json.Serialization;

namespace PcOptimizer.Core.Configuration;

public enum ThemePreference
{
    Dark,
    Light,

    /// <summary>El que tenga Windows en cada momento.</summary>
    System
}

/// <summary>Preferencias del usuario sobre la propia aplicacion.</summary>
public sealed record AppSettings
{
    [JsonPropertyName("theme")]
    [JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;

    /// <summary>Lanzar el resumen al abrir, sin pulsar nada.</summary>
    [JsonPropertyName("analyzeOnStartup")]
    public bool AnalyzeOnStartup { get; init; } = true;

    public static AppSettings Default { get; } = new();
}
