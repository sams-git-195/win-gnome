namespace WinGnome.Core.Theming;

/// <summary>Title bar colours used behind the traffic-light buttons, and COLORREF conversions for DWM and GDI.</summary>
public static class TitleBarPalette
{
    private static readonly HexColor DarkHeader = HexColor.FromRgb(0x30, 0x30, 0x30);
    private static readonly HexColor LightHeader = HexColor.FromRgb(0xEB, 0xEB, 0xEB);
    private static readonly HexColor DarkText = HexColor.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly HexColor LightText = HexColor.FromRgb(0x2E, 0x34, 0x36);
    private static readonly HexColor DarkWindowsCaption = HexColor.FromRgb(0x20, 0x20, 0x20);
    private static readonly HexColor LightWindowsCaption = HexColor.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>Adwaita header bar background used when title bars are unified.</summary>
    public static HexColor Header(bool dark) => dark ? DarkHeader : LightHeader;

    /// <summary>Adwaita header bar title colour used when title bars are unified.</summary>
    public static HexColor Text(bool dark) => dark ? DarkText : LightText;

    /// <summary>Windows 11's typical caption colour: a placeholder until the real title bar has been sampled.</summary>
    public static HexColor WindowsDefault(bool dark) => dark ? DarkWindowsCaption : LightWindowsCaption;

    /// <summary>Win32 COLORREF (0x00BBGGRR). Alpha is dropped because DWM and GDI colours are opaque.</summary>
    public static uint ToColorRef(HexColor color) => ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

    /// <summary>Opaque colour from a Win32 COLORREF (0x00BBGGRR).</summary>
    public static HexColor FromColorRef(uint colorRef) =>
        HexColor.FromRgb((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
}
