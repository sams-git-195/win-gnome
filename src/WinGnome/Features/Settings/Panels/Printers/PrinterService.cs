using System.ComponentModel;
using System.Runtime.InteropServices;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Printers;

/// <summary>One printer as the spooler reports it.</summary>
/// <param name="Name">The printer's name (a UNC path for a connection to a print server).</param>
/// <param name="Status">PRINTER_STATUS_* bits.</param>
/// <param name="Attributes">PRINTER_ATTRIBUTE_* bits.</param>
/// <param name="Jobs">Jobs in its queue.</param>
internal sealed record PrinterInfo(string Name, uint Status, uint Attributes, int Jobs);

/// <summary>Everything the Printers panel shows, read together.</summary>
/// <param name="Printers">Local and connected printers.</param>
/// <param name="DefaultName">The current default printer, or null when there is none.</param>
/// <param name="WindowsManagesDefault">True while Windows picks the default printer (the last one used).</param>
/// <param name="ListFailed">True when the spooler could not be read, so <paramref name="Printers"/> is empty for that reason.</param>
internal sealed record PrinterSnapshot(IReadOnlyList<PrinterInfo> Printers, string? DefaultName, bool WindowsManagesDefault, bool ListFailed);

/// <summary>
/// Printers through the spooler (documented winspool API) and the "Let Windows manage my default printer" switch,
/// which is the undocumented-but-stable HKCU value <c>LegacyDefaultPrinterMode</c> (KI-091). Reads can block on a print
/// server, so the panel calls <see cref="Read"/> only through a long-running load.
/// </summary>
internal static class PrinterService
{
    private const string WindowsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Windows";
    private const string ModeValue = "LegacyDefaultPrinterMode";
    private const int ErrorFileNotFound = 2;

    /// <summary>Lists the printers, the default and the default-printer mode. Throws when the spooler can't be read.</summary>
    public static PrinterSnapshot Read()
    {
        var mode = ReadMode();
        try
        {
            return new PrinterSnapshot(EnumeratePrinters(), ReadDefaultName(), mode, ListFailed: false);
        }
        catch (Win32Exception ex)
        {
            // The spooler being stopped or a print server not answering must not hide the switch, which doesn't need it.
            Log.Warn("Printers: could not list the printers", ex);
            return new PrinterSnapshot([], null, mode, ListFailed: true);
        }
    }

    private static bool ReadMode()
    {
        try
        {
            return ReadWindowsManagesDefault(new RegistryStore());
        }
        catch (RegistryAccessException ex)
        {
            Log.Warn("Printers: could not read the default-printer mode", ex);
            return true;
        }
    }

    /// <summary>True when Windows manages the default printer. Windows' own default (no value, or 0) is managed.</summary>
    public static bool ReadWindowsManagesDefault(IRegistryStore registry) =>
        registry.GetValue(WindowsKey, ModeValue) is not { Kind: RegistryValueKind.DWord, Data: 1 };

    /// <summary>Turns "Let Windows manage my default printer" on or off.</summary>
    public static bool WriteWindowsManagesDefault(IRegistryStore registry, bool windowsManages)
    {
        registry.SetValue(WindowsKey, ModeValue, RegistryValue.DWord(windowsManages ? 0 : 1));
        return true;
    }

    /// <summary>
    /// Makes <paramref name="name"/> the default printer. While Windows manages the default it would pick another one
    /// at the next print, so management is switched off first, as Windows Settings does.
    /// </summary>
    public static bool SetDefault(string name)
    {
        var registry = new RegistryStore();
        if (ReadWindowsManagesDefault(registry))
        {
            WriteWindowsManagesDefault(registry, windowsManages: false);
        }

        if (NativeMethods.SetDefaultPrinter(name))
        {
            return true;
        }

        Log.Warn($"Printers: SetDefaultPrinter(\"{name}\") failed with Win32 error {Marshal.GetLastPInvokeError()}");
        return false;
    }

    private static List<PrinterInfo> EnumeratePrinters()
    {
        const uint flags = NativeMethods.PRINTER_ENUM_LOCAL | NativeMethods.PRINTER_ENUM_CONNECTIONS;
        NativeMethods.EnumPrinters(flags, null, 2, 0, 0, out var needed, out _);
        var error = Marshal.GetLastPInvokeError();
        if (needed == 0)
        {
            // No printers is a success with nothing to read; anything else is a spooler failure.
            if (error is 0 or NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            {
                return [];
            }

            throw new Win32Exception(error, $"EnumPrinters failed (Win32 error {error})");
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.EnumPrinters(flags, null, 2, buffer, needed, out _, out var returned))
            {
                error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, $"EnumPrinters failed (Win32 error {error})");
            }

            var size = Marshal.SizeOf<PRINTER_INFO_2>();
            var printers = new List<PrinterInfo>((int)returned);
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buffer + (i * size));
                if (Marshal.PtrToStringUni(info.pPrinterName) is { Length: > 0 } name)
                {
                    printers.Add(new PrinterInfo(name, info.Status, info.Attributes, (int)Math.Min(info.cJobs, int.MaxValue)));
                }
            }

            return printers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ReadDefaultName()
    {
        uint size = 0;
        NativeMethods.GetDefaultPrinter(0, ref size);
        var error = Marshal.GetLastPInvokeError();
        if (size == 0 || error == ErrorFileNotFound)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size * sizeof(char));
        try
        {
            if (NativeMethods.GetDefaultPrinter(buffer, ref size))
            {
                return Marshal.PtrToStringUni(buffer);
            }

            Log.Warn($"Printers: GetDefaultPrinter failed with Win32 error {Marshal.GetLastPInvokeError()}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
