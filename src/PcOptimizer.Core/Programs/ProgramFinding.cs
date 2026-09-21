using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Programs;

public sealed class ProgramFinding : Finding
{
    public ProgramFinding(InstalledProgram program, ProgramVerdict verdict, string reason)
        : base(program.RegistryKey, program.Name)
    {
        Program = program;
        Verdict = verdict;

        var pieces = new List<string>();

        if (!string.IsNullOrWhiteSpace(program.Publisher))
        {
            pieces.Add(program.Publisher);
        }

        if (!string.IsNullOrWhiteSpace(program.Version))
        {
            pieces.Add($"versión {program.Version}");
        }

        if (!program.SupportsSilentUninstall)
        {
            pieces.Add("se abrirá su desinstalador");
        }

        var tail = pieces.Count > 0 ? $" ({string.Join(", ", pieces)})" : string.Empty;

        Details = string.IsNullOrEmpty(reason) ? tail.Trim(' ', '(', ')') : $"{reason}{tail}";

        ReclaimableBytes = program.EstimatedBytes;

        Recommendation = program.SupportsSilentUninstall
            ? string.Empty
            : "Este programa no admite desinstalación silenciosa: se abrirá su propio asistente "
              + "y tendrás que seguirlo. Marca pocos a la vez.";

        Severity = verdict == ProgramVerdict.Bloatware
            ? FindingSeverity.Suggestion
            : FindingSeverity.Info;

        // Desinstalar no se deshace desde aqui: nunca va marcado por defecto.
        SelectedByDefault = false;
    }

    public InstalledProgram Program { get; }

    public ProgramVerdict Verdict { get; }

    public override long DisplayBytes => Program.EstimatedBytes;
}
