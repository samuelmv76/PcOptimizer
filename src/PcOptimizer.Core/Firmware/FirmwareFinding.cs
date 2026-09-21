using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Firmware;

/// <summary>
/// Un ajuste de firmware que la aplicacion ha leido. Nunca se escribe nada:
/// modificar la BIOS desde Windows exige un driver en modo kernel y puede
/// dejar el equipo sin arrancar. Aqui solo se informa de que cambiar.
/// </summary>
public sealed class FirmwareFinding : Finding
{
    public FirmwareFinding(string setting, string state)
        : base($"firmware:{setting}", setting)
    {
        Setting = setting;
        State = state;
        Details = state;
        SelectedByDefault = false;
    }

    public string Setting { get; }

    public string State { get; }
}
