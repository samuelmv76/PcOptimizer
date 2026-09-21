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

            string[] files;
            try
            {
                files = Directory.GetFiles(current, pattern);
            }
            catch (Exception ex) when (IsTolerable(ex))
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (!recursive)
            {
                continue;
            }

            foreach (var directory in GetAccessibleDirectories(current))
            {
                pending.Push(directory);
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

    private static IEnumerable<string> GetAccessibleDirectories(string parent)
    {
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(parent);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            // Las uniones y enlaces simbolicos pueden crear ciclos o sacarnos
            // de la carpeta objetivo. No se siguen.
            bool isReparsePoint;
            try
            {
                isReparsePoint = File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
            }
            catch (Exception ex) when (IsTolerable(ex))
            {
                continue;
            }

            if (!isReparsePoint)
            {
                yield return directory;
            }
        }
    }

    public static bool IsTolerable(Exception ex) =>
        ex is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException;
}
