namespace WinGnome.Core.TopBar;

/// <summary>
/// How a tray icon bitmap is drawn inside its square slot on the bar, in device pixels. Bitmaps are never resampled
/// by a non-integer factor upwards (a 16 px icon stretched to 20 px blurs every edge): a smaller icon is drawn 1:1
/// and centred, or scaled up by a whole factor when the slot holds one; a larger icon scales down to fill the slot.
/// </summary>
/// <param name="SizePx">Edge length the icon is drawn at.</param>
/// <param name="OffsetPx">Gap from the slot's left and top edges to the icon (the slot is square).</param>
public readonly record struct TrayIconPlacement(int SizePx, int OffsetPx)
{
    /// <param name="sourcePx">The icon bitmap's edge length; 0 or less when unknown.</param>
    /// <param name="slotPx">The slot's edge length.</param>
    public static TrayIconPlacement Choose(int sourcePx, int slotPx)
    {
        if (slotPx <= 0)
        {
            return new TrayIconPlacement(0, 0);
        }

        if (sourcePx <= 0 || sourcePx >= slotPx)
        {
            return new TrayIconPlacement(slotPx, 0);
        }

        var size = sourcePx * (slotPx / sourcePx);
        return new TrayIconPlacement(size, (slotPx - size) / 2);
    }
}
