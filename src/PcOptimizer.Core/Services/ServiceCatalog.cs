namespace PcOptimizer.Core.Services;

public enum ServiceVerdict
{
    /// <summary>Windows o seguridad depende de el. No se ofrece nunca.</summary>
    Protected,

    /// <summary>Se puede pasar a manual sin romper nada para la mayoria de la gente.</summary>
    SafeToDefer,

    /// <summary>Ni critico ni catalogado. Se lista, sin recomendar nada.</summary>
    Unknown
}

/// <summary>
/// Que servicios se pueden tocar y cuales no. Todo el criterio esta aqui, en
/// listas explicitas. Un optimizador que desactiva servicios "por si acaso"
/// es como se deja un equipo sin red, sin sonido o sin impresora.
///
/// Nada se desactiva del todo: lo maximo que hace esta aplicacion es pasarlo
/// a Manual, para que Windows pueda arrancarlo si algo lo necesita.
/// </summary>
public static class ServiceCatalog
{
    /// <summary>
    /// Servicios que no se ofrecen jamas: nucleo del sistema, red, seguridad,
    /// audio, almacenamiento y actualizaciones.
    /// </summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        // Nucleo y sesion
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "ProfSvc", "Themes",
        "UserManager", "SystemEventsBroker", "TimeBrokerSvc", "CoreMessagingRegistrar",
        "Power", "PlugPlay", "gpsvc", "Schedule", "EventLog", "EventSystem",

        // Red
        "Dhcp", "Dnscache", "NlaSvc", "netprofm", "nsi", "WlanSvc", "WwanSvc",
        "LanmanServer", "LanmanWorkstation", "NetSetupSvc", "WinHttpAutoProxySvc",

        // Seguridad
        "WinDefend", "SecurityHealthService", "wscsvc", "MpsSvc", "BFE",
        "mpssvc", "SgrmBroker", "CryptSvc", "KeyIso", "VaultSvc", "SamSs",

        // Audio (desactivarlo es el error clasico)
        "Audiosrv", "AudioEndpointBuilder",

        // Almacenamiento y actualizaciones
        "vss", "swprv", "VolSnap", "wuauserv", "TrustedInstaller", "msiserver",
        "BITS", "StorSvc", "DeviceInstall", "DsmSvc",

        // Graficos y juegos
        "NVDisplay.ContainerLocalSystem", "AMD External Events Utility",
        "GamingServices", "GamingServicesNet"
    };

    /// <summary>
    /// Servicios que arrancan solos y que la mayoria de la gente no usa. Se
    /// pasan a Manual, no se desactivan: si algo los necesita, Windows los
    /// levanta. El texto explica que pierdes exactamente.
    /// </summary>
    private static readonly Dictionary<string, string> SafeToDefer = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fax"] = "Envío y recepción de faxes. Si no tienes un modem de fax, no hace nada.",
        ["RemoteRegistry"] = "Permite editar el registro de este equipo desde otro. Windows ya lo trae desactivado; si está activo, conviene revisarlo.",
        ["RemoteAccess"] = "Enrutamiento y acceso remoto. Solo lo usan equipos que hacen de router o VPN.",
        ["SharedAccess"] = "Compartir la conexión a internet con otros equipos.",
        ["SessionEnv"] = "Escritorio remoto (parte de configuración de sesión). Innecesario si nunca te conectas en remoto.",
        ["TermService"] = "Escritorio remoto. Si no usas Conexión a Escritorio remoto ni Asistencia rápida, no hace falta.",
        ["UmRdpService"] = "Redirección de impresoras y puertos en Escritorio remoto.",
        ["WMPNetworkSvc"] = "Compartir bibliotecas del Reproductor de Windows Media por la red.",
        ["XblAuthManager"] = "Inicio de sesión en Xbox Live. Quítalo solo si no juegas a nada con cuenta Xbox ni Game Pass.",
        ["XblGameSave"] = "Partidas guardadas en la nube de Xbox. Mismo aviso que el anterior.",
        ["XboxNetApiSvc"] = "Red de Xbox Live para multijugador. Mismo aviso.",
        ["MapsBroker"] = "Descarga de mapas sin conexión para la app Mapas.",
        ["lfsvc"] = "Servicio de ubicación. Si ninguna app tuya usa la ubicación, no hace falta.",
        ["PhoneSvc"] = "Telefonía, para equipos con módulo móvil.",
        ["TapiSrv"] = "Telefonía clásica (modems analógicos).",
        ["PrintNotify"] = "Notificaciones y extensiones de impresora. Imprimir sigue funcionando sin el.",
        ["Spooler"] = "Cola de impresión. Pásalo a manual SOLO si no tienes impresora: sin el no se puede imprimir nada.",
        ["WerSvc"] = "Envío de informes de errores a Microsoft.",
        ["DiagTrack"] = "Telemetría y experiencias del usuario conectado. Recopila datos de uso para Microsoft.",
        ["dmwappushservice"] = "Enrutamiento de mensajes WAP push, asociado a la telemetría.",
        ["RetailDemo"] = "Modo demostración de tienda. En un equipo normal no pinta nada.",
        ["SCardSvr"] = "Tarjetas inteligentes. Innecesario sin lector de tarjetas ni DNI electrónico.",
        ["ScDeviceEnum"] = "Enumeración de dispositivos de tarjeta inteligente.",
        ["SCPolicySvc"] = "Directivas de retirada de tarjeta inteligente.",
        ["SensorService"] = "Sensores (brillo automático, giroscopio). Innecesario en una torre.",
        ["SensrSvc"] = "Supervisión de sensores.",
        ["SensorDataService"] = "Datos de sensores.",
        ["WalletService"] = "Cartera de Windows, descatalogada.",
        ["icssvc"] = "Zona con cobertura móvil (compartir datos móviles).",
        ["WbioSrvc"] = "Biometría. Pásalo a manual solo si no usas huella ni reconocimiento facial."
    };

    /// <summary>
    /// Servicios que se ofrecen con una advertencia mas fuerte porque mucha
    /// gente si los usa sin saber que se llaman asi.
    /// </summary>
    private static readonly HashSet<string> Delicate = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spooler", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "WbioSrvc", "TermService"
    };

    public static ServiceVerdict Classify(string serviceName)
    {
        if (Protected.Contains(serviceName))
        {
            return ServiceVerdict.Protected;
        }

        return SafeToDefer.ContainsKey(serviceName)
            ? ServiceVerdict.SafeToDefer
            : ServiceVerdict.Unknown;
    }

    public static string DescribeReason(string serviceName)
        => SafeToDefer.TryGetValue(serviceName, out var reason) ? reason : string.Empty;

    public static bool IsDelicate(string serviceName) => Delicate.Contains(serviceName);
}
