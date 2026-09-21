using Microsoft.Win32;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Performance;
using PcOptimizer.Core.Platform;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class GamingTweaksTests
{
    private static HardwareProfile Profile(
        string mediaType = "SSD",
        long memoryBytes = 16L * 1024 * 1024 * 1024,
        string gpuName = "NVIDIA GeForce RTX 4070") =>
        new()
        {
            Disks = [new DiskInfo("Disco", 500_000_000_000, mediaType, "NVMe", IsSystemDisk: true)],
            MemoryModules =
            [
                new MemoryModuleInfo("A1", memoryBytes / 2, 3200, 3200, "Test"),
                new MemoryModuleInfo("A2", memoryBytes / 2, 3200, 3200, "Test")
            ],
            Gpus = [new GpuInfo(gpuName, 12L * 1024 * 1024 * 1024, "551.23", DateTime.Now)]
        };

    private static GamingTweak Find(HardwareProfile profile, string id)
        => GamingTweaks.For(profile).Single(t => t.Id == id);

    [Fact]
    public void El_modo_juego_y_la_grabacion_se_recomiendan_siempre()
    {
        var tweaks = GamingTweaks.For(Profile());

        Assert.True(tweaks.Single(t => t.Id == "game-mode").Recommended);
        Assert.True(tweaks.Single(t => t.Id == "game-dvr").Recommended);
    }

    [Fact]
    public void La_grabacion_en_segundo_plano_se_apaga_no_se_enciende()
        => Assert.Equal(0, Find(Profile(), "game-dvr").DesiredValue);

    [Fact]
    public void En_un_equipo_potente_no_se_recomienda_bajar_los_efectos_visuales()
        => Assert.False(Find(Profile(), "visual-effects").Recommended);

    [Theory]
    [InlineData("HDD", 16L * 1024 * 1024 * 1024, "NVIDIA GeForce RTX 4070")]
    [InlineData("SSD", 4L * 1024 * 1024 * 1024, "NVIDIA GeForce RTX 4070")]
    [InlineData("SSD", 16L * 1024 * 1024 * 1024, "Intel(R) UHD Graphics 620")]
    public void En_un_equipo_modesto_si(string media, long memory, string gpu)
        => Assert.True(Find(Profile(media, memory, gpu), "visual-effects").Recommended);

    [Fact]
    public void La_transparencia_solo_se_recomienda_con_grafica_integrada()
    {
        Assert.False(Find(Profile(), "transparency").Recommended);
        Assert.True(Find(Profile(gpuName: "Intel(R) UHD Graphics 620"), "transparency").Recommended);
    }

    [Fact]
    public void La_planificacion_por_GPU_no_va_marcada_por_defecto()
    {
        // Los resultados reales varian segun juego y driver: no se puede
        // recomendar a ciegas, y ademas exige reiniciar.
        var hags = Find(Profile(), "hags");

        Assert.False(hags.Recommended);
        Assert.True(hags.NeedsRestart);
    }

    [Fact]
    public void Cada_ajuste_explica_que_hace_y_tiene_un_matiz()
    {
        foreach (var tweak in GamingTweaks.For(Profile()))
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.Explanation), tweak.Id);
            Assert.False(string.IsNullOrWhiteSpace(tweak.Note), tweak.Id);
        }
    }

    [Fact]
    public void Los_identificadores_no_se_repiten()
    {
        var ids = GamingTweaks.For(Profile()).Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}

public sealed class HardwareProfileTests
{
    [Fact]
    public void Un_solo_modulo_de_ocho_gigas_no_es_equipo_modesto_por_la_ram()
    {
        var profile = new HardwareProfile
        {
            MemoryModules = [new MemoryModuleInfo("A1", 8L * 1024 * 1024 * 1024, 3200, 3200, "Test")],
            Disks = [new DiskInfo("Disco", 500_000_000_000, "SSD", "NVMe", IsSystemDisk: true)]
        };

        Assert.False(profile.IsModestHardware);
    }

    [Fact]
    public void Sin_datos_de_memoria_no_se_asume_que_sea_modesto()
        => Assert.False(new HardwareProfile().IsModestHardware);

    [Fact]
    public void La_grafica_dedicada_gana_a_la_integrada_como_principal()
    {
        var profile = new HardwareProfile
        {
            Gpus =
            [
                new GpuInfo("Intel(R) UHD Graphics 770", 0, "31.0", null),
                new GpuInfo("NVIDIA GeForce RTX 4070", 12_000_000_000, "551.23", null)
            ]
        };

        Assert.Equal("NVIDIA GeForce RTX 4070", profile.PrimaryGpu?.Name);
    }
}

public sealed class RegistryLocationTests
{
    [Fact]
    public void Ida_y_vuelta()
    {
        var location = new RegistryLocation(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\GameBar",
            "AutoGameModeEnabled");

        var parsed = RegistryLocation.Parse(location.ToString());

        Assert.Equal(location, parsed);
    }

    [Theory]
    [InlineData("sin separador")]
    [InlineData("HiveInventado\\Ruta!Valor")]
    [InlineData("CurrentUser\\Ruta!")]
    [InlineData("!Valor")]
    public void Una_ruta_mal_formada_devuelve_null(string text)
        => Assert.Null(RegistryLocation.Parse(text));

    [Fact]
    public void Una_ruta_con_exclamacion_en_la_clave_usa_la_ultima()
    {
        var parsed = RegistryLocation.Parse(@"LocalMachine\SOFTWARE\Cosa!rara!Valor");

        Assert.NotNull(parsed);
        Assert.Equal(@"SOFTWARE\Cosa!rara", parsed.SubKey);
        Assert.Equal("Valor", parsed.Name);
    }
}
