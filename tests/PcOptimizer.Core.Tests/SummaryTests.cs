using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Hardware;
using PcOptimizer.Core.Summary;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class SummaryModuleTests
{
    private sealed class TestFinding : Finding
    {
        public TestFinding(string title, FindingSeverity severity, bool selected = false, long bytes = 0)
            : base(title, title)
        {
            Severity = severity;
            SelectedByDefault = selected;
            ReclaimableBytes = bytes;
        }
    }

    private sealed class FakeModule : IOptimizerModule
    {
        private readonly IReadOnlyList<Finding> _findings;

        public FakeModule(string id, ModuleKind kind, params Finding[] findings)
        {
            Id = id;
            Kind = kind;
            _findings = findings;
        }

        public string Id { get; }

        public ModuleKind Kind { get; }

        public string DisplayName => $"Modulo {Id}";

        public string Description => "Modulo de prueba.";

        public bool RequiresElevation => false;

        public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_findings);
        }

        public Task<RemediationResult> ApplyAsync(
            IReadOnlyCollection<Finding> findings,
            RemediationOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RemediationResult());
    }

    private sealed class BrokenModule : IOptimizerModule
    {
        public string Id => "roto";

        public string DisplayName => "Modulo roto";

        public string Description => "Siempre falla.";

        public bool RequiresElevation => false;

        public Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("se ha roto");

        public Task<RemediationResult> ApplyAsync(
            IReadOnlyCollection<Finding> findings,
            RemediationOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RemediationResult());
    }

    /// <summary>Progreso sincrono: Progress&lt;T&gt; publica en otro hilo y el test no lo veria.</summary>
    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value)
        {
            lock (Messages)
            {
                Messages.Add(value);
            }
        }
    }

    private sealed class RecordingCards : IProgress<Finding>
    {
        public List<Finding> Cards { get; } = [];

        public void Report(Finding value)
        {
            lock (Cards)
            {
                Cards.Add(value);
            }
        }
    }

    private static HardwareProfile Machine() => new()
    {
        Cpu = new CpuInfo("AMD Ryzen 7 7800X3D", 8, 16, 4200, true),
        Gpus = [new GpuInfo("NVIDIA GeForce RTX 4070", 12L * 1024 * 1024 * 1024, "551.23", DateTime.Now)],
        MemoryModules =
        [
            new MemoryModuleInfo("A1", 16L * 1024 * 1024 * 1024, 6000, 6000, "Test"),
            new MemoryModuleInfo("A2", 16L * 1024 * 1024 * 1024, 6000, 6000, "Test")
        ],
        Disks = [new DiskInfo("Disco", 1_000_000_000_000, "SSD", "NVMe", IsSystemDisk: true)]
    };

    private static SummaryModule Summary(params IOptimizerModule[] modules) => new(modules, Machine);

    private static SummaryFinding Card(IReadOnlyList<Finding> findings, string moduleId)
        => findings.OfType<SummaryFinding>().Single(f => f.RelatedModuleId == moduleId);

    [Fact]
    public async Task La_primera_ficha_describe_el_equipo()
    {
        var findings = await Summary().ScanAsync();

        var machine = Assert.IsType<SummaryFinding>(findings[0]);
        Assert.Equal(HardwareInventory.ModuleId, machine.RelatedModuleId);
        Assert.Contains("Ryzen 7 7800X3D", machine.Details);
        Assert.Contains("RTX 4070", machine.Details);
        Assert.Contains("32 GB", machine.Details);
    }

    [Fact]
    public async Task Hay_una_ficha_por_apartado_y_en_su_orden()
    {
        var findings = await Summary(
            new FakeModule("a", ModuleKind.Actionable),
            new FakeModule("b", ModuleKind.Diagnostic),
            new FakeModule("c", ModuleKind.Actionable)).ScanAsync();

        Assert.Equal(
            new string?[] { "a", "b", "c" },
            findings.Skip(1).Select(f => f.RelatedModuleId).ToArray());
    }

    [Fact]
    public async Task Un_temporal_marcado_por_defecto_cuenta_aunque_sea_informativo()
    {
        // Un temporal viejo es Info, pero es exactamente lo que hay que borrar.
        var module = new FakeModule("temp", ModuleKind.Actionable,
            new TestFinding("a1b2c3", FindingSeverity.Info, selected: true, bytes: 1024),
            new TestFinding("d4e5f6", FindingSeverity.Info, selected: true, bytes: 1024),
            new TestFinding("otro", FindingSeverity.Info));

        var card = Card(await Summary(module).ScanAsync(), "temp");

        Assert.Equal(2, card.PendingCount);
        Assert.Equal(2048, card.ReclaimableBytes);
        Assert.Equal(FindingSeverity.Suggestion, card.Severity);
    }

    [Fact]
    public async Task Los_nombres_ilegibles_no_se_destacan()
    {
        // Los temporales se llaman como un hash: nombrarlos no ayuda a nadie.
        var module = new FakeModule("temp", ModuleKind.Actionable,
            new TestFinding("a1b2c3d4e5", FindingSeverity.Info, selected: true));

        var card = Card(await Summary(module).ScanAsync(), "temp");

        Assert.Empty(card.Recommendation);
    }

    [Fact]
    public async Task En_un_diagnostico_solo_cuenta_lo_que_no_es_informativo()
    {
        var module = new FakeModule("bios", ModuleKind.Diagnostic,
            new TestFinding("Secure Boot", FindingSeverity.Info),
            new TestFinding("XMP", FindingSeverity.Warning),
            new TestFinding("TPM", FindingSeverity.Suggestion));

        var card = Card(await Summary(module).ScanAsync(), "bios");

        Assert.Equal(2, card.PendingCount);
        Assert.Equal(FindingSeverity.Warning, card.Severity);
        Assert.Contains("1 importante", card.Details);
    }

    [Fact]
    public async Task Lo_mas_grave_se_destaca_primero()
    {
        var module = new FakeModule("bios", ModuleKind.Diagnostic,
            new TestFinding("TPM", FindingSeverity.Suggestion),
            new TestFinding("XMP", FindingSeverity.Warning));

        var card = Card(await Summary(module).ScanAsync(), "bios");

        Assert.StartsWith("Lo primero: XMP", card.Recommendation);
    }

    [Fact]
    public async Task Sin_nada_pendiente_la_ficha_es_informativa()
    {
        var module = new FakeModule("ok", ModuleKind.Actionable,
            new TestFinding("Todo bien", FindingSeverity.Info));

        var card = Card(await Summary(module).ScanAsync(), "ok");

        Assert.Equal(0, card.PendingCount);
        Assert.Equal(FindingSeverity.Info, card.Severity);
        Assert.StartsWith("Nada que hacer", card.Details);
    }

    [Fact]
    public async Task Un_apartado_que_falla_no_tumba_el_resumen()
    {
        var findings = await Summary(
            new FakeModule("a", ModuleKind.Actionable),
            new BrokenModule(),
            new FakeModule("c", ModuleKind.Actionable)).ScanAsync();

        var broken = Card(findings, "roto");

        Assert.Equal(FindingSeverity.Warning, broken.Severity);
        Assert.Contains("se ha roto", broken.Details);
        Assert.Equal(4, findings.Count);
    }

    [Fact]
    public async Task Los_resultados_completos_quedan_disponibles()
    {
        var finding = new TestFinding("x", FindingSeverity.Suggestion);
        var summary = Summary(new FakeModule("a", ModuleKind.Actionable, finding));

        await summary.ScanAsync();

        Assert.Same(finding, Assert.Single(summary.LastResults["a"]));

        // El inventario no se analiza aparte, pero sus resultados salen del
        // mismo perfil: entrar en su pagina desde el resumen es instantaneo.
        Assert.True(summary.LastResults.ContainsKey(HardwareInventory.ModuleId));
    }

    [Fact]
    public async Task Un_apartado_roto_no_deja_resultados_a_medias()
    {
        var summary = Summary(new BrokenModule());

        await summary.ScanAsync();

        Assert.False(summary.LastResults.ContainsKey("roto"));
    }

    [Fact]
    public async Task Informa_del_avance_apartado_a_apartado()
    {
        var progress = new RecordingProgress();
        var summary = Summary(
            new FakeModule("a", ModuleKind.Actionable),
            new FakeModule("b", ModuleKind.Actionable));

        summary.Progress = progress;
        await summary.ScanAsync();

        // Uno al empezar y uno por cada apartado terminado.
        Assert.Equal(3, progress.Messages.Count);
        Assert.Contains(progress.Messages, m => m.Contains("2 de 2"));
    }

    [Fact]
    public async Task Cada_ficha_se_entrega_en_cuanto_esta_lista()
    {
        // Al abrir la aplicacion el resumen se lanza solo: las fichas tienen
        // que ir apareciendo, no llegar todas de golpe al final.
        var cards = new RecordingCards();
        var summary = Summary(
            new FakeModule("a", ModuleKind.Actionable),
            new BrokenModule());

        summary.CardReady = cards;
        var findings = await summary.ScanAsync();

        Assert.Equal(findings.Count, cards.Cards.Count);
        Assert.Equal(HardwareInventory.ModuleId, cards.Cards[0].RelatedModuleId);

        // La del modulo roto tambien llega: si no, su hueco se quedaria vacio.
        Assert.Contains(cards.Cards, c => c.RelatedModuleId == "roto");
    }

    [Fact]
    public void Sabe_que_apartados_cubre()
    {
        var summary = Summary(new FakeModule("a", ModuleKind.Actionable));

        Assert.True(summary.Covers("a"));
        Assert.True(summary.Covers(HardwareInventory.ModuleId));
        Assert.False(summary.Covers("undo.changes"));
    }

    [Fact]
    public async Task Cancelar_cancela_de_verdad()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Summary(new FakeModule("a", ModuleKind.Actionable)).ScanAsync(cancelled.Token));
    }

    [Fact]
    public async Task El_resumen_no_se_incluye_a_si_mismo()
    {
        var inner = new SummaryModule([], Machine);
        var outer = new SummaryModule([inner, new FakeModule("a", ModuleKind.Actionable)], Machine);

        // Si se incluyera, analizarlo lanzaria otro resumen dentro, y otro...
        var findings = await outer.ScanAsync();

        Assert.Equal(2, findings.Count);
        Assert.DoesNotContain(findings, f => f.RelatedModuleId == SummaryModule.ModuleId);
    }
}

public sealed class HardwareProfileCacheTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Se_lee_una_vez_y_se_reutiliza()
    {
        var captures = 0;
        var cache = new HardwareProfileCache(() => { captures++; return new HardwareProfile(); });

        cache.Get();
        cache.Get();
        cache.Get();

        Assert.Equal(1, captures);
    }

    [Fact]
    public void Refrescar_vuelve_a_leer()
    {
        var captures = 0;
        var cache = new HardwareProfileCache(() => { captures++; return new HardwareProfile(); });

        cache.Get();
        cache.Refresh();

        Assert.Equal(2, captures);
    }

    [Fact]
    public void Pasado_el_tiempo_maximo_se_vuelve_a_leer()
    {
        var time = new ManualTime();
        var captures = 0;
        var cache = new HardwareProfileCache(
            () => { captures++; return new HardwareProfile(); },
            TimeSpan.FromMinutes(5),
            time);

        cache.Get();
        time.Now += TimeSpan.FromMinutes(4);
        cache.Get();
        Assert.Equal(1, captures);

        time.Now += TimeSpan.FromMinutes(2);
        cache.Get();
        Assert.Equal(2, captures);
    }

    [Fact]
    public async Task Si_varios_lo_piden_a_la_vez_solo_uno_lee()
    {
        // Es lo que pasa en el resumen: rendimiento y firmware arrancan a la vez.
        var captures = 0;
        var cache = new HardwareProfileCache(() =>
        {
            Interlocked.Increment(ref captures);
            Thread.Sleep(50);
            return new HardwareProfile();
        });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.Get())));

        Assert.Equal(1, captures);
    }
}
