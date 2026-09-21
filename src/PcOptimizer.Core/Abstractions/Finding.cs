namespace PcOptimizer.Core.Abstractions;

public enum FindingSeverity
{
    Info,
    Suggestion,
    Warning
}

/// <summary>
/// Algo que un modulo ha detectado y que el usuario puede decidir corregir.
/// Un Finding es solo informacion: nunca ejecuta nada por si mismo.
/// </summary>
public abstract class Finding
{
    protected Finding(string id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>Identificador estable dentro del modulo (ruta, clave, nombre de servicio...).</summary>
    public string Id { get; }

    public string Title { get; }

    public string Details { get; init; } = string.Empty;

    public FindingSeverity Severity { get; init; } = FindingSeverity.Suggestion;

    /// <summary>Espacio que se liberaria. 0 si la accion no libera disco.</summary>
    public long ReclaimableBytes { get; init; }

    /// <summary>
    /// Que deberia hacer el usuario. Se usa sobre todo en modulos de
    /// diagnostico, donde la aplicacion no puede aplicar el cambio por si misma.
    /// </summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>
    /// Si la UI debe marcarlo por defecto. Solo true cuando la accion es
    /// inequivocamente segura (por ejemplo, borrar un temporal antiguo).
    /// </summary>
    public bool SelectedByDefault { get; init; }
}
