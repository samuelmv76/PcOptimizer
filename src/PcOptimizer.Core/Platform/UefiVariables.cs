using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PcOptimizer.Core.Platform;

/// <summary>
/// Variables de UEFI por la API de Windows. Solo se usan variables del
/// espacio global que la especificacion UEFI define para que el sistema
/// operativo las escriba (BootNext, OsIndications). Nunca la variable
/// "Setup" del fabricante: su formato es propio de cada placa y version, y
/// escribirla a ciegas puede dejar el equipo sin arrancar.
/// </summary>
public static class UefiVariables
{
    /// <summary>EFI_GLOBAL_VARIABLE, definido por la especificacion UEFI.</summary>
    public const string GlobalGuid = "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}";

    /// <summary>No volatil, accesible en arranque y en ejecucion.</summary>
    public const uint DefaultAttributes = 0x1 | 0x2 | 0x4;

    private const int ErrorEnvVarNotFound = 203;
    private const int ErrorInvalidFunction = 1;

    private static readonly Lock PrivilegeGate = new();
    private static bool _privilegeEnabled;

    /// <summary>
    /// Lee una variable. Null si no existe; lanza si Windows no deja leer
    /// (sin permisos de administrador, o arranque Legacy).
    /// </summary>
    public static byte[]? Read(string name, string guid = GlobalGuid)
    {
        EnsurePrivilege();

        var buffer = new byte[4096];
        var size = GetFirmwareEnvironmentVariableExW(name, guid, buffer, (uint)buffer.Length, out _);

        if (size == 0)
        {
            var error = Marshal.GetLastWin32Error();

            if (error == ErrorEnvVarNotFound)
            {
                return null;
            }

            throw Describe(error);
        }

        return buffer[..(int)size];
    }

    /// <summary>Escribe una variable. Con valor null la borra.</summary>
    public static void Write(string name, byte[]? value, string guid = GlobalGuid, uint attributes = DefaultAttributes)
    {
        EnsurePrivilege();

        var data = value ?? [];

        if (!SetFirmwareEnvironmentVariableExW(name, guid, data, (uint)data.Length, attributes))
        {
            var error = Marshal.GetLastWin32Error();

            // Borrar algo que ya no existe no es un error.
            if (value is null && error == ErrorEnvVarNotFound)
            {
                return;
            }

            throw Describe(error);
        }
    }

    /// <summary>Si este equipo arranco en modo UEFI y se pueden leer sus variables.</summary>
    public static bool IsAvailable()
    {
        try
        {
            Read("BootOrder");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception
                                   or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static string ToHex(byte[]? data) => data is null ? string.Empty : Convert.ToHexString(data);

    public static byte[]? FromHex(string? hex) => string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);

    private static InvalidOperationException Describe(int error) => error switch
    {
        ErrorInvalidFunction => new InvalidOperationException(
            "este equipo arranca en modo Legacy: no tiene variables UEFI."),
        5 or 1314 => new InvalidOperationException(
            "Windows no deja tocar el firmware sin permisos de administrador."),
        _ => new InvalidOperationException($"Windows devolvió el error {error} al acceder al firmware.")
    };

    /// <summary>
    /// Leer o escribir variables de firmware exige activar un privilegio
    /// concreto en el proceso, aunque ya se ejecute como administrador.
    /// </summary>
    private static void EnsurePrivilege()
    {
        lock (PrivilegeGate)
        {
            if (_privilegeEnabled)
            {
                return;
            }

            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            {
                throw Describe(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out var luid))
                {
                    throw Describe(Marshal.GetLastWin32Error());
                }

                var privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                };

                AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);

                // AdjustTokenPrivileges devuelve true aunque no lo conceda:
                // el error real va en GetLastError.
                var error = Marshal.GetLastWin32Error();

                if (error != 0)
                {
                    throw Describe(error == 1300 ? 1314 : error);
                }

                _privilegeEnabled = true;
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }

    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint SePrivilegeEnabled = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFirmwareEnvironmentVariableExW(
        string name, string guid, byte[] buffer, uint size, out uint attributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableExW(
        string name, string guid, byte[] value, uint size, uint attributes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token, bool disableAll, ref TokenPrivileges newState, uint bufferLength,
        IntPtr previousState, IntPtr returnLength);
}
