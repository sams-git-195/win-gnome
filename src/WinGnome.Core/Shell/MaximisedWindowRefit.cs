using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>
/// Where an already-maximised window belongs after its monitor's work area was set without a broadcast (KI-103).
/// Windows re-fits maximised windows only while broadcasting <c>SPI_SETWORKAREA</c>, and that broadcast is what has
/// Explorer reset the work area without our strips (KI-102), so WinGnome writes silently and moves the windows itself.
/// Sending the setting change to the window alone does not re-fit it (measured on build 26200); moving it with
/// <c>SetWindowPos</c> keeps it maximised and its restore position.
/// </summary>
public static class MaximisedWindowRefit
{
    /// <summary>
    /// How far past the work area a maximised window's edge may lie and still count as fitted to it: the invisible
    /// resize border, 8 px at 100 % (measured 10 px at 125 %), so 40 px covers 400 % with room to spare.
    /// </summary>
    public const int MaxOverhang = 40;

    /// <summary>
    /// <paramref name="window"/> with every edge that was fitted to <paramref name="oldWork"/> moved by as much as
    /// that edge of the work area moved, or null when nothing changes. An edge is fitted when it lies on the work
    /// area's edge or past it by at most <see cref="MaxOverhang"/>; moving it by the same amount keeps the overhang.
    /// An edge that is not fitted to the old edge (the window was maximised onto something else), or fits the new one at
    /// least as closely (a small move, such as a one-pixel correction, where both fit), stays, so applying the result
    /// twice changes nothing more.
    /// </summary>
    public static PixelRect? Refit(PixelRect window, PixelRect oldWork, PixelRect newWork)
    {
        var fitted = new PixelRect(
            Move(oldWork.Left - window.Left, newWork.Left - window.Left) ? window.Left + (newWork.Left - oldWork.Left) : window.Left,
            Move(oldWork.Top - window.Top, newWork.Top - window.Top) ? window.Top + (newWork.Top - oldWork.Top) : window.Top,
            Move(window.Right - oldWork.Right, window.Right - newWork.Right) ? window.Right + (newWork.Right - oldWork.Right) : window.Right,
            Move(window.Bottom - oldWork.Bottom, window.Bottom - newWork.Bottom) ? window.Bottom + (newWork.Bottom - oldWork.Bottom) : window.Bottom);

        return fitted == window || fitted.IsEmpty ? null : fitted;
    }

    private static bool Move(int oldOverhang, int newOverhang) =>
        Fits(oldOverhang) && (!Fits(newOverhang) || oldOverhang < newOverhang);

    private static bool Fits(int overhang) => overhang is >= 0 and <= MaxOverhang;
}
