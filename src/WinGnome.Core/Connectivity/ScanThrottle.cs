namespace WinGnome.Core.Connectivity;

/// <summary>Lets a scan through at most once per interval, so a held Refresh button can't flood the radio.</summary>
public sealed class ScanThrottle(TimeSpan interval)
{
    private DateTimeOffset? _last;

    /// <summary>Five seconds, the Wi-Fi panel's Refresh limit.</summary>
    public ScanThrottle()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>True (and the time is recorded) when a scan may start at <paramref name="now"/>.</summary>
    public bool TryBegin(DateTimeOffset now)
    {
        if (_last is { } last && now - last < interval)
        {
            return false;
        }

        _last = now;
        return true;
    }
}
