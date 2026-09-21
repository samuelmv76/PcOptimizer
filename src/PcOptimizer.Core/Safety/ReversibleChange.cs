using System.Text.Json.Serialization;

namespace PcOptimizer.Core.Safety;

/// <summary>
/// Que tipo de cambio es, para saber como deshacerlo.
/// </summary>
public static class ChangeKinds
{
    public const string RegistryValue = "registry-value";
    public const string StartupEntry = "startup-entry";
    public const string PowerPlan = "power-plan";
    public const string ServiceStartMode = "service-start-mode";

    /// <summary>Variable UEFI global (BootNext, OsIndications). Valor en hexadecimal.</summary>
    public const string FirmwareVariable = "firmware-variable";
}

/// <summary>
/// Un cambio que la aplicacion hizo y sabe deshacer, con el valor que habia
/// antes. Es lo que separa "optimizar" de "romper sin vuelta atras".
/// </summary>
public sealed record ReversibleChange
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("at")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    [JsonPropertyName("module")]
    public string ModuleId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>Que se cambio: ruta de registro, nombre de servicio, GUID de plan...</summary>
    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Valor anterior en texto. Null significa que no existia: deshacer
    /// entonces es borrarlo, no escribir una cadena vacia.
    /// </summary>
    [JsonPropertyName("before")]
    public string? PreviousValue { get; init; }

    [JsonPropertyName("after")]
    public string? NewValue { get; init; }

    /// <summary>Tipo del valor de registro (DWord, String...), cuando aplica.</summary>
    [JsonPropertyName("valueKind")]
    public string? ValueKind { get; init; }

    /// <summary>Como contarselo al usuario.</summary>
    [JsonPropertyName("what")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("reverted")]
    public bool Reverted { get; init; }

    /// <summary>Si hace falta reiniciar para que el cambio (o su vuelta atras) surta efecto.</summary>
    [JsonPropertyName("needsRestart")]
    public bool NeedsRestart { get; init; }
}
