using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.App.Services;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Bloatware;
using PcOptimizer.Core.Cleaning;
using PcOptimizer.Core.Disk;
using PcOptimizer.Core.Firmware;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Startup;

namespace PcOptimizer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditLog _audit;
    private readonly IRestorePointService _restorePoints;
    private readonly IUserPrompt _prompt;

    private CancellationTokenSource? _work;

    public MainViewModel(
        IUserPrompt? prompt = null,
        IRestorePointService? restorePoints = null,
        IAuditLog? audit = null)
    {
        _prompt = prompt ?? new MessageBoxPrompt();
        _audit = audit ?? new FileAuditLog();
        _restorePoints = restorePoints ?? new WmiRestorePointService();

        Modules =
        [
            new HardwareInventory(),
            new TempFileCleaner(TempFileCleaner.DefaultTargets(), _audit),
            new DiskSpaceAnalyzer(_audit),
            new BloatwareModule(audit: _audit),
            new StartupManager(_audit),
            new FirmwareDiagnostics()
        ];

        IsElevated = CheckElevated();
        _selectedModule = Modules[0];

        Findings.CollectionChanged += (_, _) => RefreshSelectionSummary();
        Errors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasErrors));
    }

    public ObservableCollection<IOptimizerModule> Modules { get; }

    public ObservableCollection<SelectableFinding> Findings { get; } = [];

    /// <summary>
    /// Por que no se pudo aplicar cada elemento. Se muestra entero: un
    /// "3 fallidos" sin decir cual ni por que no le sirve a nadie.
    /// </summary>
    public ObservableCollection<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Si el proceso corre como administrador.</summary>
    public bool IsElevated { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiagnostic))]
    [NotifyPropertyChangedFor(nameof(NeedsElevationWarning))]
    [NotifyPropertyChangedFor(nameof(ShowRestorePointOption))]
    [NotifyPropertyChangedFor(nameof(ShowRebootDeletionOption))]
    private IOptimizerModule _selectedModule;

    [ObservableProperty]
    private string _status = "Elige un apartado y pulsa Analizar.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SimulateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _createRestorePoint = true;

    [ObservableProperty]
    private bool _deleteLockedOnReboot;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    /// <summary>El modulo actual solo informa: no hay nada que seleccionar ni aplicar.</summary>
    public bool IsDiagnostic => SelectedModule.Kind == ModuleKind.Diagnostic;

    public bool NeedsElevationWarning => SelectedModule.RequiresElevation && !IsElevated;

    /// <summary>
    /// Restaurar sistema no cubre ficheros: ofrecer un punto de restauracion
    /// antes de borrar temporales solo llena el disco de puntos inutiles.
    /// </summary>
    public bool ShowRestorePointOption => !IsDiagnostic && SelectedModule.BenefitsFromRestorePoint;

    public bool ShowRebootDeletionOption => !IsDiagnostic && SelectedModule.SupportsRebootDeletion;

    partial void OnSelectedModuleChanged(IOptimizerModule value)
    {
        ClearResults();
        Status = $"{value.DisplayName}: pulsa Analizar.";
    }

    private bool CanWork() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task ScanAsync() => RunScanAsync(preserveStatus: false);

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task SimulateAsync() => RunAsync(simulate: true);

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task ApplyAsync() => RunAsync(simulate: false);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _work?.Cancel();
        Status = "Cancelando...";
    }

    [RelayCommand]
    private void SelectAll() => SetSelection(true);

    [RelayCommand]
    private void SelectNone() => SetSelection(false);

    /// <summary>
    /// Analiza el modulo actual. Con preserveStatus el mensaje que ya hay no
    /// se pisa: es el re-analisis que sigue a un Aplicar, y el resultado de
    /// aquel es lo que el usuario necesita seguir leyendo.
    /// </summary>
    private async Task RunScanAsync(bool preserveStatus)
    {
        var module = SelectedModule;
        var previousStatus = Status;

        using var work = StartWork();
        ClearResults();

        if (!preserveStatus)
        {
            Status = $"Analizando {module.DisplayName}...";
        }

        try
        {
            var results = await Task.Run(
                () => module.ScanAsync(work.Token), work.Token).ConfigureAwait(true);

            foreach (var finding in results)
            {
                Findings.Add(Track(new SelectableFinding(finding)));
            }

            Status = preserveStatus
                ? $"{previousStatus} Quedan {results.Count} elementos."
                : Summarize(module, results);
        }
        catch (OperationCanceledException)
        {
            Status = "Analisis cancelado.";
        }
        catch (Exception ex)
        {
            Status = $"Error durante el analisis: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    private async Task RunAsync(bool simulate)
    {
        var module = SelectedModule;

        if (module.Kind == ModuleKind.Diagnostic)
        {
            Status = "Este apartado solo informa: no hay nada que aplicar.";
            return;
        }

        var selected = Findings.Where(f => f.IsSelected).ToList();

        if (selected.Count == 0)
        {
            Status = "No hay nada seleccionado.";
            return;
        }

        if (!simulate && !ConfirmApply(module, selected))
        {
            Status = "Cancelado. No se ha cambiado nada.";
            return;
        }

        var models = selected.Select(f => f.Model).ToList();
        var useRestorePoint = CreateRestorePoint && module.BenefitsFromRestorePoint;

        using var work = StartWork();
        Errors.Clear();
        OnPropertyChanged(nameof(HasErrors));

        RemediationResult result;

        try
        {
            if (!simulate && useRestorePoint)
            {
                Status = "Creando punto de restauracion...";

                var failure = await Task.Run(CreateRestorePointOrDescribeFailure, work.Token)
                    .ConfigureAwait(true);

                if (failure is not null && !ConfirmWithoutRestorePoint(failure))
                {
                    Status = $"No se pudo crear el punto de restauracion: {failure}. "
                             + "No se ha cambiado nada.";
                    return;
                }
            }

            var options = new RemediationOptions
            {
                Simulate = simulate,
                CreateRestorePoint = useRestorePoint,
                DeleteLockedOnReboot = DeleteLockedOnReboot && module.SupportsRebootDeletion
            };

            Status = simulate
                ? $"Simulando sobre {models.Count} elementos..."
                : $"Aplicando sobre {models.Count} elementos...";

            result = await Task
                .Run(() => module.ApplyAsync(models, options, work.Token), work.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Operacion cancelada. Lo ya aplicado no se deshace solo: "
                     + "revisa el registro de auditoria.";
            return;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            return;
        }
        finally
        {
            EndWork();
        }

        Status = Describe(result);

        foreach (var error in result.Errors)
        {
            Errors.Add(error);
        }

        // Tras aplicar de verdad, la lista esta desfasada. Se vuelve a
        // analizar, pero conservando el mensaje: es el resultado de lo que
        // el usuario acaba de hacer y es lo que quiere leer.
        if (!simulate)
        {
            var reportedErrors = Errors.ToList();

            await RunScanAsync(preserveStatus: true).ConfigureAwait(true);

            foreach (var error in reportedErrors)
            {
                Errors.Add(error);
            }
        }
    }

    private bool ConfirmApply(IOptimizerModule module, IReadOnlyCollection<SelectableFinding> selected)
    {
        var bytes = selected.Sum(f => f.ReclaimableBytes);
        var size = bytes > 0 ? $" ({ByteSize.Format(bytes)})" : string.Empty;

        return _prompt.ConfirmDestructive(
            "Confirmar",
            $"Se va a aplicar sobre {selected.Count} elementos{size} en \"{module.DisplayName}\".\n\n"
            + "Esta accion no se puede deshacer desde la aplicacion.\n\n"
            + "Si prefieres ver antes que pasaria sin tocar nada, cancela y pulsa Simular.");
    }

    private bool ConfirmWithoutRestorePoint(string failure)
        => _prompt.ConfirmDestructive(
            "Sin punto de restauracion",
            $"No se ha podido crear el punto de restauracion: {failure}\n\n"
            + "Puedes continuar de todos modos, pero no habra vuelta atras automatica.\n\n"
            + "Aceptar para continuar sin punto de restauracion.");

    /// <summary>Devuelve null si se creo el punto, o el motivo del fallo.</summary>
    private string? CreateRestorePointOrDescribeFailure()
        => _restorePoints.TryCreate("PcOptimizer", out var error)
            ? null
            : error ?? "motivo desconocido";

    private static string Summarize(IOptimizerModule module, IReadOnlyList<Finding> results)
    {
        if (results.Count == 0)
        {
            return "No se ha encontrado nada.";
        }

        if (module.Kind == ModuleKind.Diagnostic)
        {
            var toReview = results.Count(f => f.Severity != FindingSeverity.Info);

            return toReview > 0
                ? $"{results.Count} comprobaciones, {toReview} merecen un vistazo."
                : $"{results.Count} comprobaciones, nada que corregir.";
        }

        var total = results.Sum(f => f.ReclaimableBytes);

        return total > 0
            ? $"{results.Count} elementos, {ByteSize.Format(total)} recuperables."
            : $"{results.Count} elementos encontrados.";
    }

    private static string Describe(RemediationResult result)
    {
        var prefix = result.Simulated ? "Simulacion" : "Aplicado";
        var parts = new List<string> { $"{result.Applied} correctos" };

        if (result.Failed > 0)
        {
            parts.Add($"{result.Failed} fallidos");
        }

        if (result.Deferred > 0)
        {
            parts.Add($"{result.Deferred} se borraran al reiniciar");
        }

        if (result.BytesFreed > 0)
        {
            parts.Add(result.Simulated
                ? $"{ByteSize.Format(result.BytesFreed)} se liberarian"
                : $"{ByteSize.Format(result.BytesFreed)} liberados");
        }

        return $"{prefix}: {string.Join(", ", parts)}.";
    }

    private CancellationTokenSource StartWork()
    {
        _work = new CancellationTokenSource();
        IsBusy = true;
        return _work;
    }

    private void EndWork()
    {
        IsBusy = false;
        _work = null;
    }

    private void SetSelection(bool selected)
    {
        foreach (var finding in Findings)
        {
            finding.IsSelected = selected;
        }
    }

    private SelectableFinding Track(SelectableFinding finding)
    {
        finding.PropertyChanged += OnFindingChanged;
        return finding;
    }

    private void ClearResults()
    {
        foreach (var finding in Findings)
        {
            finding.PropertyChanged -= OnFindingChanged;
        }

        Findings.Clear();
        Errors.Clear();
        OnPropertyChanged(nameof(HasErrors));
    }

    private void OnFindingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableFinding.IsSelected))
        {
            RefreshSelectionSummary();
        }
    }

    private void RefreshSelectionSummary()
    {
        if (IsDiagnostic)
        {
            SelectionSummary = string.Empty;
            return;
        }

        var selected = Findings.Where(f => f.IsSelected).ToList();

        if (selected.Count == 0)
        {
            SelectionSummary = "Nada seleccionado.";
            return;
        }

        var bytes = selected.Sum(f => f.ReclaimableBytes);

        SelectionSummary = bytes > 0
            ? $"{selected.Count} seleccionados, {ByteSize.Format(bytes)}."
            : $"{selected.Count} seleccionados.";
    }

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
