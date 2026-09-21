using Microsoft.Win32;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Performance;

/// <summary>
/// Un ajuste de Windows que afecta a los juegos, con el valor que deberia
/// tener y por que. Recommended dice si merece la pena EN ESTE equipo: no es
/// lo mismo recomendar bajar efectos visuales en un portatil con grafica
/// integrada que en una torre con una 4080.
/// </summary>
public sealed record GamingTweak(
    string Id,
    string Title,
    string Explanation,
    RegistryLocation Location,
    int DesiredValue)
{
    public bool NeedsRestart { get; init; }

    public bool Recommended { get; init; } = true;

    /// <summary>Matiz honesto: por que se recomienda, o por que aqui no.</summary>
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// El catalogo completo de ajustes que toca el modulo de rendimiento.
/// Esta a proposito en un solo sitio y es corto: cada entrada tiene que
/// justificar que hace algo medible. Los ajustes de foro que "suben FPS"
/// sin evidencia no entran aqui.
/// </summary>
public static class GamingTweaks
{
    public static IReadOnlyList<GamingTweak> For(HardwareProfile profile)
    {
        var modest = profile.IsModestHardware;
        var integrated = profile.PrimaryGpu?.IsLikelyIntegrated ?? false;

        return
        [
            new GamingTweak(
                "game-mode",
                "Modo Juego",
                "Windows da prioridad al juego en primer plano y aparta tareas de fondo.",
                new RegistryLocation(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\GameBar",
                    "AutoGameModeEnabled"),
                DesiredValue: 1)
            {
                Note = "Mejora sobre todo la estabilidad de fotogramas, no el máximo."
            },

            new GamingTweak(
                "game-dvr",
                "Grabación en segundo plano",
                "La captura permanente de los últimos minutos de juego consume CPU y GPU todo el rato.",
                new RegistryLocation(
                    RegistryHive.CurrentUser,
                    @"System\GameConfigStore",
                    "GameDVR_Enabled"),
                DesiredValue: 0)
            {
                Note = "Es de lo poco que da una mejora medible. Seguirás pudiendo grabar a mano."
            },

            new GamingTweak(
                "game-dvr-policy",
                "Grabación en segundo plano (directiva del sistema)",
                "El mismo ajuste a nivel de equipo, para que no vuelva a activarse por perfil.",
                new RegistryLocation(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\GameDVR",
                    "AllowGameDVR"),
                DesiredValue: 0)
            {
                Note = "Necesita permisos de administrador."
            },

            new GamingTweak(
                "hags",
                "Planificación acelerada por GPU",
                "Deja que la GPU gestione su propia memoria de trabajo en vez de la CPU.",
                new RegistryLocation(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    "HwSchMode"),
                DesiredValue: 2)
            {
                NeedsRestart = true,
                // No va marcado por defecto a proposito: los resultados
                // reales van de "un poco mejor" a "un poco peor" segun el
                // juego y el driver. Quien lo active deberia medirlo.
                Recommended = false,
                Note = "Requiere reiniciar y los resultados varían según juego y driver: "
                       + "puede subir o bajar. Pruébalo y compara; si no notas nada, déjalo como estaba."
            },

            new GamingTweak(
                "visual-effects",
                "Efectos visuales de Windows",
                "Animaciones y sombras de la interfaz ajustadas a rendimiento en vez de a apariencia.",
                new RegistryLocation(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting"),
                DesiredValue: 2)
            {
                Recommended = modest,
                Note = modest
                    ? "En este equipo si se nota: el escritorio irá más suelto y liberas algo de GPU."
                    : "Tu equipo va sobrado para mover la interfaz. Ganarías fotogramas que no "
                      + "vas a notar a cambio de un Windows más feo."
            },

            new GamingTweak(
                "transparency",
                "Transparencia de la interfaz",
                "El efecto de cristal del menú Inicio y las ventanas se dibuja con la GPU.",
                new RegistryLocation(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency"),
                DesiredValue: 0)
            {
                Recommended = integrated,
                Note = integrated
                    ? "Con gráfica integrada, la GPU que dibuja el escritorio es la misma que "
                      + "mueve el juego: quitarlo le devuelve algo de aire."
                    : "Con gráfica dedicada el coste es despreciable. Es cuestión de gusto."
            }
        ];
    }
}
