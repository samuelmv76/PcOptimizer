using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Firmware;

/// <summary>
/// Comprueba los ajustes de firmware que mas afectan al rendimiento y a la
/// compatibilidad, y explica donde cambiarlos. Este modulo no escribe nada.
/// </summary>
public sealed class FirmwareDiagnostics : DiagnosticModule
{
    private const string SecureBootStateKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";

    private const string MemoryIntegrityKey =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    private readonly Func<HardwareProfile> _profileSource;

    public FirmwareDiagnostics(Func<HardwareProfile>? profileSource = null)
        => _profileSource = profileSource ?? (() => HardwareInventory.Capture());

    public override string Id => "firmware.diagnostics";

    public override string DisplayName => "Firmware y BIOS";

    public override string Description =>
<<<<<<< HEAD
        "Ajustes de firmware que afectan al rendimiento y a la compatibilidad, con el menú exacto de tu placa donde se cambian. Para entrar en la BIOS sin pulsar teclas, usa Arranque y BIOS.";
=======
        "Ajustes de firmware que afectan al rendimiento y a la compatibilidad. Esta aplicacion no escribe en la BIOS: te dice que cambiar y donde.";
>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0

    public override Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // El perfil se captura una sola vez: cada lectura de WMI cuesta.
        var profile = _profileSource();

        var findings = new List<Finding>
        {
            CheckFirmwareType(),
            CheckSecureBoot(profile),
            CheckVirtualization(profile),
            CheckTpm(profile),
            CheckMemoryIntegrity()
        };

        findings.AddRange(CheckMemorySpeed(profile));

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static Finding CheckFirmwareType()
    {
        var type = ReadFirmwareType();

        return type switch
        {
            FirmwareType.Uefi => new FirmwareFinding("Modo de arranque", "UEFI")
            {
                Severity = FindingSeverity.Info
            },

            FirmwareType.Bios => new FirmwareFinding("Modo de arranque", "Legacy / CSM")
            {
                Severity = FindingSeverity.Warning,
                Recommendation = "El arranque Legacy impide Secure Boot y, en Windows 11, no está soportado. "
                                 + "Convertir el disco a GPT con mbr2gpt y cambiar la BIOS a UEFI es posible, "
                                 + "pero haz una copia de seguridad antes: si algo falla el equipo no arranca."
            },

            _ => new FirmwareFinding("Modo de arranque", "no se ha podido determinar")
            {
                Severity = FindingSeverity.Info
            }
        };
    }

    /// <summary>Donde esta el ajuste en la placa de este equipo, y como llegar.</summary>
    private static string Where(HardwareProfile profile, BiosSetting setting)
        => BiosMenuGuide.Directions(profile.Motherboard, profile.Cpu?.Name ?? string.Empty, setting);

    private static Finding CheckSecureBoot(HardwareProfile profile)
    {
        var enabled = ReadDword(SecureBootStateKey, "UEFISecureBootEnabled");

        return enabled switch
        {
            1 => new FirmwareFinding("Secure Boot", "activado")
            {
                Severity = FindingSeverity.Info
            },

            0 => new FirmwareFinding("Secure Boot", "desactivado")
            {
                Severity = FindingSeverity.Suggestion,
                Recommendation = "Es requisito de Windows 11 y algunos anticheats lo exigen. Si arrancas Linux "
                                 + "en el mismo equipo, comprueba antes que tu distribución lo soporta. "
                                 + Where(profile, BiosSetting.SecureBoot)
            },

            _ => new FirmwareFinding("Secure Boot", "no disponible en este firmware")
            {
                Severity = FindingSeverity.Info
            }
        };
    }

    private static Finding CheckVirtualization(HardwareProfile profile)
    {
        var enabledInFirmware = profile.Cpu?.VirtualizationEnabledInFirmware;
        var hypervisorRunning = ReadHypervisorPresent();

        if (enabledInFirmware is false)
        {
            return new FirmwareFinding("Virtualización", "desactivada en la BIOS")
            {
                Severity = FindingSeverity.Suggestion,
                Recommendation = "La necesitan WSL, las máquinas virtuales, los emuladores de Android "
                                 + "y la protección de seguridad basada en virtualización. "
                                 + Where(profile, BiosSetting.Virtualization)
            };
        }

        var state = hypervisorRunning is true
            ? "activada, con hipervisor en marcha"
            : "activada";

        return new FirmwareFinding("Virtualización", state) { Severity = FindingSeverity.Info };
    }

    private static Finding CheckTpm(HardwareProfile profile)
    {
        foreach (var tpm in Wmi.Query(
                     "SELECT * FROM Win32_Tpm",
                     "root\\CIMV2\\Security\\MicrosoftTpm"))
        {
            using (tpm)
            {
                var enabled = tpm.GetBool("IsEnabled_InitialValue") ?? false;
                var activated = tpm.GetBool("IsActivated_InitialValue") ?? false;
                var version = tpm.GetString("SpecVersion").Split(',').FirstOrDefault()?.Trim() ?? "?";

                if (enabled && activated)
                {
                    return new FirmwareFinding("TPM", $"activo, versión {version}")
                    {
                        Severity = FindingSeverity.Info
                    };
                }

                return new FirmwareFinding("TPM", $"presente pero inactivo (versión {version})")
                {
                    Severity = FindingSeverity.Suggestion,
                    Recommendation = "Windows 11 lo requiere y BitLocker lo usa. "
                                     + Where(profile, BiosSetting.Tpm)
                };
            }
        }

        return new FirmwareFinding("TPM", "no detectado")
        {
            Severity = FindingSeverity.Suggestion,
            Recommendation = "Sin TPM 2.0 este equipo no cumple los requisitos de Windows 11. "
                             + Where(profile, BiosSetting.Tpm)
        };
    }

    private static Finding CheckMemoryIntegrity()
    {
        var enabled = ReadDword(MemoryIntegrityKey, "Enabled");

        if (enabled != 1)
        {
            return new FirmwareFinding("Integridad de memoria (HVCI)", "desactivada")
            {
                Severity = FindingSeverity.Info,
                Recommendation = "Está desactivada, así que no te está costando rendimiento. "
                                 + "Activarla en Seguridad de Windows endurece el sistema frente a drivers maliciosos."
            };
        }

        return new FirmwareFinding("Integridad de memoria (HVCI)", "activada")
        {
            Severity = FindingSeverity.Info,
            Recommendation = "Protege frente a drivers maliciosos, pero al apoyarse en virtualización "
                             + "puede costar un pequeño porcentaje de rendimiento en juegos. "
                             + "Es una decisión tuya: seguridad frente a unos pocos fotogramas."
        };
    }

    private static IEnumerable<Finding> CheckMemorySpeed(HardwareProfile profile)
    {
        var slow = profile.MemoryModules.Where(m => m.RunningBelowRatedSpeed).ToList();

        if (slow.Count == 0)
        {
            if (profile.MemoryModules.Count > 0)
            {
                yield return new FirmwareFinding(
                    "Perfil de memoria (XMP / EXPO)",
                    "la RAM funciona a su velocidad nominal")
                {
                    Severity = FindingSeverity.Info
                };
            }

            yield break;
        }

        var nominal = slow.Max(m => m.NominalSpeedMhz);
        var actual = slow.Min(m => m.ConfiguredSpeedMhz);

        yield return new FirmwareFinding(
            "Perfil de memoria (XMP / EXPO)",
            $"la RAM va a {actual} MHz pudiendo ir a {nominal} MHz")
        {
            Severity = FindingSeverity.Warning,
            Recommendation = "El perfil XMP (Intel) o EXPO/DOCP (AMD) no está activado en la BIOS. "
                             + "Activarlo es un solo ajuste y recupera la velocidad por la que pagaste; "
                             + "en juegos limitados por CPU la diferencia se nota. "
                             + "Si el equipo se vuelve inestable, vuelve al perfil automático. "
                             + Where(profile, BiosSetting.MemoryProfile)
        };
    }

    private static int? ReadDword(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as int?;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static bool? ReadHypervisorPresent()
    {
        foreach (var system in Wmi.Query("SELECT HypervisorPresent FROM Win32_ComputerSystem"))
        {
            using (system)
            {
                return system.GetBool("HypervisorPresent");
            }
        }

        return null;
    }

    private static FirmwareType ReadFirmwareType()
    {
        try
        {
            return GetFirmwareType(out var type) ? (FirmwareType)type : FirmwareType.Unknown;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return FirmwareType.Unknown;
        }
    }

    private enum FirmwareType : uint
    {
        Unknown = 0,
        Bios = 1,
        Uefi = 2
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);
}
