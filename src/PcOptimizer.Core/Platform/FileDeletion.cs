
using System.Runtime.InteropServices;

namespace PcOptimizer.Core.Platform;

/// <summary>
/// Borrado de ficheros con los tropiezos habituales resueltos: atributos de
/// solo lectura, oculto o de sistema, y ficheros abiertos por otro programa.
/// Devuelve un motivo legible en vez de una excepcion: un fichero que no se
/// puede borrar es informacion para el usuario, no un fallo de la aplicacion.
/// </summary>
public static class FileDeletion
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static bool TryDelete(string path, out string? error)
    {
        try
        {
            File.Delete(path);
            error = null;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Causa mas comun con diferencia: el fichero esta marcado como
            // solo lectura, oculto o de sistema. Quitar los atributos y
            // reintentar una vez resuelve la mayoria de estos casos.
            return TryDeleteAfterClearingAttributes(path, out error);
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return false;
        }
    }

    private static bool TryDeleteAfterClearingAttributes(string path, out string? error)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return false;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "sin permisos (puede pertenecer a otra cuenta o estar protegido por Windows)",

        IOException io when IsLocked(io) =>
            "en uso por otro programa",

        IOException io => io.Message,

        _ => ex.Message
    };

    private static bool IsLocked(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is ErrorSharingViolation or ErrorLockViolation;
    }

    /// <summary>
    /// Pide a Windows que borre el fichero en el proximo arranque, antes de
    /// que nada lo abra. Es lo que hacen los instaladores con los ficheros en
    /// uso. Requiere privilegios de administrador y no surte efecto hasta
    /// reiniciar: quien llame a esto debe decirselo al usuario.
    /// </summary>
    public static bool TryScheduleDeleteOnReboot(string path, out string? error)
    {
        try
        {
            if (MoveFileEx(path, null, MoveFileDelayUntilReboot))
            {
                error = null;
                return true;
            }

            var code = Marshal.GetLastWin32Error();
            error = code == ErrorAccessDenied
                ? "hace falta ejecutar la aplicación como administrador"
                : $"Windows devolvió el error {code}";

            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            error = "no disponible en este sistema";
            return false;
        }
    }

    /// <summary>Si el motivo del fallo es que otro programa tiene el fichero abierto.</summary>
    public static bool IsInUse(string? error)
        => error is not null && error.Contains("en uso", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Borra una carpeta con todo su contenido, quitando atributos por el
    /// camino. Devuelve false y el motivo del primer problema encontrado.
    /// </summary>
    public static bool TryDeleteDirectory(string path, out string? error)
    {
        try
        {
            ClearAttributes(path);
            Directory.Delete(path, recursive: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return false;
        }
    }

    private static void ClearAttributes(string directory)
    {
        foreach (var file in SafeDirectory.EnumerateFiles(directory))
        {
            try
            {
                var attributes = File.GetAttributes(file);

                if (attributes != FileAttributes.Normal)
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
            }
            catch (Exception ex) when (SafeDirectory.IsTolerable(ex))
            {
                // Si no se puede ni leer el atributo, el borrado fallara
                // despues y se reportara alli.
            }
        }
    }

    private const int MoveFileDelayUntilReboot = 0x4;
    private const int ErrorAccessDenied = 5;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
