namespace PcOptimizer.Core.Auditing;

/// <summary>
/// Registro de todo lo que la aplicacion cambia en el equipo.
/// Sin esto no hay forma de que el usuario sepa que deshacer.
/// </summary>
public interface IAuditLog
{
    void Record(string module, string action, string target, bool simulated);
}
