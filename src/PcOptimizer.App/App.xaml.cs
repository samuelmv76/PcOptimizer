using System.Windows;
using PcOptimizer.App.Theming;
using PcOptimizer.Core.Configuration;

namespace PcOptimizer.App;

public partial class App : Application
{
    public static SettingsStore SettingsStore { get; } = new();

    /// <summary>Las preferencias leidas al arrancar.</summary>
    public static AppSettings StartupSettings { get; private set; } = AppSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        // El tema se aplica antes de crear la ventana: asi no se ve un
        // parpadeo claro antes de pasar a oscuro.
        StartupSettings = SettingsStore.Load();
        ThemeManager.Apply(StartupSettings.Theme);

        base.OnStartup(e);
    }
}
