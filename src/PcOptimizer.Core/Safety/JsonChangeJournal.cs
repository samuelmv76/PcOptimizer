using System.Text;
using System.Text.Json;

namespace PcOptimizer.Core.Safety;

/// <summary>
/// Diario en un fichero JSON por lineas. Una linea por cambio: si una se
/// corrompe, el resto sigue siendo legible. Deshacer no borra la linea,
/// anade otra marcandola como deshecha, para que el historial no mienta.
/// </summary>
public sealed class JsonChangeJournal : IChangeJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public JsonChangeJournal(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcOptimizer",
            "changes.jsonl");

        var folder = Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    public string FilePath => _path;

    public void Record(ReversibleChange change) => Append(change);

    public void MarkReverted(string changeId)
    {
        var change = ReadAll().FirstOrDefault(c => c.Id == changeId);

        if (change is null)
        {
            return;
        }

        Append(change with { Reverted = true });
    }

    /// <summary>
    /// Todos los cambios, el mas reciente primero. Si un cambio aparece varias
    /// veces (porque se marco como deshecho), gana la ultima linea escrita.
    /// </summary>
    public IReadOnlyList<ReversibleChange> ReadAll()
    {
        string[] lines;

        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            try
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        var latest = new Dictionary<string, ReversibleChange>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ReversibleChange? change;

            try
            {
                change = JsonSerializer.Deserialize<ReversibleChange>(line, Options);
            }
            catch (JsonException)
            {
                // Una linea corrupta no invalida el diario entero.
                continue;
            }

            if (change is null || string.IsNullOrEmpty(change.Id))
            {
                continue;
            }

            if (!latest.ContainsKey(change.Id))
            {
                order.Add(change.Id);
            }

            latest[change.Id] = change;
        }

        order.Reverse();

        return order.Select(id => latest[id]).ToList();
    }

    private void Append(ReversibleChange change)
    {
        var line = JsonSerializer.Serialize(change, Options);

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sin diario se pierde la vuelta atras, pero no la operacion.
            }
        }
    }
}
