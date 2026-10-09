using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>
/// PRINTER_INFO_2W (winspool.h), the fields the Printers panel shows. Strings are left as pointers into the buffer
/// EnumPrinters filled; read them with <see cref="Marshal.PtrToStringUni(nint)"/> before freeing the buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PRINTER_INFO_2
{
    public nint pServerName;
    public nint pPrinterName;
    public nint pShareName;
    public nint pPortName;
    public nint pDriverName;
    public nint pComment;
    public nint pLocation;
    public nint pDevMode;
    public nint pSepFile;
    public nint pPrintProcessor;
    public nint pDatatype;
    public nint pParameters;
    public nint pSecurityDescriptor;
    public uint Attributes;
    public uint Priority;
    public uint DefaultPriority;
    public uint StartTime;
    public uint UntilTime;
    public uint Status;
    public uint cJobs;
    public uint AveragePPM;
}

/// <summary>Print spooler calls for the Printers panel (winspool.drv).</summary>
internal static partial class NativeMethods
{
    public const uint PRINTER_ENUM_LOCAL = 0x2;
    public const uint PRINTER_ENUM_CONNECTIONS = 0x4;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Lists printers at information level 2 into <paramref name="buffer"/>; call with a null buffer to learn the size.</summary>
    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumPrinters(uint flags, string? name, uint level, nint buffer, uint bufferSize, out uint needed, out uint returned);

    /// <summary>Copies the default printer's name into <paramref name="buffer"/> (size in characters, including the terminator).</summary>
    [LibraryImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDefaultPrinter(nint buffer, ref uint size);

    [LibraryImport("winspool.drv", EntryPoint = "SetDefaultPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetDefaultPrinter(string name);
}
