using System.Runtime.InteropServices;

namespace PcOptimizer.Core.Disk;

/// <summary>
/// Papelera de reciclaje via shell32. Consultar y vaciar por la API es lo
/// correcto: borrar a mano dentro de $Recycle.Bin deja la papelera corrupta.
/// </summary>
internal static class RecycleBin
{
    private const uint NoConfirmation = 0x1;
    private const uint NoProgressUi = 0x2;
    private const uint NoSound = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public static (long Bytes, long Items) Query()
    {
        var info = new SHQUERYRBINFO();
        info.cbSize = Marshal.SizeOf(info);

        try
        {
            // rootPath nulo consulta todas las unidades.
            return SHQueryRecycleBin(null, ref info) == 0
                ? (info.i64Size, info.i64NumItems)
                : (0, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return (0, 0);
        }
    }

    public static void Empty()
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);

        // 0 es correcto; -2147418113 (E_UNEXPECTED) lo devuelve cuando ya esta vacia.
        if (result != 0 && result != unchecked((int)0x8000FFFF))
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }
}
