using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.App.Theming;
using PcOptimizer.Core.Configuration;

namespace PcOptimizer.App.ViewModels;

/// <summary>
/// La pagina de Ajustes. Cada cambio se aplica y se guarda al momento: en
/// una pagina de preferencias no hay boton de Guardar, igual que en macOS.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private AppSettings _settings;

    public SettingsViewModel(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public AppSettings Current => _settings;

    // Un RadioButton por opcion. WPF desmarca los demas del grupo y los pone
    // a false, que aqui se ignora: solo cuenta el que se marca.

    public bool IsDarkTheme
    {
        get => _settings.Theme == ThemePreference.Dark;
        set
        {
            if (value)
            {
                SetTheme(ThemePreference.Dark);
            }
        }
    }

    public bool IsLightTheme
    {
        get => _settings.Theme == ThemePreference.Light;
        set
        {
            if (value)
            {
                SetTheme(ThemePreference.Light);
            }
        }
    }

    public bool IsSystemTheme
    {
        get => _settings.Theme == ThemePreference.System;
        set
        {
            if (value)
            {
                SetTheme(ThemePreference.System);
            }
        }
    }

    public bool AnalyzeOnStartup
    {
        get => _settings.AnalyzeOnStartup;
        set
        {
            if (value == _settings.AnalyzeOnStartup)
            {
                return;
            }

            Save(_settings with { AnalyzeOnStartup = value });
            OnPropertyChanged();
        }
    }

    public string DataFolder => _store.DataFolder;

    public string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "desarrollo" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Si no se puede abrir, la ruta sigue a la vista en la pagina.
        }
    }

    private void SetTheme(ThemePreference theme)
    {
        if (theme == _settings.Theme)
        {
            return;
        }

        Save(_settings with { Theme = theme });
        ThemeManager.Apply(theme);

        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
    }

    private void Save(AppSettings settings)
    {
        _settings = settings;
        _store.TrySave(settings);
    }
}
