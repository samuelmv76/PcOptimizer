using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Bloatware;
using PcOptimizer.Core.Cleaning;
using PcOptimizer.Core.Disk;
using PcOptimizer.Core.Firmware;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Startup;
using Xunit;

namespace PcOptimizer.Core.Tests;

/// <summary>
/// Reglas que todo modulo debe cumplir. Sin esto, un modulo nuevo puede
/// quedarse sin descripcion o prometer que aplica cambios cuando solo lee.
/// </summary>
public sealed class ModuleContractTests
{
    private static IReadOnlyList<IOptimizerModule> AllModules() =>
    [
        new HardwareInventory(),
        new TempFileCleaner(TempFileCleaner.DefaultTargets()),
        new DiskSpaceAnalyzer(),
        new BloatwareModule(),
        new StartupManager(),
        new FirmwareDiagnostics()
    ];

    [Fact]
    public void Todo_modulo_se_identifica_y_se_explica()
    {
        foreach (var module in AllModules())
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Id), $"{module.GetType().Name}: Id vacio");
            Assert.False(string.IsNullOrWhiteSpace(module.DisplayName), $"{module.GetType().Name}: nombre vacio");
            Assert.False(string.IsNullOrWhiteSpace(module.Description), $"{module.GetType().Name}: sin descripcion");
        }
    }

    [Fact]
    public void Los_identificadores_no_se_repiten()
    {
        var ids = AllModules().Select(m => m.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Un_modulo_de_diagnostico_nunca_aplica_nada()
    {
        foreach (var module in AllModules().Where(m => m.Kind == ModuleKind.Diagnostic))
        {
            var result = await module.ApplyAsync(
                [new StubFinding()],
                new RemediationOptions { Simulate = false });

            Assert.Equal(0, result.Applied);
            Assert.NotEmpty(result.Errors);
        }
    }

    [Fact]
    public void Hay_al_menos_un_modulo_de_cada_tipo()
    {
        var modules = AllModules();

        Assert.Contains(modules, m => m.Kind == ModuleKind.Actionable);
        Assert.Contains(modules, m => m.Kind == ModuleKind.Diagnostic);
    }

    [Fact]
    public void Simular_es_el_valor_por_defecto()
        => Assert.True(new RemediationOptions().Simulate);

    private sealed class StubFinding() : Finding("stub", "Stub");
}
