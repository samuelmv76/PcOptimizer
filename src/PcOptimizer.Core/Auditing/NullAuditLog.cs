namespace PcOptimizer.Core.Auditing;

public sealed class NullAuditLog : IAuditLog
{
    public static NullAuditLog Instance { get; } = new();

    public void Record(string module, string action, string target, bool simulated)
    {
    }

    public void RecordFailure(string module, string action, string target, string reason)
    {
    }
}
