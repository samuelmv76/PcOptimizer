using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PcOptimizer.Core.Configuration;

namespace PcOptimizer.App.Theming;

/// <summary>
/// Cambia entre el tema oscuro y el claro en caliente. Todos los colores de
/// la interfaz salen de un diccionario de paleta (Themes/Dark.xaml o
/// Themes/Light.xaml) y se usan con DynamicResource, asi que sustituir ese
/// diccionario repinta la ventana entera sin reiniciar.
/// </summary>
public static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private static ResourceDictionary? _palette;
    private static ThemePreference _preference = ThemePreference.Dark;
    private static bool _followingSystem;

    public static bool IsDark { get; private set; } = true;

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;

        ApplyPalette(preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.System => SystemUsesDarkTheme(),
            _ => true
        });

        // Con "como Windows" hay que enterarse de cuando cambia el sistema.
        if (preference == ThemePreference.System && !_followingSystem)
        {
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            _followingSystem = true;
        }
    }

    /// <summary>
    /// Barra de titulo del mismo color que la ventana. Sin esto, Windows la
    /// pinta blanca sobre una aplicacion oscura, que es lo primero que delata
    /// una interfaz poco cuidada. En Windows 10 solo funciona el modo oscuro;
    /// el color exacto requiere Windows 11, y si falla no pasa nada.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        if (Application.Current.TryFindResource("TitleBarColor") is Color caption)
        {
            var value = ToColorRef(caption);
            DwmSetWindowAttribute(handle, DwmCaptionColor, ref value, sizeof(int));
        }

        if (Application.Current.TryFindResource("TitleTextColor") is Color text)
        {
            var value = ToColorRef(text);
            DwmSetWindowAttribute(handle, DwmTextColor, ref value, sizeof(int));
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // 0 = las aplicaciones usan el tema oscuro.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return true;
        }
    }

    private static void ApplyPalette(bool dark)
    {
        IsDark = dark;

        var next = new ResourceDictionary
        {
            Source = new Uri(
                dark
                    ? "pack://application:,,,/Themes/Dark.xaml"
                    : "pack://application:,,,/Themes/Light.xaml",
                UriKind.Absolute)
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        if (_palette is not null)
        {
            dictionaries.Remove(_palette);
        }

        // La paleta va la primera: los estilos de Controls.xaml la usan.
        dictionaries.Insert(0, next);
        _palette = next;

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference != ThemePreference.System || e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        var dark = SystemUsesDarkTheme();

        if (dark == IsDark)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() => ApplyPalette(dark));
    }

    /// <summary>DWM quiere los colores como COLORREF: 0x00BBGGRR.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
