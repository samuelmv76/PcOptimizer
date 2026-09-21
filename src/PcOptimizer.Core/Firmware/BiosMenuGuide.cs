namespace PcOptimizer.Core.Firmware;

public enum BoardVendor
{
    Unknown,
    Asus,
    Msi,
    Gigabyte,
    AsRock
}

public enum BiosSetting
{
    MemoryProfile,
    Virtualization,
    SecureBoot,
    Tpm
}

/// <summary>
/// En que menu de la BIOS suele estar cada ajuste segun el fabricante de la
/// placa. Es lo que un optimizador puede hacer de verdad en una placa de PC
/// montado: llevarte directo al sitio, no cambiarlo a ciegas desde Windows.
/// Las rutas cambian entre versiones de BIOS, asi que se dicen como "suele".
/// </summary>
public static class BiosMenuGuide
{
    public static BoardVendor Detect(string motherboard)
    {
        var text = motherboard.ToUpperInvariant();

        if (text.Contains("ASROCK"))
        {
            return BoardVendor.AsRock;
        }

        if (text.Contains("ASUS"))
        {
            return BoardVendor.Asus;
        }

        if (text.Contains("MICRO-STAR") || text.Contains("MSI"))
        {
            return BoardVendor.Msi;
        }

        return text.Contains("GIGABYTE") ? BoardVendor.Gigabyte : BoardVendor.Unknown;
    }

    public static string VendorName(BoardVendor vendor) => vendor switch
    {
        BoardVendor.Asus => "ASUS",
        BoardVendor.Msi => "MSI",
        BoardVendor.Gigabyte => "Gigabyte",
        BoardVendor.AsRock => "ASRock",
        _ => "tu placa"
    };

    /// <summary>Ruta del menu, o null si no se conoce para ese fabricante.</summary>
    public static string? PathFor(BoardVendor vendor, BiosSetting setting, bool amd) => (vendor, setting) switch
    {
        (BoardVendor.Asus, BiosSetting.MemoryProfile) => amd
            ? "Ai Tweaker > Ai Overclock Tuner > EXPO (o DOCP)"
            : "Ai Tweaker > Ai Overclock Tuner > XMP I",
        (BoardVendor.Asus, BiosSetting.Virtualization) => amd
            ? "Advanced > CPU Configuration > SVM Mode"
            : "Advanced > CPU Configuration > Intel Virtualization Technology",
        (BoardVendor.Asus, BiosSetting.SecureBoot) => "Boot > Secure Boot > OS Type: Windows UEFI mode",
        (BoardVendor.Asus, BiosSetting.Tpm) => amd
            ? "Advanced > AMD fTPM configuration > Firmware TPM"
            : "Advanced > PCH-FW Configuration > PTT",

        (BoardVendor.Msi, BiosSetting.MemoryProfile) => amd
            ? "OC > A-XMP / EXPO"
            : "OC > Extreme Memory Profile (XMP)",
        (BoardVendor.Msi, BiosSetting.Virtualization) => amd
            ? "OC > CPU Features > SVM Mode"
            : "OC > CPU Features > Intel Virtualization Tech",
        (BoardVendor.Msi, BiosSetting.SecureBoot) => "Settings > Security > Secure Boot",
        (BoardVendor.Msi, BiosSetting.Tpm) => "Settings > Security > Trusted Computing > Security Device Support",

        (BoardVendor.Gigabyte, BiosSetting.MemoryProfile) => "Tweaker > Extreme Memory Profile (X.M.P.) / EXPO",
        (BoardVendor.Gigabyte, BiosSetting.Virtualization) => amd
            ? "Tweaker > Advanced CPU Settings > SVM Mode"
            : "Tweaker > Advanced CPU Settings > Intel Virtualization Technology",
        (BoardVendor.Gigabyte, BiosSetting.SecureBoot) => "Boot > Secure Boot",
        (BoardVendor.Gigabyte, BiosSetting.Tpm) => amd
            ? "Settings > Miscellaneous > AMD CPU fTPM"
            : "Settings > Miscellaneous > Intel Platform Trust Technology (PTT)",

        (BoardVendor.AsRock, BiosSetting.MemoryProfile) => "OC Tweaker > DRAM Configuration > Load XMP Setting / EXPO",
        (BoardVendor.AsRock, BiosSetting.Virtualization) => amd
            ? "Advanced > CPU Configuration > SVM Mode"
            : "Advanced > CPU Configuration > Intel Virtualization Technology",
        (BoardVendor.AsRock, BiosSetting.SecureBoot) => "Security > Secure Boot",
        (BoardVendor.AsRock, BiosSetting.Tpm) => amd
            ? "Advanced > CPU Configuration > AMD fTPM switch"
            : "Security > Intel Platform Trust Technology",

        _ => null
    };

    /// <summary>
    /// Frase lista para la recomendacion: donde esta y como llegar sin
    /// pulsar teclas al arrancar.
    /// </summary>
    public static string Directions(string motherboard, string cpuName, BiosSetting setting)
    {
        var vendor = Detect(motherboard);
        var amd = cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase);
        var path = PathFor(vendor, setting, amd);

        var where = path is null
            ? "Busca el ajuste en la BIOS de tu placa."
            : $"En {VendorName(vendor)} suele estar en: {path} (puede cambiar según la versión de la BIOS).";

        return $"{where} Para entrar sin pulsar teclas al arrancar, usa \"Abrir la BIOS al reiniciar\" en el apartado Arranque y BIOS.";
    }
}
