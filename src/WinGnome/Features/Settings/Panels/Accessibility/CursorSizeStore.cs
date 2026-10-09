using WinGnome.Core.ControlCenter;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Accessibility;

/// <summary>
/// The pointer size, in the undocumented values Windows 11's Mouse pointer page writes (KI-087): the slider step in
/// <c>Software\Microsoft\Accessibility\CursorSize</c> and the size in pixels in
/// <c>Control Panel\Cursors\CursorBaseSize</c>, after which SPI_SETCURSORS reloads the cursors at that size. The only
/// class that touches these values.
/// </summary>
internal sealed class CursorSizeStore(RegistryStore registry)
{
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";
    private const string CursorsKey = @"Control Panel\Cursors";

    /// <summary>The slider step (from CursorSize, else from CursorBaseSize, else 1) and whether WinGnome may change it.</summary>
    public (int Size, bool CanChange) Read()
    {
        var size = ReadDWord(AccessibilityKey, "CursorSize") is { } step
            ? Math.Clamp(step, CursorSizeScale.MinSize, CursorSizeScale.MaxSize)
            : ReadDWord(CursorsKey, "CursorBaseSize") is { } pixels ? CursorSizeScale.FromPixels(pixels) : CursorSizeScale.MinSize;
        var canChange = CursorSizeAvailability.For(ReadDWord(AccessibilityKey, "CursorType"), ReadDWord(CursorsKey, "Scheme Source"));
        return (size, canChange);
    }

    /// <summary>Writes both values and reloads the cursors. Runs on the writer thread; refuses when the pointer style doesn't allow it.</summary>
    public bool Write(int size)
    {
        if (!Read().CanChange)
        {
            return false;
        }

        var step = Math.Clamp(size, CursorSizeScale.MinSize, CursorSizeScale.MaxSize);
        registry.SetValue(AccessibilityKey, "CursorSize", RegistryValue.DWord(step));
        registry.SetValue(CursorsKey, "CursorBaseSize", RegistryValue.DWord(CursorSizeScale.ToPixels(step)));
        return Spi.Set(NativeMethods.SPI_SETCURSORS, 0, 0, "the pointer size");
    }

    private int? ReadDWord(string subKey, string name) =>
        registry.GetValue(subKey, name) is { Kind: RegistryValueKind.DWord, Data: int value } ? value : null;
}
