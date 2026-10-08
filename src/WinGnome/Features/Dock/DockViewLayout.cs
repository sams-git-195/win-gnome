using System.Windows.Input;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;

namespace WinGnome.Features.Dock;

/// <summary>Geometry handed to <see cref="DockWindow.ApplyLayout"/>. Window bounds in physical pixels, the rest in DIPs.</summary>
/// <param name="WindowBounds">Dock window bounds (body, gap to the edge and magnification headroom).</param>
/// <param name="Position">Screen edge.</param>
/// <param name="ExtendToEdges">Panel mode: the body spans the whole edge.</param>
/// <param name="EdgeGap">Gap between the body and the screen edge.</param>
/// <param name="BodyThickness">Body size across the dock.</param>
/// <param name="EndPadding">Padding at both ends of the body.</param>
/// <param name="CornerRadius">Requested corner radius (clamped to half the thickness).</param>
/// <param name="Magnification">Maximum hover magnification.</param>
/// <param name="IconSize">Effective icon size.</param>
internal sealed record DockViewLayout(
    PixelRect WindowBounds,
    DockPosition Position,
    bool ExtendToEdges,
    double EdgeGap,
    double BodyThickness,
    double EndPadding,
    double CornerRadius,
    double Magnification,
    double IconSize);

/// <summary>Colours and material handed to <see cref="DockWindow.ApplyStyle"/>.</summary>
/// <param name="Background">Body colour, or null to follow the theme.</param>
/// <param name="Opacity">Background opacity 0..1 (the tint strength when blurred).</param>
/// <param name="Blur">Blur or acrylic behind the body.</param>
/// <param name="Indicator">Running-dot colour, or null for the accent colour.</param>
internal sealed record DockStyle(HexColor? Background, double Opacity, BlurEffect Blur, HexColor? Indicator);

/// <summary>A click on a dock entry.</summary>
internal sealed class DockEntryEventArgs(DockEntry entry, MouseButton button) : EventArgs
{
    public DockEntry Entry { get; } = entry;

    public MouseButton Button { get; } = button;
}

/// <summary>A right-click on a dock entry; <see cref="Target"/> is the item's container (for menu placement).</summary>
internal sealed class DockMenuRequestEventArgs(DockEntry entry, System.Windows.FrameworkElement target) : EventArgs
{
    public DockEntry Entry { get; } = entry;

    public System.Windows.FrameworkElement Target { get; } = target;
}

/// <summary>Files dropped on the dock, to be pinned at <see cref="PinIndex"/> (an index into the pinned list).</summary>
internal sealed class DockFilesDroppedEventArgs(IReadOnlyList<string> paths, int pinIndex) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths;

    public int PinIndex { get; } = pinIndex;
}
