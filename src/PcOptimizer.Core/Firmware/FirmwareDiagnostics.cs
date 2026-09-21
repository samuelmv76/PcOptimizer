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
        "Ajustes de firmware que afectan al rendimiento y a la compatibilidad. Esta aplicacion no escribe en la BIOS: te dice que cambiar y donde.";

    public override Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // El perfil se captura una sola vez: cada lectura de WMI cuesta.
        var profile = _profileSource();

        var findings = new List<Finding>
        {
            CheckFirmwareType(),
            CheckSecureBoot(),
            CheckVirtualization(profile),
            CheckTpm(),
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
                Recommendation = "El arranque Legacy impide Secure Boot y, en Windows 11, no esta soportado. "
                                 + "Convertir el disco a GPT con mbr2gpt y cambiar la BIOS a UEFI es posible, "
                                 + "pero haz una copia de seguridad antes: si algo falla el equipo no arranca."
            },

            _ => new FirmwareFinding("Modo de arranque", "no se ha podido determinar")
            {
                Severity = FindingSeverity.Info
            }
        };
    }

    private static Finding CheckSecureBoot()
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
                Recommendation = "Activalo en la BIOS (Boot o Security > Secure Boot). Es requisito de Windows 11 "
                                 + "y algunos anticheats lo exigen. Si arrancas Linux en el mismo equipo, "
                                 + "comprueba antes que tu distribucion lo soporta."
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
            return new FirmwareFinding("Virtualizacion", "desactivada en la BIOS")
            {
                Severity = FindingSeverity.Suggestion,
                Recommendation = "Busca Intel VT-x / AMD-V (o SVM Mode) en la BIOS y activala. "
                                 + "La necesitan WSL, las maquinas virtuales, los emuladores de Android "
                                 + "y la proteccion de seguridad basada en virtualizacion."
            };
        }

        var state = hypervisorRunning is true
            ? "activada, con hipervisor en marcha"
            : "activada";

        return new FirmwareFinding("Virtualizacion", state) { Severity = FindingSeverity.Info };
    }

    private static Finding CheckTpm()
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
                    return new FirmwareFinding("TPM", $"activo, version {version}")
                    {
                        Severity = FindingSeverity.Info
                    };
                }

                return new FirmwareFinding("TPM", $"presente pero inactivo (version {version})")
                {
                    Severity = FindingSeverity.Suggestion,
                    Recommendation = "Activalo en la BIOS. Suele aparecer como PTT (Intel) o fTPM (AMD). "
                                     + "Windows 11 lo requiere y BitLocker lo usa."
                };
            }
        }

        return new FirmwareFinding("TPM", "no detectado")
        {
            Severity = FindingSeverity.Suggestion,
            Recommendation = "Si la placa lo soporta, aparecera en la BIOS como PTT (Intel) o fTPM (AMD). "
                             + "Sin TPM 2.0 este equipo no cumple los requisitos de Windows 11."
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
                Recommendation = "Esta desactivada, asi que no te esta costando rendimiento. "
                                 + "Activarla en Seguridad de Windows endurece el sistema frente a drivers maliciosos."
            };
        }

        return new FirmwareFinding("Integridad de memoria (HVCI)", "activada")
        {
            Severity = FindingSeverity.Info,
            Recommendation = "Protege frente a drivers maliciosos, pero al apoyarse en virtualizacion "
                             + "puede costar un pequeno porcentaje de rendimiento en juegos. "
                             + "Es una decision tuya: seguridad frente a unos pocos fotogramas."
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
            Recommendation = "El perfil XMP (Intel) o EXPO/DOCP (AMD) no esta activado en la BIOS. "
                             + "Activarlo es un solo ajuste y recupera la velocidad por la que pagaste; "
                             + "en juegos limitados por CPU la diferencia se nota. "
                             + "Si el equipo se vuelve inestable, vuelve al perfil automatico."
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
