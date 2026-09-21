using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Undo;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class ChangeJournalTests : IDisposable
{
    private readonly string _path;

    public ChangeJournalTests()
    {
        _path = Path.Combine(
            Path.GetTempPath(),
            "PcOptimizerTests",
            Guid.NewGuid().ToString("N"),
            "changes.jsonl");
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_path);

        if (folder is not null && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private JsonChangeJournal Journal() => new(_path);

    private static ReversibleChange Change(string description = "Un cambio") => new()
    {
        ModuleId = "test",
        Kind = ChangeKinds.RegistryValue,
        Target = @"CurrentUser\Software\Test!Valor",
        PreviousValue = "1",
        NewValue = "0",
        ValueKind = "DWord",
        Description = description
    };

    [Fact]
    public void Un_diario_que_no_existe_esta_vacio()
        => Assert.Empty(Journal().ReadAll());

    [Fact]
    public void Lo_guardado_se_lee_igual()
    {
        var journal = Journal();
        var change = Change();

        journal.Record(change);

        var stored = Assert.Single(journal.ReadAll());
        Assert.Equal(change.Id, stored.Id);
        Assert.Equal("1", stored.PreviousValue);
        Assert.Equal("DWord", stored.ValueKind);
        Assert.False(stored.Reverted);
    }

    [Fact]
    public void El_mas_reciente_va_primero()
    {
        var journal = Journal();

        journal.Record(Change("Primero"));
        journal.Record(Change("Segundo"));

        var all = journal.ReadAll();

        Assert.Equal("Segundo", all[0].Description);
        Assert.Equal("Primero", all[1].Description);
    }

    [Fact]
    public void Marcar_como_deshecho_no_borra_el_historial()
    {
        var journal = Journal();
        var change = Change();

        journal.Record(change);
        journal.MarkReverted(change.Id);

        // Sigue habiendo un solo cambio, pero ahora consta como deshecho.
        var stored = Assert.Single(journal.ReadAll());
        Assert.True(stored.Reverted);

        // Y el fichero conserva las dos lineas: el historial no miente.
        Assert.Equal(2, File.ReadAllLines(_path).Length);
    }

    [Fact]
    public void Una_linea_corrupta_no_invalida_el_resto()
    {
        var journal = Journal();
        journal.Record(Change("Bueno"));

        File.AppendAllText(_path, "esto no es json" + Environment.NewLine);

        var stored = Assert.Single(journal.ReadAll());
        Assert.Equal("Bueno", stored.Description);
    }

    [Fact]
    public void Marcar_un_cambio_que_no_existe_no_hace_nada()
    {
        var journal = Journal();
        journal.Record(Change());

        journal.MarkReverted("inventado");

        Assert.Single(journal.ReadAll());
    }
}

public sealed class UndoModuleTests
{
    private sealed class FakeJournal : IChangeJournal
    {
        private readonly List<ReversibleChange> _changes;

        public FakeJournal(params ReversibleChange[] changes) => _changes = [.. changes];

        public List<string> Reverted { get; } = [];

        public void Record(ReversibleChange change) => _changes.Add(change);

        public IReadOnlyList<ReversibleChange> ReadAll() => _changes;

        public void MarkReverted(string changeId) => Reverted.Add(changeId);
    }

    private static ReversibleChange Registry(bool reverted = false) => new()
    {
        Kind = ChangeKinds.RegistryValue,
        Target = @"CurrentUser\Software\Test!Valor",
        PreviousValue = "1",
        ValueKind = "DWord",
        Description = "Ajuste de registro",
        Reverted = reverted
    };

    [Fact]
    public async Task Lo_ya_deshecho_no_se_vuelve_a_ofrecer()
    {
        var module = new UndoModule(new FakeJournal(Registry(), Registry(reverted: true)));

        var findings = await module.ScanAsync();

        Assert.Single(findings);
    }

    [Fact]
    public async Task Nada_viene_marcado_por_defecto()
    {
        var module = new UndoModule(new FakeJournal(Registry()));

        var findings = await module.ScanAsync();

        Assert.All(findings, f => Assert.False(f.SelectedByDefault));
    }

    [Theory]
    [InlineData(ChangeKinds.RegistryValue, @"CurrentUser\Software\Test!Valor", "1", true)]
    [InlineData(ChangeKinds.RegistryValue, "ruta mala", "1", false)]
    [InlineData(ChangeKinds.StartupEntry, "CurrentUserRun:Cosa", "C:\\cosa.exe", true)]
    [InlineData(ChangeKinds.StartupEntry, "SinDosPuntos", "C:\\cosa.exe", false)]
    [InlineData(ChangeKinds.PowerPlan, "x", "381b4222-f694-41f0-9685-ff5bb260df2e", true)]
    [InlineData(ChangeKinds.PowerPlan, "x", "no-es-un-guid", false)]
    [InlineData(ChangeKinds.ServiceStartMode, "Fax", "Auto", true)]
    [InlineData("tipo-inventado", "x", "y", false)]
    public void Solo_se_ofrece_lo_que_de_verdad_se_sabe_deshacer(
        string kind, string target, string previous, bool expected)
    {
        var change = new ReversibleChange
        {
            Kind = kind,
            Target = target,
            PreviousValue = previous
        };

        Assert.Equal(expected, UndoModule.CanRevert(change));
    }

    [Fact]
    public async Task Un_cambio_que_no_se_sabe_deshacer_se_reporta_como_tal()
    {
        var change = new ReversibleChange { Kind = "tipo-inventado", Description = "Algo raro" };
        var journal = new FakeJournal(change);
        var module = new UndoModule(journal);

        var findings = await module.ScanAsync();
        var result = await module.ApplyAsync(findings, new RemediationOptions { Simulate = false });

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Failed);
        Assert.Empty(journal.Reverted);
    }

    [Fact]
    public async Task Simular_no_marca_nada_como_deshecho()
    {
        var journal = new FakeJournal(Registry());
        var module = new UndoModule(journal);

        var findings = await module.ScanAsync();
        var result = await module.ApplyAsync(findings, RemediationOptions.Preview);

        Assert.Equal(1, result.Applied);
        Assert.Empty(journal.Reverted);
    }
}
