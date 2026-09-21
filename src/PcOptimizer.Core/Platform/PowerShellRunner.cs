using System.Diagnostics;
using System.Text;

namespace PcOptimizer.Core.Platform;

public sealed record PowerShellResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Ejecuta comandos de PowerShell. Se usa para las APIs de Windows que no
/// tienen equivalente razonable en .NET puro, como el inventario de paquetes
/// AppX. Nunca se interpolan datos del usuario en el script: los argumentos
/// van por variables con comillas simples y escapado explicito.
/// </summary>
public interface IPowerShellRunner
{
    Task<PowerShellResult> RunAsync(string script, CancellationToken cancellationToken = default);
}

public sealed class PowerShellRunner : IPowerShellRunner
{
    private readonly TimeSpan _timeout;

    public PowerShellRunner(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromMinutes(3);

    /// <summary>
    /// Convierte un valor en un literal de cadena de PowerShell seguro.
    /// Las comillas simples impiden cualquier interpolacion; duplicarlas
    /// impide cerrar la cadena antes de tiempo.
    /// </summary>
    public static string Quote(string value)
        => "'" + value.Replace("'", "''") + "'";

    public async Task<PowerShellResult> RunAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-OutputFormat");
        startInfo.ArgumentList.Add("Text");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new PowerShellResult(-1, string.Empty, $"No se pudo iniciar PowerShell: {ex.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // Una cancelacion pedida por el usuario se propaga; un planton del
            // proceso se devuelve como error para no tumbar el analisis entero.
            cancellationToken.ThrowIfCancellationRequested();
            return new PowerShellResult(-1, string.Empty, "PowerShell no respondió a tiempo.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new PowerShellResult(process.ExitCode, output, error);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // El proceso ya se habia ido.
        }
    }
}
