using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>What Windows Update's cached state says, as read by the Windows Update panel.</summary>
/// <param name="LastChecked">When updates were last searched for successfully (local time), or null if never or unknown.</param>
/// <param name="LastInstalled">When an update was last installed successfully (local time), or null.</param>
/// <param name="PendingTitles">Titles of the updates found and not yet installed or hidden.</param>
/// <param name="RebootRequired">True when an installed update needs a restart to finish.</param>
/// <param name="ErrorCode">The HRESULT of what went wrong reading the status, or null when it was read.</param>
public sealed record UpdateStatus(
    DateTime? LastChecked,
    DateTime? LastInstalled,
    IReadOnlyList<string> PendingTitles,
    bool RebootRequired,
    int? ErrorCode);

/// <summary>The panel's wording of an <see cref="UpdateStatus"/>.</summary>
/// <param name="LastChecked">E.g. "Today at 14:05", "3 days ago" or "Never".</param>
/// <param name="LastInstalled">Same style as <paramref name="LastChecked"/>.</param>
/// <param name="Pending">E.g. "No updates waiting" or "3 updates waiting".</param>
/// <param name="PendingTitles">The titles to list under <paramref name="Pending"/>.</param>
/// <param name="Restart">"A restart is needed to finish updating" or "No restart needed".</param>
/// <param name="Error">What went wrong, or null when the status was read.</param>
public sealed record UpdateStatusLines(
    string LastChecked,
    string LastInstalled,
    string Pending,
    IReadOnlyList<string> PendingTitles,
    string Restart,
    string? Error);

/// <summary>Turns Windows Update state into the words the Windows Update panel shows.</summary>
public static class UpdateStatusText
{
    /// <summary>The HRESULT used when the search was cut off because Windows Update didn't answer in time.</summary>
    public const int TimedOut = unchecked((int)0x800705B4);

    /// <summary>Dates before this are Windows' "never" (a zero OLE date reads as 1899-12-30).</summary>
    private static readonly DateTime EarliestRealDate = new(1980, 1, 1);

    public static UpdateStatusLines Build(UpdateStatus status, DateTime now, CultureInfo culture)
    {
        var count = status.PendingTitles.Count;
        return new UpdateStatusLines(
            When(status.LastChecked, now, culture),
            When(status.LastInstalled, now, culture),
            status.ErrorCode is not null ? "Unknown" : count switch
            {
                0 => "No updates waiting",
                1 => "1 update waiting",
                _ => string.Create(culture, $"{count} updates waiting"),
            },
            status.PendingTitles,
            status.RebootRequired ? "A restart is needed to finish updating" : "No restart needed",
            status.ErrorCode is { } code ? ErrorMessage(code) : null);
    }

    /// <summary>"Today at 14:05", "Yesterday at 09:30", "3 days ago", or the date from 7 days on. Null or a zero date is "Never".</summary>
    public static string When(DateTime? time, DateTime now, CultureInfo culture)
    {
        if (time is not { } value || value < EarliestRealDate)
        {
            return "Never";
        }

        var days = (now.Date - value.Date).Days;
        return days switch
        {
            < 0 => value.ToString("g", culture),
            0 => $"Today at {value.ToString("t", culture)}",
            1 => $"Yesterday at {value.ToString("t", culture)}",
            < 7 => string.Create(culture, $"{days} days ago"),
            _ => value.ToString("d", culture),
        };
    }

    /// <summary>A sentence for a Windows Update or COM failure, ending with the code so it can be looked up.</summary>
    public static string ErrorMessage(int hresult)
    {
        var code = $"0x{(uint)hresult:X8}";
        return (uint)hresult switch
        {
            0x80070422 => $"The Windows Update service is turned off ({code}).",
            0x80070005 => $"Windows doesn't let this account read update information ({code}).",
            0x800705B4 => "Windows Update didn't answer in time.",
            0x8024402C or 0x80244010 or 0x8024401C or 0x80240438 or 0x80072EE2 or 0x80072EE7 => $"Windows Update can't be reached ({code}).",
            >= 0x80240000 and <= 0x8024FFFF => $"Windows Update reported a problem ({code}).",
            _ => $"Couldn't read the update status ({code}).",
        };
    }
}
