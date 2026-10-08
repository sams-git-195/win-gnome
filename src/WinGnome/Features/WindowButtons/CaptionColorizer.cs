using System.Collections.Concurrent;
using WinGnome.Core.Theming;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Paints other apps' DWM caption (title bar background and title text) and remembers every window it touched,
/// so the system colours can always be put back: when a window stops being decorated, when the feature is
/// switched off, on shutdown, and from the crash handler.
/// </summary>
/// <remarks>
/// DWM cannot report a window's current caption colour (DwmGetWindowAttribute returns E_INVALIDARG for
/// DWMWA_CAPTION_COLOR), so "restore" means DWMWA_COLOR_DEFAULT: the system-drawn colour.
/// </remarks>
internal sealed class CaptionColorizer
{
    // Window -> caption COLORREF we applied. Concurrent because EmergencyRestore may run on any thread
    // while the UI thread is wedged; enumerating a ConcurrentDictionary never blocks.
    private readonly ConcurrentDictionary<nint, uint> _applied = new();

    /// <summary>Sets the caption and title colours of <paramref name="hwnd"/>; a no-op when already applied.</summary>
    /// <returns>False when DWM refused (the window is gone or does not support caption colours).</returns>
    public bool Apply(nint hwnd, HexColor caption, HexColor text)
    {
        var captionRef = TitleBarPalette.ToColorRef(caption);
        if (_applied.TryGetValue(hwnd, out var current) && current == captionRef)
        {
            return true;
        }

        var textRef = TitleBarPalette.ToColorRef(text);

        // Record the window before touching it so a crash between the two calls still restores it.
        _applied[hwnd] = captionRef;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref captionRef, sizeof(uint));
        if (hr == 0)
        {
            hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref textRef, sizeof(uint));
        }

        if (hr != 0)
        {
            Reset(hwnd);
            _applied.TryRemove(hwnd, out _);
            ThrottledLog.Warn("caption-color", $"DwmSetWindowAttribute(caption colour) failed for 0x{hwnd:X}: 0x{hr:X8}");
            return false;
        }

        return true;
    }

    /// <summary>Puts back the system caption colours of <paramref name="hwnd"/> if this class changed them.</summary>
    public void Restore(nint hwnd)
    {
        if (_applied.TryRemove(hwnd, out _) && NativeMethods.IsWindow(hwnd))
        {
            Reset(hwnd);
        }
    }

    /// <summary>Forgets a destroyed window without calling DWM.</summary>
    public void Forget(nint hwnd) => _applied.TryRemove(hwnd, out _);

    /// <summary>
    /// Restores every recoloured window. Safe from crash handlers on any thread: Win32 calls only, no logging,
    /// no locks, never throws.
    /// </summary>
    /// <returns>How many windows were restored.</returns>
    public int RestoreAll()
    {
        var restored = 0;
        foreach (var hwnd in _applied.Keys)
        {
            if (_applied.TryRemove(hwnd, out _) && NativeMethods.IsWindow(hwnd))
            {
                Reset(hwnd);
                restored++;
            }
        }

        return restored;
    }

    private static void Reset(nint hwnd)
    {
        var systemDefault = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref systemDefault, sizeof(uint));
        systemDefault = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref systemDefault, sizeof(uint));
    }
}
