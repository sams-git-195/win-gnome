namespace WinGnome.Core.Workspaces;

/// <summary>Number of virtual desktops and which one is current, parsed from Explorer's registry blobs.</summary>
public readonly record struct VirtualDesktopState(int Count, int CurrentIndex)
{
    private const int GuidSize = 16;

    /// <summary>
    /// Parses VirtualDesktopIDs (N consecutive 16-byte GUIDs) and CurrentVirtualDesktop (one GUID).
    /// Missing or malformed data yields a single desktop; an unknown current id yields index 0.
    /// </summary>
    public static VirtualDesktopState Parse(byte[]? desktopIds, byte[]? currentId)
    {
        if (desktopIds is null || desktopIds.Length == 0 || desktopIds.Length % GuidSize != 0)
        {
            return new VirtualDesktopState(1, 0);
        }

        var count = desktopIds.Length / GuidSize;
        var index = 0;
        if (currentId is { Length: GuidSize })
        {
            for (var i = 0; i < count; i++)
            {
                if (desktopIds.AsSpan(i * GuidSize, GuidSize).SequenceEqual(currentId))
                {
                    index = i;
                    break;
                }
            }
        }

        return new VirtualDesktopState(count, index);
    }

    /// <summary>Signed number of desktop switches needed to go from one index to another (positive = right).</summary>
    public static int StepsTo(int from, int to) => to - from;
}
