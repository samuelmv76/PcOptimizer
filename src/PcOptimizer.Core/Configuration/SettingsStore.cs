using System.Text;
using System.Text.Json;

namespace PcOptimizer.Core.Configuration;

/// <summary>
/// Guarda las preferencias en un JSON junto al resto de datos de la
/// aplicacion. Un fichero ilegible o de otra version no impide arrancar: se
/// vuelve a los valores por defecto, que es lo que el usuario espera de una
/// preferencia visual.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcOptimizer",
            "settings.json");
    }

    public string FilePath => _path;

    /// <summary>Carpeta de datos: ajustes, registro de auditoria y diario de cambios.</summary>
    public string DataFolder => Path.GetDirectoryName(_path) ?? string.Empty;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>Guarda. Si no se puede, la preferencia dura hasta cerrar la aplicacion.</summary>
    public bool TrySave(AppSettings settings)
    {
        try
        {
            if (!string.IsNullOrEmpty(DataFolder))
            {
                Directory.CreateDirectory(DataFolder);
            }

            // Se escribe aparte y se sustituye: un corte a mitad no deja un
            // fichero a medias que al siguiente arranque no se pueda leer.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options), Encoding.UTF8);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
