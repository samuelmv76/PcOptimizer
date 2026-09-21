using PcOptimizer.Core.Platform;
using PcOptimizer.Core.Programs;
using PcOptimizer.Core.Services;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Cosa\\unins.exe\" /S", "C:\\Program Files\\Cosa\\unins.exe", "/S")]
    [InlineData("\"C:\\Cosa\\unins.exe\"", "C:\\Cosa\\unins.exe", "")]
    [InlineData("MsiExec.exe /I{1234-5678}", "MsiExec.exe", "/I{1234-5678}")]
    [InlineData("C:\\cosa.exe", "C:\\cosa.exe", "")]
    [InlineData("", "", "")]
    public void Se_separa_el_ejecutable_de_sus_argumentos(string input, string executable, string arguments)
    {
        var (actualExecutable, actualArguments) = CommandLine.Split(input);

        Assert.Equal(executable, actualExecutable);
        Assert.Equal(arguments, actualArguments);
    }

    [Theory]
    [InlineData("MsiExec.exe", true)]
    [InlineData("C:\\Windows\\System32\\msiexec.exe", true)]
    [InlineData("unins000.exe", false)]
    public void Se_reconoce_windows_installer(string executable, bool expected)
        => Assert.Equal(expected, CommandLine.IsMsiExec(executable));
}

public sealed class ProgramCatalogTests
{
    private static InstalledProgram Program(string name, string publisher = "", string uninstall = "C:\\u.exe") =>
        new()
        {
            Name = name,
            Publisher = publisher,
            UninstallCommand = uninstall,
            RegistryKey = $"HKLM\\...\\{name}"
        };

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)")]
    [InlineData("NVIDIA Graphics Driver 551.23")]
    [InlineData("Realtek High Definition Audio Driver")]
    [InlineData("Intel(R) Chipset Device Software")]
    public void Los_drivers_y_runtimes_estan_protegidos(string name)
        => Assert.Equal(ProgramVerdict.Protected, ProgramCatalog.Classify(Program(name)));

    [Theory]
    [InlineData("McAfee LiveSafe")]
    [InlineData("HP Support Assistant")]
    [InlineData("Lenovo Now")]
    [InlineData("WildTangent Games")]
    public void El_bloatware_de_fabrica_se_reconoce(string name)
        => Assert.Equal(ProgramVerdict.Bloatware, ProgramCatalog.Classify(Program(name)));

    [Theory]
    [InlineData("BioShock Infinite")]
    [InlineData("Amazon Games")]
    [InlineData("Amazon Corretto 17")]
    public void Los_fragmentos_no_atrapan_programas_legitimos(string name)
    {
        // Regresion: "BIOS" ocultaba BioShock y "Amazon" marcaba como basura
        // un JDK. Las listas por fragmento tienen que ser especificas.
        Assert.Equal(ProgramVerdict.Unknown, ProgramCatalog.Classify(Program(name)));
    }

    [Fact]
    public void Un_programa_tuyo_no_se_clasifica_como_basura()
        => Assert.Equal(ProgramVerdict.Unknown, ProgramCatalog.Classify(Program("Blender")));

    [Fact]
    public void Lo_protegido_gana_aunque_el_nombre_suene_a_fabricante()
    {
        // "HP Audio Driver" contiene "HP" pero tambien "Driver": manda lo protegido.
        Assert.Equal(ProgramVerdict.Protected, ProgramCatalog.Classify(Program("HP Audio Driver")));
    }

    [Fact]
    public void Los_protegidos_no_se_ofrecen()
    {
        var findings = InstalledProgramsModule.Classify([
            Program("NVIDIA Graphics Driver"),
            Program("McAfee LiveSafe")
        ]);

        var single = Assert.Single(findings);
        Assert.Equal("McAfee LiveSafe", single.Title);
    }

    [Fact]
    public void El_bloatware_aparece_antes_que_lo_desconocido()
    {
        var findings = InstalledProgramsModule.Classify([
            Program("Blender"),
            Program("McAfee LiveSafe")
        ]);

        Assert.Equal("McAfee LiveSafe", findings[0].Title);
        Assert.Equal("Blender", findings[1].Title);
    }

    [Fact]
    public void Nada_viene_marcado_por_defecto()
    {
        // Desinstalar no se deshace desde la aplicacion.
        var findings = InstalledProgramsModule.Classify([Program("McAfee LiveSafe")]);

        Assert.All(findings, f => Assert.False(f.SelectedByDefault));
    }

    [Fact]
    public void A_msiexec_se_le_pide_desinstalar_en_silencio()
    {
        var program = Program("Cosa", uninstall: "MsiExec.exe /I{1234-5678}");

        var (executable, arguments) = InstalledProgramsModule.BuildUninstallCommand(program);

        Assert.Equal("MsiExec.exe", executable);
        Assert.Equal("/X{1234-5678} /quiet /norestart", arguments);
    }

    [Fact]
    public void Si_el_programa_ofrece_desinstalacion_silenciosa_se_usa_la_suya()
    {
        var program = new InstalledProgram
        {
            Name = "Cosa",
            UninstallCommand = "\"C:\\Cosa\\unins.exe\"",
            QuietUninstallCommand = "\"C:\\Cosa\\unins.exe\" /VERYSILENT"
        };

        var (executable, arguments) = InstalledProgramsModule.BuildUninstallCommand(program);

        Assert.Equal("C:\\Cosa\\unins.exe", executable);
        Assert.Equal("/VERYSILENT", arguments);
        Assert.True(program.SupportsSilentUninstall);
    }

    [Fact]
    public void Un_desinstalador_clasico_se_lanza_tal_cual_y_se_avisa()
    {
        var program = Program("Cosa", uninstall: "\"C:\\Cosa\\unins.exe\"");

        Assert.False(program.SupportsSilentUninstall);

        var finding = new ProgramFinding(program, ProgramVerdict.Unknown, string.Empty);

        Assert.Contains("asistente", finding.Recommendation);
    }
}

public sealed class ServiceCatalogTests
{
    [Theory]
    [InlineData("Audiosrv")]
    [InlineData("Dhcp")]
    [InlineData("WinDefend")]
    [InlineData("wuauserv")]
    [InlineData("GamingServices")]
    public void Los_servicios_criticos_estan_protegidos(string name)
        => Assert.Equal(ServiceVerdict.Protected, ServiceCatalog.Classify(name));

    [Theory]
    [InlineData("Fax")]
    [InlineData("RetailDemo")]
    [InlineData("MapsBroker")]
    public void Los_prescindibles_se_reconocen(string name)
        => Assert.Equal(ServiceVerdict.SafeToDefer, ServiceCatalog.Classify(name));

    [Fact]
    public void Un_servicio_de_terceros_no_se_clasifica()
        => Assert.Equal(ServiceVerdict.Unknown, ServiceCatalog.Classify("ContosoAgent"));

    [Theory]
    [InlineData("Spooler")]
    [InlineData("XblAuthManager")]
    [InlineData("WbioSrvc")]
    public void Los_delicados_llevan_advertencia(string name)
    {
        Assert.True(ServiceCatalog.IsDelicate(name));
        Assert.Equal(ServiceVerdict.SafeToDefer, ServiceCatalog.Classify(name));
    }

    [Fact]
    public void Todo_prescindible_explica_que_pierdes()
    {
        foreach (var name in new[] { "Fax", "Spooler", "DiagTrack", "TermService", "lfsvc" })
        {
            Assert.False(string.IsNullOrWhiteSpace(ServiceCatalog.DescribeReason(name)), name);
        }
    }

    [Fact]
    public void Un_servicio_protegido_no_esta_tambien_en_la_lista_de_prescindibles()
    {
        // Si estuviera en las dos, el veredicto dependeria del orden de
        // comprobacion en vez de del criterio.
        foreach (var name in new[] { "Audiosrv", "Dhcp", "WinDefend", "Spooler", "Fax" })
        {
            var verdict = ServiceCatalog.Classify(name);
            var hasReason = !string.IsNullOrEmpty(ServiceCatalog.DescribeReason(name));

            Assert.Equal(verdict == ServiceVerdict.SafeToDefer, hasReason);
        }
    }

    [Fact]
    public void Nada_viene_marcado_por_defecto()
    {
        var finding = new ServiceFinding(
            "Fax", "Fax", "Auto", "Stopped", ServiceVerdict.SafeToDefer, "Faxes", delicate: false);

        Assert.False(finding.SelectedByDefault);
    }
}
