namespace PcOptimizer.Core.Platform;

/// <summary>
/// Recorrido de carpetas que no se rinde ante una subcarpeta protegida.
/// Directory.EnumerateFiles con AllDirectories lanza UnauthorizedAccessException
/// en mitad de la iteracion (por ejemplo en INetCache\Content.IE5), lo que
/// abortaria el analisis entero. Aqui cada carpeta inaccesible se ignora.
/// </summary>
public static class SafeDirectory
{
    public static IEnumerable<string> EnumerateFiles(
        string root,
        string pattern = "*",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();

            foreach (var file in TryGetFiles(current, pattern))
            {
                yield return file;
            }

            if (!recursive)
            {
                continue;
            }

            foreach (var directory in TryGetDirectories(current))
            {
                // Las uniones y enlaces simbolicos pueden crear ciclos o sacarnos
                // de la carpeta objetivo. No se siguen.
                if (!IsReparsePoint(directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    /// <summary>Tamano total de una carpeta. Lo inaccesible no suma.</summary>
    public static long GetDirectorySize(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;

        foreach (var file in EnumerateFiles(path, "*", recursive: true, cancellationToken))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception ex) when (IsTolerable(ex))
            {
                // Un fichero que desaparece a mitad del recuento no invalida el resto.
            }
        }

        return total;
    }

    /// <summary>Subcarpetas directas, sin fallar si la carpeta esta protegida.</summary>
    public static IReadOnlyList<string> EnumerateTopLevelDirectories(string path)
        => TryGetDirectories(path);

    private static string[] TryGetFiles(string path, string pattern)
    {
        try
        {
            return Directory.GetFiles(path, pattern);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return [];
        }
    }

    private static string[] TryGetDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return [];
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            // Si no se puede ni leer el atributo, tampoco se va a poder recorrer.
            return true;
        }
    }

    public static bool IsTolerable(Exception ex) =>
        ex is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException
            or ArgumentException;
}
