using PcOptimizer.Core.Platform;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(1073741824, "1 GB")]
    public void Se_formatea_en_la_unidad_mas_legible(long bytes, string expected)
    {
        var formatted = ByteSize.Format(bytes);

        // El separador decimal depende de la cultura del equipo.
        Assert.Equal(
            expected.Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
            formatted);
    }
}

public sealed class PowerShellQuotingTests
{
    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("con espacio", "'con espacio'")]
    [InlineData("con'comilla", "'con''comilla'")]
    [InlineData("$variable", "'$variable'")]
    public void El_valor_queda_dentro_de_un_literal_de_cadena(string value, string expected)
        => Assert.Equal(expected, PowerShellRunner.Quote(value));
}

public sealed class SafeDirectoryTests : IDisposable
{
    private readonly string _root;

    public SafeDirectoryTests()
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

    private string Write(string relativePath, int sizeInBytes)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeInBytes]);
        return path;
    }

    [Fact]
    public void Recorre_las_subcarpetas()
    {
        Write("raiz.txt", 10);
        Write(Path.Combine("uno", "dos", "hondo.txt"), 10);

        var files = SafeDirectory.EnumerateFiles(_root).ToList();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void Sin_recursion_solo_mira_el_primer_nivel()
    {
        Write("raiz.txt", 10);
        Write(Path.Combine("uno", "hondo.txt"), 10);

        var files = SafeDirectory.EnumerateFiles(_root, recursive: false).ToList();

        Assert.Single(files);
    }

    [Fact]
    public void Respeta_el_patron()
    {
        Write("uno.tmp", 10);
        Write("dos.log", 10);

        var files = SafeDirectory.EnumerateFiles(_root, "*.tmp").ToList();

        Assert.Single(files);
    }

    [Fact]
    public void Una_carpeta_que_no_existe_no_es_un_error()
        => Assert.Empty(SafeDirectory.EnumerateFiles(Path.Combine(_root, "no-existe")));

    [Fact]
    public void El_tamano_suma_todos_los_niveles()
    {
        Write("raiz.bin", 1000);
        Write(Path.Combine("uno", "dos.bin"), 24);

        Assert.Equal(1024, SafeDirectory.GetDirectorySize(_root));
    }

    [Fact]
    public void El_tamano_de_una_carpeta_inexistente_es_cero()
        => Assert.Equal(0, SafeDirectory.GetDirectorySize(Path.Combine(_root, "no-existe")));

    [Fact]
    public void Se_puede_cancelar_a_mitad()
    {
        Write("uno.bin", 10);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => SafeDirectory.EnumerateFiles(_root, cancellationToken: cancelled.Token).ToList());
    }
}
<<<<<<< HEAD

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PcOptimizerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private PcOptimizer.Core.Configuration.SettingsStore Store()
        => new(Path.Combine(_folder, "settings.json"));

    [Fact]
    public void Sin_fichero_el_tema_por_defecto_es_el_oscuro()
    {
        var settings = Store().Load();

        Assert.Equal(PcOptimizer.Core.Configuration.ThemePreference.Dark, settings.Theme);
        Assert.True(settings.AnalyzeOnStartup);
    }

    [Fact]
    public void Lo_guardado_se_lee_igual()
    {
        var store = Store();
        var saved = new PcOptimizer.Core.Configuration.AppSettings
        {
            Theme = PcOptimizer.Core.Configuration.ThemePreference.System,
            AnalyzeOnStartup = false
        };

        Assert.True(store.TrySave(saved));
        Assert.Equal(saved, store.Load());
    }

    [Fact]
    public void El_tema_se_guarda_como_texto_legible()
    {
        var store = Store();
        store.TrySave(new PcOptimizer.Core.Configuration.AppSettings { Theme = PcOptimizer.Core.Configuration.ThemePreference.Light });

        Assert.Contains("\"Light\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Un_fichero_corrupto_no_impide_arrancar()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ esto no es json");

        Assert.Equal(PcOptimizer.Core.Configuration.AppSettings.Default, Store().Load());
    }
}
=======
>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0
