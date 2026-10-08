using System.Windows;
using System.Windows.Interop;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Views;

/// <summary>Makes a window's system title bar follow the light or dark theme.</summary>
internal static class TitleBarTheme
{
    /// <summary>Applies the immersive dark mode attribute. Needs the window handle, so call it from <c>SourceInitialized</c> or later.</summary>
    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return;
        }

        var value = dark ? 1 : 0;
        var result = NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        if (result != 0)
        {
            Log.Warn($"Could not set the title bar theme (HRESULT 0x{result:X8})");
        }
    }
}
