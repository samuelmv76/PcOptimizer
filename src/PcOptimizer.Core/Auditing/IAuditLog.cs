namespace PcOptimizer.Core.Auditing;

/// <summary>
/// Registro de todo lo que la aplicacion cambia en el equipo, y de lo que
/// intenta cambiar y no puede. Sin esto no hay forma de que el usuario sepa
/// que deshacer, ni de averiguar por que algo no funciono.
/// </summary>
public interface IAuditLog
{
    void Record(string module, string action, string target, bool simulated);

    /// <summary>Un intento que no salio bien, con el motivo.</summary>
    void RecordFailure(string module, string action, string target, string reason);
}
