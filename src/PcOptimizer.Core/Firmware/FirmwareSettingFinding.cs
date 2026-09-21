using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Firmware;

public enum FirmwareAction
{
    None,

    /// <summary>Pedir al firmware que abra su configuracion en el proximo arranque.</summary>
    OpenSetupOnRestart,

    /// <summary>Arrancar una sola vez desde otra entrada (BootNext).</summary>
    BootOnce,

    /// <summary>Activar o desactivar la integridad de memoria de Windows.</summary>
    SetMemoryIntegrity
}

public sealed class FirmwareSettingFinding : Finding
{
    public FirmwareSettingFinding(string id, string title, FirmwareAction action)
        : base($"firmware-setting:{id}", title)
    {
        Action = action;
        SelectedByDefault = false;
    }

    public FirmwareAction Action { get; }

    /// <summary>Entrada de arranque, para BootOnce.</summary>
    public ushort BootNumber { get; init; }

    /// <summary>Valor deseado, para SetMemoryIntegrity.</summary>
    public bool Enable { get; init; }
}
