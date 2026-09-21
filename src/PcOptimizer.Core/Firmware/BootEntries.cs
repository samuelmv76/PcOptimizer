using System.Buffers.Binary;
using System.Text;
using PcOptimizer.Core.Platform;

namespace PcOptimizer.Core.Firmware;

/// <summary>Una entrada del gestor de arranque del firmware (Boot####).</summary>
public sealed record BootEntry(ushort Number, string Description, bool IsActive, bool IsHidden)
{
    public string VariableName => $"Boot{Number:X4}";
}

/// <summary>
/// Lectura del orden de arranque del firmware y las dos acciones que UEFI
/// preve que haga el sistema operativo: arrancar una vez desde otra entrada
/// (BootNext) y abrir la configuracion al reiniciar (OsIndications).
/// Ninguna cambia la configuracion de la placa: la primera se consume sola
/// en el siguiente arranque y la segunda solo abre el menu.
/// </summary>
public static class BootEntries
{
    /// <summary>EFI_OS_INDICATIONS_BOOT_TO_FW_UI.</summary>
    public const ulong BootToFirmwareUi = 0x1;

    private const uint LoadOptionActive = 0x1;
    private const uint LoadOptionHidden = 0x8;

    public static IReadOnlyList<ushort> ParseBootOrder(byte[]? data)
    {
        if (data is null)
        {
            return [];
        }

        var order = new List<ushort>(data.Length / 2);

        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            order.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i, 2)));
        }

        return order;
    }

    /// <summary>
    /// EFI_LOAD_OPTION: UINT32 atributos, UINT16 longitud de rutas, y la
    /// descripcion en UTF-16 terminada en nulo. Lo que sigue (la ruta del
    /// dispositivo) no hace falta para mostrar ni para elegir la entrada.
    /// </summary>
    public static BootEntry? ParseLoadOption(ushort number, byte[]? data)
    {
        if (data is null || data.Length < 8)
        {
            return null;
        }

        var attributes = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));

        var start = 6;
        var end = start;

        while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
        {
            end += 2;
        }

        var description = Encoding.Unicode.GetString(data, start, end - start).Trim();

        return new BootEntry(
            number,
            description.Length == 0 ? $"Entrada {number:X4}" : description,
            (attributes & LoadOptionActive) != 0,
            (attributes & LoadOptionHidden) != 0);
    }

    public static byte[] EncodeBootNext(ushort number)
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, number);
        return data;
    }

    public static ulong DecodeUInt64(byte[]? data)
        => data is { Length: >= 8 } ? BinaryPrimitives.ReadUInt64LittleEndian(data) : 0;

    public static byte[] EncodeUInt64(ulong value)
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, value);
        return data;
    }

    /// <summary>Las entradas del orden de arranque, en ese orden, visibles y activas.</summary>
    public static IReadOnlyList<BootEntry> Read()
    {
        var entries = new List<BootEntry>();

        foreach (var number in ParseBootOrder(UefiVariables.Read("BootOrder")))
        {
            var entry = ParseLoadOption(number, UefiVariables.Read($"Boot{number:X4}"));

            if (entry is { IsActive: true, IsHidden: false })
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public static ushort? ReadBootCurrent()
    {
        var data = UefiVariables.Read("BootCurrent");
        return data is { Length: >= 2 } ? BinaryPrimitives.ReadUInt16LittleEndian(data) : null;
    }

    public static bool FirmwareSupportsBootToSetup()
        => (DecodeUInt64(UefiVariables.Read("OsIndicationsSupported")) & BootToFirmwareUi) != 0;

    public static bool IsBootToSetupPending()
        => (DecodeUInt64(UefiVariables.Read("OsIndications")) & BootToFirmwareUi) != 0;
}
