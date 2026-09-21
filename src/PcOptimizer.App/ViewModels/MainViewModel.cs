using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Auditing;
using PcOptimizer.Core.Cleaning;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Startup;

namespace PcOptimizer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditLog _audit;
    private readonly IRestorePointService _restorePoints;

    public MainViewModel()
    {
        _audit = new FileAuditLog();
        _restorePoints = new WmiRestorePointService();

        Modules =
        [
            new TempFileCleaner(TempFileCleaner.DefaultTargets(), _audit),
            new StartupManager(_audit)
        ];

        _selectedModule = Modules[0];
    }

    public ObservableCollection<IOptimizerModule> Modules { get; }

    public ObservableCollection<SelectableFinding> Findings { get; } = [];

    [ObservableProperty]
    private IOptimizerModule _selectedModule;

    [ObservableProperty]
    private string _status = "Selecciona un modulo y pulsa Analizar.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _createRestorePoint = true;

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        Findings.Clear();
        Status = $"Analizando: {SelectedModule.DisplayName}...";

        try
        {
            var results = await Task.Run(() => SelectedModule.ScanAsync());

            foreach (var finding in results)
            {
                Findings.Add(new SelectableFinding(finding));
            }

            var total = results.Sum(f => f.ReclaimableBytes);
            Status = total > 0
                ? $"{results.Count} elementos, {SelectableFinding.FormatBytes(total)} recuperables."
                : $"{results.Count} elementos encontrados.";
        }
        catch (Exception ex)
        {
            Status = $"Error durante el analisis: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SimulateAsync() => RunAsync(simulate: true);

    [RelayCommand]
    private Task ApplyAsync() => RunAsync(simulate: false);

    private async Task RunAsync(bool simulate)
    {
        var selected = Findings.Where(f => f.IsSelected).Select(f => f.Model).ToList();
        if (selected.Count == 0)
        {
            Status = "No hay nada seleccionado.";
            return;
        }

        IsBusy = true;

        try
        {
            if (!simulate && CreateRestorePoint)
            {
                Status = "Creando punto de restauracion...";
                if (!_restorePoints.TryCreate("PcOptimizer", out var error))
                {
                    Status = $"No se pudo crear el punto de restauracion: {error}. Operacion cancelada.";
                    return;
                }
            }

            var options = new RemediationOptions
            {
                Simulate = simulate,
                CreateRestorePoint = CreateRestorePoint
            };

            var result = await Task.Run(() => SelectedModule.ApplyAsync(selected, options));

            var prefix = simulate ? "Simulacion" : "Aplicado";
            Status = $"{prefix}: {result.Applied} correctos, {result.Failed} fallidos, " +
                     $"{SelectableFinding.FormatBytes(result.BytesFreed)} liberados.";

            if (!simulate)
            {
                await ScanAsync();
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
