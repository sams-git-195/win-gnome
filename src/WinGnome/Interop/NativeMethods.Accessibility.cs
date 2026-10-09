using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>STICKYKEYS: SPI_GETSTICKYKEYS / SPI_SETSTICKYKEYS.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STICKYKEYS
{
    public uint cbSize;
    public uint dwFlags;
}

/// <summary>FILTERKEYS: SPI_GETFILTERKEYS / SPI_SETFILTERKEYS. Slow keys are iWaitMSec, bounce keys iBounceMSec.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FILTERKEYS
{
    public uint cbSize;
    public uint dwFlags;
    public uint iWaitMSec;
    public uint iDelayMSec;
    public uint iRepeatMSec;
    public uint iBounceMSec;
}

/// <summary>
/// HIGHCONTRASTW: SPI_GETHIGHCONTRAST. lpszDefaultScheme points at a string Windows owns. Read only until the spec 0020
/// WP4 high-contrast spike passes (KI-087).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HIGHCONTRASTW
{
    public uint cbSize;
    public uint dwFlags;
    public nint lpszDefaultScheme;
}

/// <summary>SystemParametersInfo declarations for the Accessibility settings panel.</summary>
internal static partial class NativeMethods
{
    public const uint SPI_GETFILTERKEYS = 0x0032;
    public const uint SPI_SETFILTERKEYS = 0x0033;
    public const uint SPI_GETSTICKYKEYS = 0x003A;
    public const uint SPI_SETSTICKYKEYS = 0x003B;
    public const uint SPI_GETHIGHCONTRAST = 0x0042;

    /// <summary>Reloads the system cursors from the registry (Control Panel\Cursors, including CursorBaseSize).</summary>
    public const uint SPI_SETCURSORS = 0x0057;

    public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    public const uint SPI_GETCARETWIDTH = 0x2006;
    public const uint SPI_SETCARETWIDTH = 0x2007;

    /// <summary>HIGHCONTRAST.dwFlags: high contrast is on.</summary>
    public const uint HCF_HIGHCONTRASTON = 0x0001;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, ref STICKYKEYS value, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, ref FILTERKEYS value, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, ref HIGHCONTRASTW value, uint winIni);
}
