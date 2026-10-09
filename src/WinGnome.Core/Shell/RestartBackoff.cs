namespace WinGnome.Core.Shell;

/// <summary>How long the bootstrap waits before restarting a crashed shell, and when it gives up.</summary>
public static class RestartBackoff
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
    ];

    /// <summary>Restarts allowed before the bootstrap falls back to Explorer.</summary>
    public static int MaxRestarts => Delays.Length;

    /// <summary>
    /// The delay before the restart after <paramref name="restartsSoFar"/> earlier ones (0 = the first restart).
    /// False once the schedule is exhausted, or for a negative count.
    /// </summary>
    public static bool TryGetDelay(int restartsSoFar, out TimeSpan delay)
    {
        if (restartsSoFar < 0 || restartsSoFar >= Delays.Length)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        delay = Delays[restartsSoFar];
        return true;
    }
}
