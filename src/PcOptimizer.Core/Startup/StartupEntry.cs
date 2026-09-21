using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Startup;

public enum StartupLocation
{
    CurrentUserRun,
    LocalMachineRun,
    StartupFolder
}

public sealed class StartupEntry : Finding
{
    public StartupEntry(StartupLocation location, string name, string command)
        : base($"{location}:{name}", name)
    {
        Location = location;
        Name = name;
        Command = command;
        Details = command;
        Severity = FindingSeverity.Suggestion;

        // Nunca marcado por defecto: desactivar el arranque de un programa
        // es una decision del usuario, no una limpieza obvia.
        SelectedByDefault = false;
    }

    public StartupLocation Location { get; }

    public string Name { get; }

    public string Command { get; }
}
