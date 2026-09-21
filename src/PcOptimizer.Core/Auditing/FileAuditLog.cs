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
    {
        var line = string.Join('\t',
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            simulated ? "SIMULATED" : "APPLIED",
            module,
            action,
            target);

        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
