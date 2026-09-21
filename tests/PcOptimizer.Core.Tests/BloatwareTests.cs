using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Bloatware;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class BloatwareCatalogTests
{
    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("Microsoft.GamingServices")]
    public void Las_piezas_del_sistema_estan_protegidas(string package)
        => Assert.Equal(AppClassification.Protected, BloatwareCatalog.Classify(package));

    [Theory]
    [InlineData("Microsoft.BingWeather")]
    [InlineData("Clipchamp.Clipchamp")]
    [InlineData("king.com.CandyCrushSaga")]
    [InlineData("SpotifyAB.SpotifyMusic")]
    public void Las_preinstaladas_prescindibles_se_reconocen(string package)
        => Assert.Equal(AppClassification.Bloatware, BloatwareCatalog.Classify(package));

    [Fact]
    public void Una_app_desconocida_no_se_clasifica_como_basura()
        => Assert.Equal(AppClassification.Other, BloatwareCatalog.Classify("Contoso.HerramientaInterna"));

    [Fact]
    public void Las_apps_delicadas_llevan_advertencia()
    {
        var caveat = BloatwareCatalog.DescribeCaveat("Microsoft.XboxGamingOverlay");

        Assert.False(string.IsNullOrWhiteSpace(caveat));
    }
}

public sealed class BloatwareModuleTests
{
    private static AppxPackageInfo Package(string name, bool? nonRemovable = false) =>
        new()
        {
            Name = name,
            PackageFullName = $"{name}_1.0.0.0_x64__8wekyb3d8bbwe",
            Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond",
            Version = "1.0.0.0",
            NonRemovable = nonRemovable
        };

    [Fact]
    public void Los_paquetes_protegidos_no_se_ofrecen()
    {
        var findings = BloatwareModule.Classify([
            Package("Microsoft.WindowsStore"),
            Package("Microsoft.BingWeather")
        ]);

        var single = Assert.Single(findings);
        Assert.Equal("Bing Weather", single.Title);
    }

    [Fact]
    public void Los_paquetes_marcados_como_no_desinstalables_se_omiten()
    {
        var findings = BloatwareModule.Classify([Package("Contoso.App", nonRemovable: true)]);

        Assert.Empty(findings);
    }

    [Fact]
    public void Lo_prescindible_aparece_antes_que_lo_desconocido()
    {
        var findings = BloatwareModule.Classify([
            Package("Contoso.HerramientaInterna"),
            Package("Microsoft.BingWeather")
        ]);

        Assert.Equal(AppClassification.Bloatware, ((BloatwareFinding)findings[0]).Classification);
        Assert.Equal(AppClassification.Other, ((BloatwareFinding)findings[1]).Classification);
    }

    [Fact]
    public void Nada_viene_marcado_por_defecto()
    {
        var findings = BloatwareModule.Classify([Package("Microsoft.BingWeather")]);

        Assert.All(findings, f => Assert.False(f.SelectedByDefault));
    }

    [Fact]
    public void Se_interpreta_la_salida_json_de_PowerShell()
    {
        const string json = """
            [{"Name":"Microsoft.BingWeather","PackageFullName":"Microsoft.BingWeather_1.0_x64__8wekyb3d8bbwe","Publisher":"CN=Microsoft Corporation","Version":"1.0","NonRemovable":false}]
            """;

        var packages = BloatwareModule.ParsePackages(json);

        var single = Assert.Single(packages);
        Assert.Equal("Microsoft.BingWeather", single.Name);
        Assert.False(single.NonRemovable);
    }

    [Fact]
    public void Una_salida_vacia_no_es_un_error()
        => Assert.Empty(BloatwareModule.ParsePackages("   "));

    [Fact]
    public void Una_salida_ilegible_se_reporta_como_tal()
        => Assert.Throws<InvalidOperationException>(() => BloatwareModule.ParsePackages("no es json"));

    [Fact]
    public void El_nombre_del_paquete_no_se_puede_escapar_del_literal()
    {
        // Un nombre con comilla simple no debe poder cerrar la cadena y
        // colar un comando detras.
        var script = BloatwareModule.BuildRemovalScript("Malo'; Remove-Item C:\\ -Recurse; '");

        // La comilla se duplica, asi que todo el texto sigue dentro del literal.
        Assert.Contains(@"$full = 'Malo''; Remove-Item C:\ -Recurse; '''", script);
    }
}

public sealed class BloatwareFindingTests
{
    [Theory]
    [InlineData("Microsoft.WindowsMaps", "Windows Maps")]
    [InlineData("Microsoft.BingNews", "Bing News")]
    [InlineData("Clipchamp.Clipchamp", "Clipchamp")]
    [InlineData("Microsoft.XboxGamingOverlay", "Xbox Gaming Overlay")]
    public void El_nombre_de_paquete_se_convierte_en_algo_legible(string package, string expected)
        => Assert.Equal(expected, BloatwareFinding.FriendlyName(package));

    [Fact]
    public void El_publicador_se_extrae_del_nombre_distinguido()
    {
        var finding = new BloatwareFinding(
            new AppxPackageInfo
            {
                Name = "Contoso.App",
                PackageFullName = "Contoso.App_1.0",
                Publisher = "CN=Contoso Ltd, O=Contoso, L=Madrid"
            },
            AppClassification.Other,
            reason: string.Empty,
            caveat: null);

        Assert.Equal("Contoso Ltd", finding.Details);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }
}
