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
using PcOptimizer.Core.Configuration;
using PcOptimizer.Core.Disk;
using PcOptimizer.Core.Firmware;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Performance;
using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Programs;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Services;
using PcOptimizer.Core.Startup;
using PcOptimizer.Core.Summary;
using PcOptimizer.Core.Undo;

namespace PcOptimizer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditLog _audit;
    private readonly IChangeJournal _journal;
    private readonly IRestorePointService _restorePoints;
    private readonly IUserPrompt _prompt;
    private readonly SummaryModule _summary;

    /// <summary>
    /// Ultimo analisis de cada apartado, con la seleccion que tuviera. Sin
    /// esto, cambiar de apartado tiraba el analisis y habia que repetirlo.
    /// </summary>
    private readonly Dictionary<string, CachedScan> _scans = new(StringComparer.Ordinal);

    private sealed record CachedScan(IReadOnlyList<SelectableFinding> Items, DateTime At, string Summary);

    /// <summary>
    /// El apartado que se esta analizando. Puede no ser el que se ve: se puede
    /// navegar mientras el resumen trabaja, y sus resultados no deben acabar
    /// pintados en la pagina equivocada.
    /// </summary>
    private IOptimizerModule? _running;

    /// <summary>Fichas del resumen que ya han llegado mientras sigue analizando.</summary>
    private readonly List<SelectableFinding> _liveCards = [];

    private CancellationTokenSource? _work;

    public MainViewModel(
        IUserPrompt? prompt = null,
        IRestorePointService? restorePoints = null,
        IAuditLog? audit = null,
        IChangeJournal? journal = null,
        SettingsStore? settingsStore = null,
        AppSettings? settings = null)
    {
        var store = settingsStore ?? new SettingsStore();
        Settings = new SettingsViewModel(store, settings ?? store.Load());

        _prompt = prompt ?? new MessageBoxPrompt();
        _audit = audit ?? new FileAuditLog();
        _journal = journal ?? new JsonChangeJournal();
        _restorePoints = restorePoints ?? new WmiRestorePointService();

        // Una sola lectura del hardware para los tres modulos que lo usan.
        var hardware = new HardwareProfileCache();

        var inventory = new HardwareInventory(hardware);
        var undo = new UndoModule(_journal, _audit);

        // Fuera del resumen: sus opciones (abrir la BIOS, arrancar desde un
        // USB) no son tareas pendientes del equipo sino herramientas.
        var boot = new FirmwareSettingsModule(_audit, _journal);

        IOptimizerModule[] sections =
        [
            new GamingPerformanceModule(_audit, _journal, hardware.Get),
            new TempFileCleaner(TempFileCleaner.DefaultTargets(), _audit),
            new DiskSpaceAnalyzer(_audit),
            new BloatwareModule(audit: _audit),
            new InstalledProgramsModule(_audit),
            new StartupManager(_audit, _journal),
            new ServiceManager(_audit, _journal),
            new FirmwareDiagnostics(hardware.Get)
        ];

        // El resumen revisa los apartados, no el inventario (lo resume la
        // ficha del equipo) ni deshacer (no es el estado del equipo).
        _summary = new SummaryModule(sections, hardware.Get)
        {
            Progress = new Progress<string>(message =>
            {
                if (IsBusy)
                {
                    Status = message;
                }
            }),

            CardReady = new Progress<Finding>(card =>
            {
                if (!IsBusy || _running is not SummaryModule)
                {
                    return;
                }

                var item = new SelectableFinding(card);
                _liveCards.Add(item);

                if (SelectedModule is SummaryModule)
                {
                    Findings.Add(Track(item));
                }
            })
        };

        Modules = [_summary, inventory, .. sections, boot, undo];

        IsElevated = CheckElevated();
        _selectedModule = Modules[0];

        _status = Settings.AnalyzeOnStartup
            ? "Preparando el análisis del equipo..."
            : "Pulsa Analizar para revisar todo el equipo de una vez.";

        Findings.CollectionChanged += (_, _) =>
        {
            RefreshSelectionSummary();
            OnPropertyChanged(nameof(MachineCard));
            OnPropertyChanged(nameof(HasMachineCard));
            OnPropertyChanged(nameof(SectionCards));
            OnPropertyChanged(nameof(HasFindings));
        };
        Errors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasErrors));
    }

    public ObservableCollection<IOptimizerModule> Modules { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Lo que marca la barra lateral. Con Ajustes abierto no marca ningun
    /// apartado, pero el apartado actual se conserva con su analisis para
    /// volver a el tal cual.
    /// </summary>
    public IOptimizerModule? SidebarSelection
    {
        get => IsSettingsOpen ? null : SelectedModule;
        set
        {
            // La lista pone null al desmarcar cuando se abre Ajustes: se ignora.
            if (value is null)
            {
                return;
            }

            IsSettingsOpen = false;
            SelectedModule = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarSelection))]
    [NotifyPropertyChangedFor(nameof(PageKind))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageDescription))]
    [NotifyPropertyChangedFor(nameof(ShowScan))]
    [NotifyPropertyChangedFor(nameof(ShowRemediation))]
    [NotifyPropertyChangedFor(nameof(NeedsElevationWarning))]
    private bool _isSettingsOpen;

    /// <summary>Que vista pinta el cuerpo de la ventana.</summary>
    public string PageKind => IsSettingsOpen
        ? "Settings"
        : SelectedModule is SummaryModule
            ? "Summary"
            : IsDiagnostic ? "Diagnostic" : "Actionable";

    public string PageTitle => IsSettingsOpen ? "Ajustes" : SelectedModule.DisplayName;

    public string PageDescription => IsSettingsOpen
        ? "Preferencias de la aplicación. Los cambios se guardan al momento."
        : SelectedModule.Description;

    public bool ShowScan => !IsSettingsOpen;

    public bool ShowRemediation => !IsSettingsOpen && !IsDiagnostic;

    /// <summary>La ficha del equipo en el resumen.</summary>
    public SelectableFinding? MachineCard => SelectedModule is SummaryModule && !IsSettingsOpen
        ? Findings.FirstOrDefault(f => f.RelatedModuleId == HardwareInventory.ModuleId)
        : null;

    public bool HasMachineCard => MachineCard is not null;

    public bool HasFindings => Findings.Count > 0;

    /// <summary>Las fichas de apartados en el resumen.</summary>
    public IReadOnlyList<SelectableFinding> SectionCards => SelectedModule is SummaryModule
        ? Findings.Where(f => f.RelatedModuleId != HardwareInventory.ModuleId).ToList()
        : [];

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
    [NotifyPropertyChangedFor(nameof(ModuleDescription))]
    [NotifyPropertyChangedFor(nameof(ApplyVerb))]
    [NotifyPropertyChangedFor(nameof(NeedsElevationWarning))]
    [NotifyPropertyChangedFor(nameof(ShowRestorePointOption))]
    [NotifyPropertyChangedFor(nameof(ShowRebootDeletionOption))]
    [NotifyPropertyChangedFor(nameof(SidebarSelection))]
    [NotifyPropertyChangedFor(nameof(PageKind))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageDescription))]
    [NotifyPropertyChangedFor(nameof(ShowRemediation))]
    [NotifyPropertyChangedFor(nameof(MachineCard))]
    [NotifyPropertyChangedFor(nameof(HasMachineCard))]
    [NotifyPropertyChangedFor(nameof(SectionCards))]
    private IOptimizerModule _selectedModule;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SimulateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>
    /// Aplicando cambios. Mientras dura no se puede cambiar de apartado: los
    /// errores y el re-analisis tienen que quedarse en la pagina donde se aplico.
    /// Analizar, en cambio, si deja navegar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigate))]
    [NotifyCanExecuteChangedFor(nameof(GoToModuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private bool _isApplying;

    public bool CanNavigate => !IsApplying;

    [ObservableProperty]
    private bool _createRestorePoint = true;

    [ObservableProperty]
    private bool _deleteLockedOnReboot;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    /// <summary>
    /// Lo que hace el modulo actual. Se expone desde aqui a proposito: un
    /// enlace de XAML resuelve por reflexion sobre el tipo concreto y no ve
    /// los miembros por defecto de una interfaz.
    /// </summary>
    public string ModuleDescription => SelectedModule.Description;

    public string ApplyVerb => SelectedModule.ApplyVerb;

    /// <summary>El modulo actual solo informa: no hay nada que seleccionar ni aplicar.</summary>
    public bool IsDiagnostic => SelectedModule.Kind == ModuleKind.Diagnostic;

    public bool NeedsElevationWarning => !IsSettingsOpen && SelectedModule.RequiresElevation && !IsElevated;

    /// <summary>
    /// Restaurar sistema no cubre ficheros: ofrecer un punto de restauracion
    /// antes de borrar temporales solo llena el disco de puntos inutiles.
    /// </summary>
    public bool ShowRestorePointOption => !IsDiagnostic && SelectedModule.BenefitsFromRestorePoint;

    public bool ShowRebootDeletionOption => !IsDiagnostic && SelectedModule.SupportsRebootDeletion;

    partial void OnSelectedModuleChanged(IOptimizerModule value)
    {
        ClearResults();

        if (RestoreFromCache(value))
        {
            return;
        }

        // El resumen sigue trabajando en segundo plano.
        if (IsBusy && _running is SummaryModule overview)
        {
            if (value is SummaryModule)
            {
                foreach (var card in _liveCards)
                {
                    Findings.Add(Track(card));
                }

                return;
            }

            if (overview.Covers(value.Id))
            {
                Status = $"{value.DisplayName}: se está analizando con todo lo demás; aparecerá aquí en cuanto termine.";
                return;
            }
        }

        if (IsBusy && _running is { } other && !ReferenceEquals(other, value))
        {
            Status = $"{value.DisplayName}: pulsa Analizar cuando termine el análisis de {other.DisplayName}.";
            return;
        }

        Status = value.Id == SummaryModule.ModuleId
            ? "Pulsa Analizar para revisar todo el equipo de una vez."
            : $"{value.DisplayName}: pulsa Analizar.";
    }

    /// <summary>
    /// Pinta el ultimo analisis guardado de un apartado. Devuelve false si no
    /// hay ninguno.
    /// </summary>
    private bool RestoreFromCache(IOptimizerModule module)
    {
        if (!_scans.TryGetValue(module.Id, out var cached))
        {
            return false;
        }

        foreach (var item in cached.Items)
        {
            Findings.Add(Track(item));
        }

        Status = $"{cached.Summary} Analizado a las {cached.At:HH:mm}; pulsa Analizar para actualizar.";
        return true;
    }

    /// <summary>
    /// El analisis de arranque. Lo llama la ventana en cuanto se ha pintado:
    /// al abrir la aplicacion ya deberia estar todo revisado sin pedirlo.
    /// </summary>
    public Task StartAsync()
    {
        if (!Settings.AnalyzeOnStartup
            || SelectedModule is not SummaryModule
            || !ScanCommand.CanExecute(null))
        {
            return Task.CompletedTask;
        }

        return ScanCommand.ExecuteAsync(null);
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

    /// <summary>
    /// Desde una ficha del resumen, al apartado que describe. Si el resumen ya
    /// lo analizo se muestran esos resultados; si no, se analiza al entrar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task GoToModuleAsync(SelectableFinding? item)
    {
        var targetId = item?.Model.RelatedModuleId;

        var target = targetId is null
            ? null
            : Modules.FirstOrDefault(m => m.Id == targetId);

        if (target is null || ReferenceEquals(target, SelectedModule))
        {
            return;
        }

        IsSettingsOpen = false;
        SelectedModule = target;

        if (!_scans.ContainsKey(target.Id) && !IsBusy)
        {
            await RunScanAsync(preserveStatus: false).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void OpenSettings() => IsSettingsOpen = true;

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
        _running = module;
        _liveCards.Clear();
        ClearResults();

        if (!preserveStatus)
        {
            Status = $"Analizando {module.DisplayName}...";
        }

        try
        {
            var results = await Task.Run(
                () => module.ScanAsync(work.Token), work.Token).ConfigureAwait(true);

            var items = results.Select(f => new SelectableFinding(f)).ToList();
            var summary = Summarize(module, results);

            Remember(module, items, summary);

            // El resumen trae el analisis completo de cada apartado: se
            // guarda para que entrar en ellos sea instantaneo.
            if (module is SummaryModule overview)
            {
                RememberFromSummary(overview);
            }

            if (ReferenceEquals(SelectedModule, module))
            {
                // Fuera las fichas que llegaron sueltas: estas van ordenadas.
                ClearResults();

                foreach (var item in items)
                {
                    Findings.Add(Track(item));
                }

                Status = preserveStatus
                    ? $"{previousStatus} Quedan {results.Count} elementos."
                    : summary;
            }
            else if (module is SummaryModule finished && finished.Covers(SelectedModule.Id))
            {
                // El usuario se fue a otro apartado mientras tanto, y el
                // resumen acaba de traer justo ese: se muestra ya, con los
                // datos nuevos aunque hubiera otros de antes.
                ClearResults();
                RestoreFromCache(SelectedModule);
            }
            else
            {
                Status = $"{module.DisplayName} analizado: {summary}";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Análisis cancelado.";
        }
        catch (Exception ex)
        {
            Status = $"Error durante el análisis de {module.DisplayName}: {ex.Message}";
        }
        finally
        {
            _running = null;
            _liveCards.Clear();
            EndWork();
        }
    }

    private async Task RunAsync(bool simulate)
    {
        IsApplying = true;

        try
        {
            await RunCoreAsync(simulate).ConfigureAwait(true);
        }
        finally
        {
            IsApplying = false;
        }
    }

    private async Task RunCoreAsync(bool simulate)
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
                Status = "Creando punto de restauración...";

                var failure = await Task.Run(CreateRestorePointOrDescribeFailure, work.Token)
                    .ConfigureAwait(true);

                if (failure is not null && !ConfirmWithoutRestorePoint(failure))
                {
                    Status = $"No se pudo crear el punto de restauración: {failure}. "
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
            Status = "Operación cancelada. Lo ya aplicado no se deshace solo: "
                     + "revisa el registro de auditoría.";
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

        if (!simulate && result.RequiresRestart && result.Applied > 0)
        {
            OfferRestart();
        }

        // Tras aplicar de verdad, la lista esta desfasada. Se vuelve a
        // analizar, pero conservando el mensaje: es el resultado de lo que
        // el usuario acaba de hacer y es lo que quiere leer.
        if (!simulate)
        {
            // Aplicar en un apartado puede cambiar lo que ven otros (quitar un
            // programa quita su servicio y su entrada de arranque; deshacer
            // cambia el rendimiento). Nada de lo guardado es ya fiable, ni
            // siquiera lo de este apartado si el re-analisis fallara.
            _scans.Clear();

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
            $"{module.ApplyVerb} sobre {selected.Count} elementos{size} en \"{module.DisplayName}\".\n\n"
            + "Esta acción no se puede deshacer desde la aplicación.\n\n"
            + "Si prefieres ver antes qué pasaría sin tocar nada, cancela y pulsa Simular.");
    }

    private bool ConfirmWithoutRestorePoint(string failure)
        => _prompt.ConfirmDestructive(
            "Sin punto de restauración",
            $"No se ha podido crear el punto de restauración: {failure}\n\n"
            + "Puedes continuar de todos modos, pero no habrá vuelta atrás automática.\n\n"
            + "Aceptar para continuar sin punto de restauración.");

    /// <summary>
    /// Lo aplicado no surte efecto hasta reiniciar: se ofrece hacerlo ya. Si
    /// el usuario dice que no, queda programado para cuando reinicie el.
    /// </summary>
    private void OfferRestart()
    {
        var restartNow = _prompt.Ask(
            "Reiniciar",
            "Los cambios se aplicarán en el próximo reinicio.\n\n"
            + "¿Reiniciar ahora? Guarda antes lo que tengas abierto en otros programas.");

        if (!restartNow)
        {
            Status += " Se aplicará cuando reinicies.";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 5")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Status += " Reiniciando en 5 segundos...";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status += $" No se pudo reiniciar: {ex.Message}. Reinicia tú cuando quieras.";
        }
    }

    /// <summary>Devuelve null si se creo el punto, o el motivo del fallo.</summary>
    private string? CreateRestorePointOrDescribeFailure()
        => _restorePoints.TryCreate("PcOptimizer", out var error)
            ? null
            : error ?? "motivo desconocido";

    private void Remember(IOptimizerModule module, IReadOnlyList<SelectableFinding> items, string summary)
        => _scans[module.Id] = new CachedScan(items, DateTime.Now, summary);

    private void RememberFromSummary(SummaryModule overview)
    {
        foreach (var (moduleId, results) in overview.LastResults)
        {
            var module = Modules.FirstOrDefault(m => m.Id == moduleId);

            if (module is null)
            {
                continue;
            }

            Remember(module, results.Select(r => new SelectableFinding(r)).ToList(), Summarize(module, results));
        }
    }

    private static string Summarize(IOptimizerModule module, IReadOnlyList<Finding> results)
    {
        if (module is SummaryModule)
        {
            var sections = results.OfType<SummaryFinding>().Where(f => f.RelatedModuleId != HardwareInventory.ModuleId).ToList();
            var withWork = sections.Count(f => f.PendingCount > 0);
            var bytes = sections.Sum(f => f.ReclaimableBytes);
            var space = bytes > 0 ? $", {ByteSize.Format(bytes)} recuperables en total" : string.Empty;

            return withWork == 0
                ? $"{sections.Count} apartados revisados, nada pendiente."
                : $"{sections.Count} apartados revisados, {withWork} con cosas que hacer{space}.";
        }

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
        var prefix = result.Simulated ? "Simulación" : "Aplicado";
        var parts = new List<string> { $"{result.Applied} correctos" };

        if (result.Failed > 0)
        {
            parts.Add($"{result.Failed} fallidos");
        }

        if (result.Deferred > 0)
        {
            parts.Add($"{result.Deferred} se borrarán al reiniciar");
        }

        if (result.BytesFreed > 0)
        {
            parts.Add(result.Simulated
                ? $"{ByteSize.Format(result.BytesFreed)} se liberarían"
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
