using System.Text;
using PcOptimizer.Core.Firmware;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Undo;
using Xunit;

namespace PcOptimizer.Core.Tests;

public sealed class BootEntriesTests
{
    private static byte[] LoadOption(uint attributes, string description)
    {
        var text = Encoding.Unicode.GetBytes(description + "\0");
        var devicePath = new byte[] { 0x7F, 0xFF, 0x04, 0x00 }; // fin de ruta
        var data = new byte[6 + text.Length + devicePath.Length];

        BitConverter.GetBytes(attributes).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)devicePath.Length).CopyTo(data, 4);
        text.CopyTo(data, 6);
        devicePath.CopyTo(data, 6 + text.Length);
        return data;
    }

    [Fact]
    public void El_orden_de_arranque_es_una_lista_de_enteros_de_16_bits()
        => Assert.Equal(new ushort[] { 3, 1, 0x1A }, BootEntries.ParseBootOrder([0x03, 0x00, 0x01, 0x00, 0x1A, 0x00]));

    [Fact]
    public void Sin_orden_de_arranque_no_hay_entradas()
        => Assert.Empty(BootEntries.ParseBootOrder(null));

    [Fact]
    public void Se_lee_la_descripcion_de_una_entrada()
    {
        var entry = BootEntries.ParseLoadOption(1, LoadOption(0x1, "Windows Boot Manager"));

        Assert.NotNull(entry);
        Assert.Equal("Windows Boot Manager", entry.Description);
        Assert.True(entry.IsActive);
        Assert.False(entry.IsHidden);
        Assert.Equal("Boot0001", entry.VariableName);
    }

    [Fact]
    public void Se_distinguen_las_entradas_inactivas_y_ocultas()
    {
        var inactive = BootEntries.ParseLoadOption(2, LoadOption(0x0, "Red"));
        var hidden = BootEntries.ParseLoadOption(3, LoadOption(0x1 | 0x8, "Diagnóstico"));

        Assert.False(inactive!.IsActive);
        Assert.True(hidden!.IsHidden);
    }

    [Fact]
    public void Una_entrada_truncada_no_rompe_nada()
        => Assert.Null(BootEntries.ParseLoadOption(1, [0x01, 0x00]));

    [Fact]
    public void BootNext_se_codifica_en_little_endian()
        => Assert.Equal(new byte[] { 0x1A, 0x00 }, BootEntries.EncodeBootNext(0x1A));

    [Fact]
    public void Ida_y_vuelta_de_OsIndications()
        => Assert.Equal(0x41UL, BootEntries.DecodeUInt64(BootEntries.EncodeUInt64(0x41)));
}

public sealed class BiosMenuGuideTests
{
    [Theory]
    [InlineData("ASUSTeK COMPUTER INC. ROG STRIX B650E-F", BoardVendor.Asus)]
    [InlineData("Micro-Star International Co., Ltd. MAG B550 TOMAHAWK", BoardVendor.Msi)]
    [InlineData("Gigabyte Technology Co., Ltd. B650 AORUS ELITE AX", BoardVendor.Gigabyte)]
    [InlineData("ASRock B650M Pro RS", BoardVendor.AsRock)]
    [InlineData("Placa rara", BoardVendor.Unknown)]
    public void Se_reconoce_el_fabricante_de_la_placa(string board, BoardVendor expected)
        => Assert.Equal(expected, BiosMenuGuide.Detect(board));

    [Fact]
    public void ASRock_no_se_confunde_con_ASUS()
        => Assert.Equal(BoardVendor.AsRock, BiosMenuGuide.Detect("ASRock X670E Taichi"));

    [Fact]
    public void En_AMD_la_ruta_habla_de_EXPO_y_en_Intel_de_XMP()
    {
        Assert.Contains("EXPO", BiosMenuGuide.PathFor(BoardVendor.Asus, BiosSetting.MemoryProfile, amd: true));
        Assert.Contains("XMP", BiosMenuGuide.PathFor(BoardVendor.Asus, BiosSetting.MemoryProfile, amd: false));
    }

    [Fact]
    public void Todos_los_fabricantes_conocidos_tienen_ruta_para_todo()
    {
        foreach (var vendor in new[] { BoardVendor.Asus, BoardVendor.Msi, BoardVendor.Gigabyte, BoardVendor.AsRock })
        foreach (var setting in Enum.GetValues<BiosSetting>())
        foreach (var amd in new[] { true, false })
        {
            Assert.False(string.IsNullOrWhiteSpace(BiosMenuGuide.PathFor(vendor, setting, amd)), $"{vendor} {setting} amd={amd}");
        }
    }

    [Fact]
    public void Con_placa_desconocida_se_indica_igualmente_como_entrar()
    {
        var text = BiosMenuGuide.Directions("Placa rara", "AMD Ryzen 7", BiosSetting.Virtualization);

        Assert.Contains("Abrir la BIOS al reiniciar", text);
    }
}

public sealed class FirmwareUndoTests
{
    [Theory]
    [InlineData("BootNext", null, true)]
    [InlineData("BootNext", "0100", true)]
    [InlineData("OsIndications", "0000000000000000", true)]
    [InlineData("Setup", "00", false)]
    [InlineData("BootOrder", "0100", false)]
    [InlineData("BootNext", "no-es-hex", false)]
    public void Solo_se_deshacen_las_variables_que_escribe_la_aplicacion(string target, string? previous, bool expected)
    {
        var change = new ReversibleChange
        {
            Kind = ChangeKinds.FirmwareVariable,
            Target = target,
            PreviousValue = previous
        };

        Assert.Equal(expected, UndoModule.CanRevert(change));
    }
}
