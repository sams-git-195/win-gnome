using System.ComponentModel;
using System.Runtime.InteropServices;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Accessibility;

/// <summary>What Windows reports for the Accessibility panel.</summary>
/// <param name="StickyKeysFlags">STICKYKEYS.dwFlags.</param>
/// <param name="FilterKeys">FILTERKEYS (slow and bounce keys).</param>
/// <param name="CursorSize">The pointer size step, 1..15.</param>
/// <param name="CanChangeCursorSize">False for coloured pointers and the user's own cursor scheme.</param>
/// <param name="CaretWidth">The text cursor thickness in pixels.</param>
/// <param name="ClientAreaAnimation">Windows' "Animation effects".</param>
/// <param name="HighContrastOn">High contrast is on.</param>
/// <param name="HighContrastScheme">The scheme Windows reports (also kept while high contrast is off).</param>
/// <param name="TextScalePercent">Windows' text size, 100..225.</param>
internal sealed record AccessibilityState(
    uint StickyKeysFlags,
    FilterKeysState FilterKeys,
    int CursorSize,
    bool CanChangeCursorSize,
    int CaretWidth,
    bool ClientAreaAnimation,
    bool HighContrastOn,
    string HighContrastScheme,
    int TextScalePercent);

/// <summary>
/// Reads and writes the Accessibility panel's Windows settings. Writes are read-modify-write from a fresh read, so
/// flags WinGnome doesn't show (the keyboard shortcuts, sounds, the indicator) are kept as Windows has them. Every
/// write persists and broadcasts (SPIF_SENDCHANGE), so it runs on the settings writer thread, never the UI thread.
/// </summary>
internal sealed class AccessibilityService
{
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";

    /// <summary>Write and persist for the user, and tell running apps (as Windows Settings does).</summary>
    private const uint PersistAndNotify = NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE;

    private readonly RegistryStore _registry = new();
    private readonly CursorSizeStore _cursors;

    public AccessibilityService() => _cursors = new CursorSizeStore(_registry);

    /// <summary>Reads everything the panel shows. Throws when a structure Windows always has can't be read.</summary>
    public AccessibilityState Read()
    {
        var sticky = ReadStickyKeys() ?? throw new Win32Exception("Could not read the sticky keys settings.");
        var filter = ReadFilterKeys() ?? throw new Win32Exception("Could not read the filter keys settings.");
        var contrast = ReadHighContrast() ?? throw new Win32Exception("Could not read the high contrast settings.");
        var (cursorSize, canChangeCursor) = _cursors.Read();
        return new AccessibilityState(
            sticky.dwFlags,
            ToState(filter),
            cursorSize,
            canChangeCursor,
            Spi.Get(NativeMethods.SPI_GETCARETWIDTH, 1, "the text cursor thickness"),
            Spi.Get(NativeMethods.SPI_GETCLIENTAREAANIMATION, 1, "animation effects") != 0,
            (contrast.Flags & NativeMethods.HCF_HIGHCONTRASTON) != 0,
            contrast.Scheme,
            ReadDWord(AccessibilityKey, "TextScaleFactor") ?? 100);
    }

    public static bool SetStickyKeys(bool on)
    {
        if (ReadStickyKeys() is not { } current)
        {
            return false;
        }

        current.dwFlags = StickyKeysFlags.With(current.dwFlags, on);
        return Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETSTICKYKEYS, current.cbSize, ref current, PersistAndNotify),
            NativeMethods.SPI_SETSTICKYKEYS, "sticky keys");
    }

    public static bool SetSlowKeys(bool on) => WriteFilterKeys(current => FilterKeysPlan.ForSlowKeys(current, on));

    public static bool SetBounceKeys(bool on) => WriteFilterKeys(current => FilterKeysPlan.ForBounceKeys(current, on));

    public static bool SetCaretWidth(int width) =>
        Spi.Set(NativeMethods.SPI_SETCARETWIDTH, 0, CaretWidth.Clamp(width), "the text cursor thickness");

    public static bool SetReduceAnimation(bool reduce) =>
        Spi.Set(NativeMethods.SPI_SETCLIENTAREAANIMATION, 0, reduce ? 0 : 1, "animation effects");

    public bool SetCursorSize(int size) => _cursors.Write(size);

    /// <summary>
    /// Turns high contrast on with <paramref name="theme"/>, or off for null (Windows then restores the previous theme).
    /// Windows shows its own "Please wait" and the call returns once the switch is done, several seconds later.
    /// </summary>
    public static bool SetHighContrast(ContrastTheme? theme)
    {
        if (ReadHighContrast() is not { } current)
        {
            return false;
        }

        var flags = theme is null ? current.Flags & ~NativeMethods.HCF_HIGHCONTRASTON : current.Flags | NativeMethods.HCF_HIGHCONTRASTON;
        var scheme = Marshal.StringToHGlobalUni(theme?.Scheme ?? current.Scheme);
        try
        {
            var value = new HIGHCONTRASTW { cbSize = (uint)Marshal.SizeOf<HIGHCONTRASTW>(), dwFlags = flags, lpszDefaultScheme = scheme };
            return Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETHIGHCONTRAST, value.cbSize, ref value, PersistAndNotify),
                NativeMethods.SPI_SETHIGHCONTRAST, "high contrast");
        }
        finally
        {
            Marshal.FreeHGlobal(scheme);
        }
    }

    private static bool WriteFilterKeys(Func<FilterKeysState, FilterKeysState> plan)
    {
        if (ReadFilterKeys() is not { } current)
        {
            return false;
        }

        var next = plan(ToState(current));
        current.dwFlags = next.Flags;
        current.iWaitMSec = (uint)next.WaitMs;
        current.iDelayMSec = (uint)next.DelayMs;
        current.iRepeatMSec = (uint)next.RepeatMs;
        current.iBounceMSec = (uint)next.BounceMs;
        return Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETFILTERKEYS, current.cbSize, ref current, PersistAndNotify),
            NativeMethods.SPI_SETFILTERKEYS, "filter keys");
    }

    private static STICKYKEYS? ReadStickyKeys()
    {
        var value = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
        return Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSTICKYKEYS, value.cbSize, ref value, 0),
            NativeMethods.SPI_GETSTICKYKEYS, "sticky keys") ? value : null;
    }

    private static FILTERKEYS? ReadFilterKeys()
    {
        var value = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
        return Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETFILTERKEYS, value.cbSize, ref value, 0),
            NativeMethods.SPI_GETFILTERKEYS, "filter keys") ? value : null;
    }

    /// <summary>The high contrast flags and scheme name, copied out of the string Windows owns.</summary>
    private static (uint Flags, string Scheme)? ReadHighContrast()
    {
        var value = new HIGHCONTRASTW { cbSize = (uint)Marshal.SizeOf<HIGHCONTRASTW>() };
        if (!Check(NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST, value.cbSize, ref value, 0),
                NativeMethods.SPI_GETHIGHCONTRAST, "high contrast"))
        {
            return null;
        }

        return (value.dwFlags, value.lpszDefaultScheme == 0 ? "" : Marshal.PtrToStringUni(value.lpszDefaultScheme) ?? "");
    }

    // FILTERKEYS timings are DWORDs; anything past int range is nonsense and treated as the largest value.
    private static FilterKeysState ToState(FILTERKEYS value) =>
        new(value.dwFlags, ToInt(value.iWaitMSec), ToInt(value.iDelayMSec), ToInt(value.iRepeatMSec), ToInt(value.iBounceMSec));

    private static int ToInt(uint value) => (int)Math.Min(value, int.MaxValue);

    private int? ReadDWord(string subKey, string name) =>
        _registry.GetValue(subKey, name) is { Kind: RegistryValueKind.DWord, Data: int value } ? value : null;

    private static bool Check(bool succeeded, uint action, string what)
    {
        if (!succeeded)
        {
            Log.Warn($"Accessibility: SystemParametersInfo 0x{action:X} ({what}) failed with error {Marshal.GetLastPInvokeError()}");
        }

        return succeeded;
    }
}
