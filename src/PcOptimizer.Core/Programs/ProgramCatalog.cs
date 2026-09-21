namespace PcOptimizer.Core.Programs;

public enum ProgramVerdict
{
    /// <summary>Driver, runtime o pieza del sistema. No se ofrece nunca.</summary>
    Protected,

    /// <summary>Preinstalado por el fabricante y prescindible para la mayoria.</summary>
    Bloatware,

    /// <summary>Lo instalaste tu o no esta catalogado. Se lista sin recomendar nada.</summary>
    Unknown
}

/// <summary>
/// Que programas de escritorio son basura de fabrica y cuales no se tocan.
/// Igual que con las apps de la Store, el criterio esta en listas explicitas:
/// desinstalar por patrones vagos es como alguien se queda sin drivers.
/// </summary>
public static class ProgramCatalog
{
    /// <summary>
    /// Drivers, runtimes y piezas de las que depende el equipo. Se comparan
    /// por fragmento porque el nombre exacto lleva version.
    /// </summary>
    private static readonly string[] ProtectedFragments =
    [
        "Visual C++", "Visual Studio", ".NET", "DirectX", "Microsoft Edge",
        "NVIDIA", "GeForce", "AMD Software", "AMD Chipset", "Radeon Software",
        "Intel(R)", "Intel Graphics", "Intel Chipset", "Realtek", "Conexant",
        "Driver Package", "Controlador", "Chipset", "Audio Driver",
        "Microsoft Update Health", "Windows Software Development Kit",
        "Microsoft Office", "Microsoft 365", "OneDrive",
        "Java 8 Update", "Microsoft Visual", "Security Update", "Hotfix",
        "Firmware", "BIOS Update", "Management Engine", "Thunderbolt",
        "Bluetooth", "Wireless", "Wi-Fi", "Ethernet", "LAN Driver",
        "Touchpad", "Synaptics", "ELAN", "Precision Touchpad"
    ];

    /// <summary>
    /// Lo que suele venir de fabrica y casi nadie usa. La clave es el
    /// fragmento del nombre; el valor, que es y por que sobra.
    /// </summary>
    private static readonly Dictionary<string, string> KnownBloat = new(StringComparer.OrdinalIgnoreCase)
    {
        // Antivirus de prueba: ademas de ocupar, entran en conflicto con Defender.
        ["McAfee"] = "Antivirus de prueba preinstalado. Al caducar deja avisos constantes y frena a Defender.",
        ["Norton"] = "Antivirus de prueba preinstalado. Mismo problema que el anterior.",
        ["Avast"] = "Antivirus de terceros preinstalado.",
        ["AVG"] = "Antivirus de terceros preinstalado.",
        ["Webroot"] = "Antivirus de prueba preinstalado.",

        // Utilidades del fabricante
        ["HP Support Assistant"] = "Utilidad de HP que arranca sola y avisa de actualizaciones.",
        ["HP JumpStart"] = "Bienvenida de HP, no hace nada útil después del primer arranque.",
        ["HP Documentation"] = "Manuales de HP en local.",
        ["HP Audio Switch"] = "Selector de audio de HP, redundante con Windows.",
        ["Lenovo Vantage"] = "Panel de Lenovo. Útil para actualizar drivers, pesado si no lo usas.",
        ["Lenovo Now"] = "Publicidad y ofertas de Lenovo.",
        ["Lenovo Migration"] = "Asistente de migración, solo sirve al estrenar el equipo.",
        ["Dell Customer Connect"] = "Encuestas y ofertas de Dell.",
        ["Dell Digital Delivery"] = "Entrega de software comprado con el equipo. Ya cumplió.",
        ["Dell SupportAssist"] = "Diagnóstico de Dell que arranca solo.",
        ["ASUS GiftBox"] = "Catálogo de ofertas de ASUS.",
        ["ASUS Product Register"] = "Registro del producto ASUS.",
        ["Acer Jumpstart"] = "Bienvenida de Acer.",
        ["Acer Product Registration"] = "Registro del producto Acer.",
        ["MyASUS"] = "Panel de ASUS. Útil para drivers, pesado si no lo usas.",

        // Ofertas y juegos de prueba
        ["WildTangent"] = "Juegos de prueba con publicidad.",
        ["Booking.com"] = "Acceso directo a Booking preinstalado.",
        ["ExpressVPN"] = "VPN de prueba preinstalada.",
        ["Keeper"] = "Gestor de contraseñas de prueba preinstalado.",
        ["CyberLink"] = "Suite multimedia de prueba, suele venir capada.",
        ["Nero"] = "Suite de grabación de prueba.",
        ["PowerDirector"] = "Editor de video de prueba.",
        ["Candy Crush"] = "Juego preinstalado.",
        ["Dropbox Promotion"] = "Promoción de Dropbox preinstalada.",
        ["Amazon Assistant"] = "Extensión de compras de Amazon preinstalada."
    };

    public static ProgramVerdict Classify(InstalledProgram program)
    {
        var haystack = $"{program.Name} {program.Publisher}";

        if (ProtectedFragments.Any(f => haystack.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            return ProgramVerdict.Protected;
        }

        return KnownBloat.Keys.Any(f => haystack.Contains(f, StringComparison.OrdinalIgnoreCase))
            ? ProgramVerdict.Bloatware
            : ProgramVerdict.Unknown;
    }

    public static string DescribeReason(InstalledProgram program)
    {
        var haystack = $"{program.Name} {program.Publisher}";

        return KnownBloat
            .Where(entry => haystack.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Value)
            .FirstOrDefault() ?? string.Empty;
    }
}
