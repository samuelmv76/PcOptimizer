namespace PcOptimizer.Core.Safety;

public interface IRestorePointService
{
    /// <summary>
    /// Intenta crear un punto de restauracion. Devuelve false y un mensaje
    /// si no se pudo (Restaurar sistema desactivado, sin privilegios, o la
    /// limitacion de un punto cada 24 horas de Windows).
    /// </summary>
    bool TryCreate(string description, out string? error);
}
