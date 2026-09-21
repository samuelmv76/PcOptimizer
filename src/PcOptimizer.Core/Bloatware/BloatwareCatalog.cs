namespace PcOptimizer.Core.Bloatware;

public enum AppClassification
{
    /// <summary>Parte del sistema. Nunca se ofrece para desinstalar.</summary>
    Protected,

    /// <summary>Preinstalada y prescindible para la mayoria de la gente.</summary>
    Bloatware,

    /// <summary>Ni sistema ni basura conocida. Se lista, pero sin recomendar nada.</summary>
    Other
}

/// <summary>
/// Que se puede quitar y que no. Todo el criterio de la aplicacion sobre
/// paquetes AppX esta en este fichero, en listas explicitas: la logica no
/// adivina por el nombre del publicador ni borra por patrones vagos.
/// </summary>
public static class BloatwareCatalog
{
    /// <summary>
    /// Paquetes que la aplicacion no ofrece nunca. Quitarlos rompe algo que
    /// el usuario espera que funcione, y varios no se pueden reinstalar sin
    /// reinstalar Windows.
    /// </summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tienda e instalador: sin ellos no se puede reinstalar nada.
        "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.Services.Store.Engagement",

        // Seguridad.
        "Microsoft.SecHealthUI",
        "Microsoft.Windows.SecHealthUI",

        // Piezas del propio Windows.
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.ContentDeliveryManager",
        "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.Windows.Client.CBS",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.CredDialogHost",
        "Microsoft.ECApp",
        "Microsoft.LockApp",
        "Microsoft.Win32WebViewHost",
        "Microsoft.WindowsAppRuntime.CBS",
        "Microsoft.MicrosoftEdge.Stable",
        "Microsoft.MicrosoftEdgeDevToolsClient",
        "Microsoft.WebMediaExtensions",
        "Microsoft.WebpImageExtension",
        "Microsoft.HEIFImageExtension",
        "Microsoft.VP9VideoExtensions",
        "Microsoft.AV1VideoExtension",
        "Microsoft.RawImageExtension",
        "Microsoft.UI.Xaml.CBS",

        // Necesario para Game Pass y para varios juegos. Justo lo contrario
        // de lo que quiere alguien que optimiza el PC para jugar.
        "Microsoft.GamingServices",

        // Accesibilidad y entrada.
        "Microsoft.Windows.NarratorQuickStart",
        "Microsoft.Windows.PeopleExperienceHost",
        "Microsoft.Windows.PinningConfirmationDialog",
        "Microsoft.InputApp",
        "Microsoft.WindowsCalculator",
        "Microsoft.WindowsNotepad",
        "Microsoft.Paint",
        "Microsoft.ScreenSketch",
        "Microsoft.Windows.Photos",
        "MicrosoftWindows.Client.WebExperience"
    };

    /// <summary>
    /// Preinstaladas prescindibles. Cada una se puede volver a instalar desde
    /// la Tienda, asi que quitarlas es reversible.
    /// </summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clipchamp.Clipchamp"] = "Editor de video preinstalado",
        ["Microsoft.3DBuilder"] = "Editor 3D descatalogado",
        ["Microsoft.Microsoft3DViewer"] = "Visor 3D descatalogado",
        ["Microsoft.MixedReality.Portal"] = "Portal de realidad mixta, servicio discontinuado",
        ["Microsoft.BingNews"] = "Noticias de Bing",
        ["Microsoft.BingWeather"] = "El Tiempo de Bing",
        ["Microsoft.BingFinance"] = "Finanzas de Bing",
        ["Microsoft.BingSports"] = "Deportes de Bing",
        ["Microsoft.BingSearch"] = "Búsqueda web de Bing",
        ["Microsoft.GetHelp"] = "Asistencia de Microsoft",
        ["Microsoft.Getstarted"] = "Sugerencias de Windows",
        ["Microsoft.Messaging"] = "Mensajes, discontinuado",
        ["Microsoft.MicrosoftOfficeHub"] = "Acceso directo a Office",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Solitario con anuncios",
        ["Microsoft.MicrosoftStickyNotes"] = "Notas rápidas",
        ["Microsoft.People"] = "Contactos, apenas integrado",
        ["Microsoft.SkypeApp"] = "Skype preinstalado",
        ["Microsoft.Todos"] = "Microsoft To Do",
        ["Microsoft.WindowsFeedbackHub"] = "Centro de opiniones",
        ["Microsoft.WindowsMaps"] = "Mapas de Windows",
        ["Microsoft.YourPhone"] = "Móvil vinculado",
        ["Microsoft.ZuneMusic"] = "Reproductor multimedia de Microsoft",
        ["Microsoft.ZuneVideo"] = "Películas y TV",
        ["Microsoft.WindowsSoundRecorder"] = "Grabadora de voz",
        ["Microsoft.WindowsCamera"] = "Cámara",
        ["Microsoft.WindowsAlarms"] = "Alarmas y reloj",
        ["Microsoft.OutlookForWindows"] = "Outlook nuevo, preinstalado",
        ["MicrosoftTeams"] = "Teams personal, preinstalado",
        ["MSTeams"] = "Teams, preinstalado",
        ["Microsoft.549981C3F5F10"] = "Cortana",
        ["Microsoft.Copilot"] = "Copilot",
        ["Microsoft.XboxApp"] = "Xbox, versión antigua descatalogada",
        ["Microsoft.Xbox.TCUI"] = "Interfaz de Xbox Live para apps antiguas",
        ["Microsoft.XboxGameOverlay"] = "Superposición de Xbox, versión antigua",
        ["Microsoft.XboxSpeechToTextOverlay"] = "Subtítulos de Xbox",
        ["Microsoft.MicrosoftJournal"] = "Journal, app de notas a mano",
        ["Microsoft.PowerAutomateDesktop"] = "Power Automate preinstalado",
        ["Microsoft.Whiteboard"] = "Pizarra",
        ["Microsoft.Wallet"] = "Cartera, discontinuada",
        ["Microsoft.OneConnect"] = "Movistar/operadoras, discontinuado",
        ["Microsoft.Print3D"] = "Impresión 3D, discontinuada",
        ["Microsoft.NetworkSpeedTest"] = "Test de velocidad",
        ["Microsoft.Advertising.Xaml"] = "Biblioteca de anuncios"
    };

    /// <summary>
    /// Preinstaladas de terceros que llegan con el fabricante del equipo.
    /// Se comparan por fragmento porque el nombre exacto cambia entre lotes.
    /// </summary>
    private static readonly string[] ThirdPartyFragments =
    [
        "CandyCrush", "king.com", "BubbleWitch", "FarmHeroes", "MarchofEmpires",
        "Spotify", "Disney", "Netflix", "TikTok", "Facebook", "Instagram",
        "Twitter", "LinkedIn", "Amazon", "Prime", "Booking", "Duolingo",
        "AdobePhotoshopExpress", "AdobeExpress", "Dropbox", "Plex", "Hidden City",
        "Asphalt", "RoyalRevolt", "Sketchable", "Flipboard", "Keeper", "ExpressVPN",
        "McAfee", "Norton", "WildTangent", "PicsArt", "Playtika", "Wunderlist"
    ];

    /// <summary>
    /// Paquetes que se ofrecen, pero con una advertencia porque alguien
    /// los puede estar usando de verdad.
    /// </summary>
    private static readonly Dictionary<string, string> RemoveWithCare = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.XboxGamingOverlay"] = "Es la Game Bar (Win+G): grabación, captura y widget de FPS. "
                                          + "Quítala solo si no la usas; algunos juegos la necesitan para "
                                          + "la superposición de Game Pass.",
        ["Microsoft.XboxIdentityProvider"] = "Necesaria para iniciar sesión con cuenta Xbox. "
                                             + "Sin ella, varios juegos con Xbox Live no arrancan.",
        ["Microsoft.GamingApp"] = "Es la app Xbox / Game Pass. Quítala solo si no juegas con Game Pass.",
        ["Microsoft.WindowsTerminal"] = "Terminal de Windows. No es basura: solo quítala si no la usas."
    };

    public static AppClassification Classify(string packageName)
    {
        if (Protected.Contains(packageName))
        {
            return AppClassification.Protected;
        }

        if (Known.ContainsKey(packageName) || RemoveWithCare.ContainsKey(packageName))
        {
            return AppClassification.Bloatware;
        }

        return ThirdPartyFragments.Any(f => packageName.Contains(f, StringComparison.OrdinalIgnoreCase))
            ? AppClassification.Bloatware
            : AppClassification.Other;
    }

    public static string DescribeReason(string packageName)
    {
        if (Known.TryGetValue(packageName, out var reason))
        {
            return reason;
        }

        return ThirdPartyFragments.Any(f => packageName.Contains(f, StringComparison.OrdinalIgnoreCase))
            ? "Preinstalada por el fabricante del equipo"
            : string.Empty;
    }

    public static string? DescribeCaveat(string packageName)
        => RemoveWithCare.TryGetValue(packageName, out var caveat) ? caveat : null;
}
