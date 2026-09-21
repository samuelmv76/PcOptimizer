using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Cleaning;
using PcOptimizer.Core.Platform;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class FileDeletionTests : IDisposable
{
    private readonly string _root;

    public FileDeletionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PcOptimizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Limpieza de los tests: si no se puede, no importa.
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateFile(string name, int sizeInBytes = 64)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[sizeInBytes]);
        return path;
    }

    [Fact]
    public void Un_fichero_normal_se_borra()
    {
        var path = CreateFile("normal.tmp");

        Assert.True(FileDeletion.TryDelete(path, out var error));
        Assert.Null(error);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Un_fichero_de_solo_lectura_tambien_se_borra()
    {
        var path = CreateFile("protegido.tmp");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.True(FileDeletion.TryDelete(path, out var error));
        Assert.Null(error);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Un_fichero_oculto_y_de_sistema_tambien_se_borra()
    {
        var path = CreateFile("oculto.tmp");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);

        Assert.True(FileDeletion.TryDelete(path, out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Un_fichero_en_uso_da_un_motivo_legible()
    {
        var path = CreateFile("abierto.tmp");

        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(FileDeletion.TryDelete(path, out var error));
        Assert.Equal("en uso por otro programa", error);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Un_fichero_que_ya_no_esta_no_es_un_fallo()
        => Assert.True(FileDeletion.TryDelete(Path.Combine(_root, "no-existe.tmp"), out _));

    [Fact]
    public async Task El_limpiador_reporta_el_fichero_bloqueado_y_borra_el_resto()
    {
        var libre = CreateFile("libre.tmp", 1024);
        var bloqueado = CreateFile("bloqueado.tmp", 1024);

        File.SetLastWriteTimeUtc(libre, DateTime.UtcNow - TimeSpan.FromDays(3));
        File.SetLastWriteTimeUtc(bloqueado, DateTime.UtcNow - TimeSpan.FromDays(3));

        using var handle = new FileStream(bloqueado, FileMode.Open, FileAccess.Read, FileShare.None);

        var cleaner = new TempFileCleaner([new CleaningTarget("Test", _root)]);
        var findings = await cleaner.ScanAsync();
        var result = await cleaner.ApplyAsync(findings, new RemediationOptions { Simulate = false });

        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1024, result.BytesFreed);

        var error = Assert.Single(result.Errors);
        Assert.Contains("bloqueado.tmp", error);
        Assert.Contains("en uso", error);

        Assert.False(File.Exists(libre));
        Assert.True(File.Exists(bloqueado));

        // Sin pedirlo, nada queda programado para el proximo arranque.
        Assert.Equal(0, result.Deferred);
    }

    [Theory]
    [InlineData("en uso por otro programa", true)]
    [InlineData("sin permisos (puede pertenecer a otra cuenta)", false)]
    [InlineData(null, false)]
    public void Se_reconoce_cuando_el_motivo_es_que_esta_en_uso(string? error, bool expected)
        => Assert.Equal(expected, FileDeletion.IsInUse(error));

    [Fact]
    public async Task Un_fichero_borrado_entre_el_analisis_y_el_aplicar_no_cuenta_como_fallo()
    {
        var path = CreateFile("efimero.tmp", 512);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromDays(3));

        var cleaner = new TempFileCleaner([new CleaningTarget("Test", _root)]);
        var findings = await cleaner.ScanAsync();

        File.Delete(path);

        var result = await cleaner.ApplyAsync(findings, new RemediationOptions { Simulate = false });

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
    }
}

public sealed class RestorePointPolicyTests
{
    [Fact]
    public void La_limpieza_de_ficheros_no_ofrece_punto_de_restauracion()
    {
        // Restaurar sistema no cubre ficheros de usuario ni temporales:
        // ofrecerlo aqui solo llenaria el disco de puntos inutiles.
        // BenefitsFromRestorePoint es un miembro por defecto de la interfaz: en
        // los modulos que no lo redefinen solo existe a traves de IOptimizerModule.
        Assert.False(Policy(new TempFileCleaner(TempFileCleaner.DefaultTargets())));
        Assert.False(Policy(new PcOptimizer.Core.Disk.DiskSpaceAnalyzer()));
    }

    [Fact]
    public void Lo_que_toca_registro_o_programas_si_lo_ofrece()
    {
        Assert.True(Policy(new PcOptimizer.Core.Startup.StartupManager()));
        Assert.True(Policy(new PcOptimizer.Core.Bloatware.BloatwareModule()));
    }

    private static bool Policy(IOptimizerModule module) => module.BenefitsFromRestorePoint;
}
