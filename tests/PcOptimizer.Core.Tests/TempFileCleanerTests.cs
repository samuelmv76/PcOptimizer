using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Cleaning;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class TempFileCleanerTests : IDisposable
{
    private readonly string _root;

    public TempFileCleanerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PcOptimizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateFile(string name, TimeSpan age, int sizeInBytes = 1024)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[sizeInBytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private TempFileCleaner CreateCleaner(TimeSpan minimumAge) =>
        new([new CleaningTarget("Test", _root) { MinimumAge = minimumAge }]);

    [Fact]
    public async Task Scan_ignora_ficheros_mas_nuevos_que_la_antiguedad_minima()
    {
        CreateFile("reciente.tmp", TimeSpan.FromMinutes(5));
        CreateFile("antiguo.tmp", TimeSpan.FromDays(3));

        var findings = await CreateCleaner(TimeSpan.FromHours(24)).ScanAsync();

        var finding = Assert.Single(findings);
        Assert.Equal("antiguo.tmp", finding.Title);
    }

    [Fact]
    public async Task Scan_calcula_el_espacio_recuperable()
    {
        CreateFile("a.tmp", TimeSpan.FromDays(2), sizeInBytes: 2048);

        var findings = await CreateCleaner(TimeSpan.FromHours(24)).ScanAsync();

        Assert.Equal(2048, findings.Sum(f => f.ReclaimableBytes));
    }

    [Fact]
    public async Task Simular_no_borra_nada()
    {
        var path = CreateFile("antiguo.tmp", TimeSpan.FromDays(2));
        var cleaner = CreateCleaner(TimeSpan.FromHours(24));

        var findings = await cleaner.ScanAsync();
        var result = await cleaner.ApplyAsync(findings, RemediationOptions.Preview);

        Assert.True(result.Simulated);
        Assert.Equal(1, result.Applied);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Aplicar_borra_los_ficheros_seleccionados()
    {
        var path = CreateFile("antiguo.tmp", TimeSpan.FromDays(2));
        var cleaner = CreateCleaner(TimeSpan.FromHours(24));

        var findings = await cleaner.ScanAsync();
        var result = await cleaner.ApplyAsync(findings, new RemediationOptions { Simulate = false });

        Assert.Equal(1, result.Applied);
        Assert.Equal(0, result.Failed);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Scan_no_falla_si_la_carpeta_no_existe()
    {
        var cleaner = new TempFileCleaner([new CleaningTarget("Inexistente", Path.Combine(_root, "no-existe"))]);

        var findings = await cleaner.ScanAsync();

        Assert.Empty(findings);
    }
}
