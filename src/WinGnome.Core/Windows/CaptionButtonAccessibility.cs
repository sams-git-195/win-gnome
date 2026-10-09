namespace WinGnome.Core.Windows;

/// <summary>Names and keyboard movement for the traffic-light circles, as exposed to UI Automation and the keyboard.</summary>
public static class CaptionButtonAccessibility
{
    /// <summary>The accessible name of a circle. Maximise reads "Restore" while the window is maximised.</summary>
    public static string Name(CaptionButtonKind kind, bool isMaximized) => kind switch
    {
        CaptionButtonKind.Close => "Close",
        CaptionButtonKind.Minimize => "Minimise",
        _ => isMaximized ? "Restore" : "Maximise",
    };

    /// <summary>A stable automation id (the same in every language and window state).</summary>
    public static string AutomationId(CaptionButtonKind kind) => kind switch
    {
        CaptionButtonKind.Close => "Close",
        CaptionButtonKind.Minimize => "Minimize",
        _ => "Maximize",
    };

    /// <summary>
    /// The index of the next available circle from <paramref name="current"/> in direction <paramref name="step"/>
    /// (+1 right, -1 left), skipping unavailable ones and stopping at the ends (no wrap-around). Pass -1 as
    /// <paramref name="current"/> with step +1 to find the first available circle.
    /// </summary>
    /// <returns>The new index; <paramref name="current"/> when nothing further is available; -1 when none is.</returns>
    public static int MoveFocus(IReadOnlyList<bool> available, int current, int step)
    {
        ArgumentNullException.ThrowIfNull(available);
        var count = available.Count;
        var direction = step < 0 ? -1 : 1;

        // Start one step on, pulled back inside the list (current may be -1, or past the end after a relayout).
        var i = Math.Clamp(current + direction, 0, Math.Max(0, count - 1));
        for (; i >= 0 && i < count; i += direction)
        {
            if (available[i] && i != current)
            {
                return i;
            }
        }

        if (current >= 0 && current < count && available[current])
        {
            return current;
        }

        for (i = 0; i < count; i++)
        {
            if (available[i])
            {
                return i;
            }
        }

        return -1;
    }
}
