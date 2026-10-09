namespace WinGnome.Core.ControlCenter;

/// <summary>The date and time patterns a Region &amp; Language drop-down offers.</summary>
public static class RegionFormatChoices
{
    /// <summary>
    /// The patterns in the order the locale lists them, without blanks or repeats (compared exactly: "mm" and "MM" are
    /// different pattern letters). When <paramref name="current"/> is not among them, for example a custom format set in
    /// the Control Panel, it is placed first so the drop-down can still show what Windows holds.
    /// </summary>
    public static IReadOnlyList<string> Build(IEnumerable<string> patterns, string? current)
    {
        var choices = new List<string>();
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrEmpty(pattern) && !choices.Contains(pattern, StringComparer.Ordinal))
            {
                choices.Add(pattern);
            }
        }

        if (!string.IsNullOrEmpty(current) && !choices.Contains(current, StringComparer.Ordinal))
        {
            choices.Insert(0, current);
        }

        return choices;
    }
}
