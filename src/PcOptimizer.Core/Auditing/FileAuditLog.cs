using System.Globalization;
using System.Text;

namespace PcOptimizer.Core.Auditing;

public sealed class FileAuditLog : IAuditLog
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileAuditLog(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcOptimizer",
            "audit.log");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public string FilePath => _path;

    public void Record(string module, string action, string target, bool simulated)
        => Write(simulated ? "SIMULATED" : "APPLIED", module, action, target, reason: null);

    public void RecordFailure(string module, string action, string target, string reason)
        => Write("FAILED", module, action, target, reason);

    private void Write(string outcome, string module, string action, string target, string? reason)
    {
        var fields = new List<string>
        {
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            outcome,
            module,
            action,
            target
        };

        if (reason is not null)
        {
            fields.Add(reason);
        }

        var line = string.Join('\t', fields);

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // El registro es una ayuda, no un requisito: si no se puede
                // escribir, la operacion del usuario sigue adelante.
            }
        }
    }
}
