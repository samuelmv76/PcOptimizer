namespace PcOptimizer.Core.Platform;

/// <summary>
/// Las cadenas de desinstalacion del registro vienen como una linea de
/// comandos entera ("C:\Program Files\Cosa\unins.exe" /S). Para lanzarlas sin
/// pasar por el interprete de comandos hay que separar el ejecutable de sus
/// argumentos respetando las comillas.
/// </summary>
public static class CommandLine
{
    public static (string Executable, string Arguments) Split(string commandLine)
    {
        var text = commandLine.Trim();

        if (text.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (text[0] == '"')
        {
            var closing = text.IndexOf('"', 1);

            return closing < 0
                ? (text[1..], string.Empty)
                : (text[1..closing], text[(closing + 1)..].TrimStart());
        }

        // Sin comillas, el ejecutable acaba en el primer espacio. Es lo que
        // hace Windows, aunque la ruta tenga espacios: por eso los
        // instaladores serios siempre entrecomillan.
        var space = text.IndexOf(' ');

        return space < 0
            ? (text, string.Empty)
            : (text[..space], text[(space + 1)..].TrimStart());
    }

    /// <summary>
    /// Si la desinstalacion la gestiona Windows Installer, en cuyo caso se
    /// puede pedir en silencio aunque el programa no ofrezca una cadena propia.
    /// </summary>
    public static bool IsMsiExec(string executable)
        => Path.GetFileNameWithoutExtension(executable)
            .Equals("msiexec", StringComparison.OrdinalIgnoreCase);
}
