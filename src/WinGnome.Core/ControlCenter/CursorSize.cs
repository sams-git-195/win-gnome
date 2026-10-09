namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Windows 11's pointer size slider (1..15) and the cursor size in pixels it stores beside it
/// (<c>Control Panel\Cursors\CursorBaseSize</c>: 32 at size 1, 16 more per step, 256 at size 15).
/// </summary>
public static class CursorSizeScale
{
    public const int MinSize = 1;

    public const int MaxSize = 15;

    private const int BasePixels = 32;
    private const int PixelsPerStep = 16;

    /// <summary>The cursor size in pixels for slider step <paramref name="size"/> (clamped to 1..15).</summary>
    public static int ToPixels(int size) => BasePixels + (PixelsPerStep * (Math.Clamp(size, MinSize, MaxSize) - 1));

    /// <summary>The nearest slider step for a cursor size in pixels (clamped to 1..15).</summary>
    public static int FromPixels(int pixels) =>
        Math.Clamp((int)Math.Round((pixels - BasePixels) / (double)PixelsPerStep, MidpointRounding.AwayFromZero) + 1, MinSize, MaxSize);
}

/// <summary>
/// Whether WinGnome can resize the pointer itself. Black, inverted and custom-colour pointers are cursor files Windows
/// regenerates through a private API, and a user's own scheme has its own files; for those the size is changed in
/// Windows Settings.
/// </summary>
public static class CursorSizeAvailability
{
    /// <summary><c>Control Panel\Cursors\Scheme Source</c> for a scheme the user saved.</summary>
    private const int UserScheme = 1;

    /// <summary>
    /// True for the white pointer style (<c>Software\Microsoft\Accessibility\CursorType</c> 0 or absent) with Windows'
    /// own scheme (<c>Scheme Source</c> anything but 1, or absent).
    /// </summary>
    public static bool For(int? cursorType, int? schemeSource) =>
        cursorType is null or 0 && schemeSource is not UserScheme;
}
