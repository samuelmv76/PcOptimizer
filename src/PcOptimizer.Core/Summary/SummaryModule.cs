using System.Collections.Concurrent;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Summary;

/// <summary>
/// Analiza todos los apartados de una vez, en paralelo, y resume cada uno en
/// una ficha: cuanto hay pendiente y que es lo mas importante. No cambia
/// nada: es un punto de partida para saber por donde empezar.
///
/// Los resultados completos de cada apartado se guardan en LastResults, para
/// que al entrar en uno desde el resumen no haya que analizarlo otra vez.
/// </summary>
public sealed class SummaryModule : DiagnosticModule
{
    public const string ModuleId = "summary";

    /// <summary>Cuantos titulos destacados se nombran por ficha.</summary>
    private const int HighlightCount = 3;

    private readonly IReadOnlyList<IOptimizerModule> _modules;
    private readonly Func<HardwareProfile> _profileSource;

    private IReadOnlyDictionary<string, IReadOnlyList<Finding>> _lastResults =
        new Dictionary<string, IReadOnlyList<Finding>>();

    /// <param name="modules">
    /// Los apartados a revisar. No deberia incluir el propio resumen, el
    /// inventario de hardware (lo resume la ficha del equipo) ni deshacer.
    /// </param>
    public SummaryModule(IEnumerable<IOptimizerModule> modules, Func<HardwareProfile>? profileSource = null)
    {
        _modules = modules.Where(m => m.Id != ModuleId).ToList();
        _profileSource = profileSource ?? (() => HardwareInventory.Capture());
    }

    public override string Id => ModuleId;

    public override string DisplayName => "Resumen";

    public override string Description =>
        "Analiza todos los apartados a la vez y te dice por dónde empezar. Solo lee: para cambiar algo, entra en cada apartado.";

    /// <summary>Avance del analisis, apartado a apartado.</summary>
    public IProgress<string>? Progress { get; set; }

    /// <summary>
    /// Cada ficha en cuanto esta lista, sin esperar a las demas. Al abrir la
    /// aplicacion el resumen se lanza solo, y ver aparecer las fichas una a
    /// una es muy distinto de mirar una pantalla vacia durante un minuto.
    /// El resultado final de ScanAsync las trae todas, ya ordenadas.
    /// </summary>
    public IProgress<Finding>? CardReady { get; set; }

    /// <summary>Si el resumen analiza ese apartado (y guarda sus resultados).</summary>
    public bool Covers(string moduleId)
        => moduleId == HardwareInventory.ModuleId || _modules.Any(m => m.Id == moduleId);

    /// <summary>Resultados completos del ultimo analisis, por identificador de apartado.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Finding>> LastResults => _lastResults;

    public override async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentDictionary<string, IReadOnlyList<Finding>>();
        var total = _modules.Count;
        var completed = 0;

        Progress?.Report($"Analizando todo el equipo: 0 de {total} apartados listos.");

        var profile = await Task.Run(_profileSource, cancellationToken).ConfigureAwait(false);
        results[HardwareInventory.ModuleId] = HardwareInventory.Describe(profile);

        var machine = DescribeMachine(profile);
        CardReady?.Report(machine);

        // Todos a la vez: son independientes, y el mas lento (las apps de la
        // Store, via PowerShell) no deberia hacer esperar a los demas.
        var scans = _modules.Select(async module =>
        {
            try
            {
                var found = await Task.Run(() => module.ScanAsync(cancellationToken), cancellationToken)
                    .ConfigureAwait(false);

                results[module.Id] = found;

                var card = Describe(module, found);
                CardReady?.Report(card);
                return card;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Un apartado que falla no tumba el resumen: su ficha lo cuenta.
                var card = Failed(module, ex);
                CardReady?.Report(card);
                return card;
            }
            finally
            {
                var done = Interlocked.Increment(ref completed);
                Progress?.Report($"Analizando todo el equipo: {done} de {total} apartados listos.");
            }
        }).ToList();

        var cards = await Task.WhenAll(scans).ConfigureAwait(false);

        _lastResults = new Dictionary<string, IReadOnlyList<Finding>>(results);

        var findings = new List<Finding> { machine };
        findings.AddRange(cards);

        return findings;
    }

    internal static Finding DescribeMachine(HardwareProfile profile)
    {
        var parts = new List<string>();

        if (profile.Cpu is { } cpu && !string.IsNullOrWhiteSpace(cpu.Name))
        {
            parts.Add(cpu.Name);
        }

        if (profile.PrimaryGpu is { } gpu)
        {
            parts.Add(gpu.Name);
        }

        if (profile.TotalMemoryBytes > 0)
        {
            parts.Add($"{ByteSize.Format(profile.TotalMemoryBytes)} de RAM");
        }

        if (profile.Os is { } os && !string.IsNullOrWhiteSpace(os.Caption))
        {
            parts.Add(os.Caption);
        }

        return new SummaryFinding("machine", profile.IsPortable ? "Este portátil" : "Este equipo")
        {
            Details = parts.Count > 0 ? string.Join(" · ", parts) : "No se pudo leer el hardware.",
            Severity = FindingSeverity.Info,
            RelatedModuleId = HardwareInventory.ModuleId,
            Recommendation = profile.IsModestHardware
                ? "Es un equipo modesto: en el apartado de rendimiento, los ajustes visuales si se van a notar."
                : string.Empty
        };
    }

    /// <summary>
    /// Que cuenta como pendiente en un apartado. En los de diagnostico, lo que
    /// no es informativo. En los accionables, ademas, lo que vendria marcado
    /// por defecto: un temporal viejo es Info, pero es justo lo que hay que borrar.
    /// </summary>
    internal static IReadOnlyList<Finding> Pending(IOptimizerModule module, IReadOnlyList<Finding> found)
        => module.Kind == ModuleKind.Diagnostic
            ? found.Where(f => f.Severity != FindingSeverity.Info).ToList()
            : found.Where(f => f.SelectedByDefault || f.Severity != FindingSeverity.Info).ToList();

    internal static Finding Describe(IOptimizerModule module, IReadOnlyList<Finding> found)
    {
        var pending = Pending(module, found);
        var warnings = pending.Count(f => f.Severity == FindingSeverity.Warning);
        var bytes = pending.Sum(f => f.ReclaimableBytes);

        string details;

        if (pending.Count == 0)
        {
            details = found.Count == 0
                ? "Nada que hacer."
                : $"Nada que hacer ({found.Count} elementos revisados).";
        }
        else
        {
            details = $"{pending.Count} para revisar";

            if (bytes > 0)
            {
                details += $", {ByteSize.Format(bytes)} recuperables";
            }

            if (warnings > 0)
            {
                details += warnings == 1 ? ", 1 importante" : $", {warnings} importantes";
            }

            details += ".";
        }

        // Se nombran solo sugerencias y avisos: un temporal se llama como un
        // hash y no le dice nada a nadie.
        var highlights = pending
            .Where(f => f.Severity != FindingSeverity.Info)
            .OrderByDescending(f => f.Severity)
            .Select(f => f.Title)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(HighlightCount)
            .ToList();

        return new SummaryFinding(module.Id, module.DisplayName)
        {
            Details = details,
            PendingCount = pending.Count,
            ReclaimableBytes = bytes,
            RelatedModuleId = module.Id,
            Recommendation = highlights.Count > 0
                ? $"Lo primero: {string.Join("; ", highlights)}."
                : string.Empty,
            Severity = warnings > 0
                ? FindingSeverity.Warning
                : pending.Count > 0
                    ? FindingSeverity.Suggestion
                    : FindingSeverity.Info
        };
    }

    private static Finding Failed(IOptimizerModule module, Exception ex) =>
        new SummaryFinding(module.Id, module.DisplayName)
        {
            Details = $"No se pudo analizar: {ex.Message}",
            RelatedModuleId = module.Id,
            Severity = FindingSeverity.Warning,
            Recommendation = "Entra en el apartado y pulsa Analizar para ver el error completo."
        };
}
