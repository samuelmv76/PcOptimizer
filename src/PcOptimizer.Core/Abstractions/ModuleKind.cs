namespace PcOptimizer.Core.Abstractions;

/// <summary>
/// Que puede hacer la interfaz con los hallazgos de un modulo.
/// </summary>
public enum ModuleKind
{
    /// <summary>El usuario selecciona hallazgos y el modulo los corrige.</summary>
    Actionable,

    /// <summary>
    /// El modulo solo informa. Aplicar no hace nada: el cambio esta fuera del
    /// alcance de la aplicacion (la BIOS) o no hay nada que corregir (inventario).
    /// </summary>
    Diagnostic
}
