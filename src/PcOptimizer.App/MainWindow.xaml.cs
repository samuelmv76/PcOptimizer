using System.Windows;
using PcOptimizer.App.Theming;
using PcOptimizer.App.ViewModels;

namespace PcOptimizer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(settingsStore: App.SettingsStore, settings: App.StartupSettings);
        DataContext = _viewModel;

        // La barra de titulo solo se puede tenir cuando ya existe la ventana nativa.
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        // Se analiza al abrir, pero despues del primer pintado: la ventana
        // aparece al instante y las fichas van llegando, en vez de que la
        // aplicacion parezca colgada mientras lee el equipo.
        ContentRendered += OnFirstRender;
    }

    private async void OnFirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstRender;
        await _viewModel.StartAsync();
    }
}
