namespace WinGnome.Core.Windows;

/// <summary>
/// When to ask a window again where its own caption buttons are (<see cref="OwnCaptionButtons"/>) after an
/// answer that showed none. An app such as Steam creates its window, and is decorated, before its web UI has
/// drawn its buttons (after being restored from the tray, for one), so the first answer is often empty.
/// </summary>
public static class OwnButtonsRetry
{
    private static readonly int[] DelaysMs = [1_000, 3_000, 10_000, 30_000];

    /// <summary>Number of retries after the first probe.</summary>
    public static int MaxRetries => DelaysMs.Length;

    /// <summary>
    /// Milliseconds to wait before retry number <paramref name="retry"/> (0 for the first retry), or null when
    /// the window has been asked often enough and is left with DWM's rectangle.
    /// </summary>
    public static int? DelayMs(int retry) => retry >= 0 && retry < DelaysMs.Length ? DelaysMs[retry] : null;
}
