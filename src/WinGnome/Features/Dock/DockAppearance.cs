using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Dock;

/// <summary>
/// Size and orientation values shared by every dock item template (all in DIPs). Each entry view model links
/// to the one instance, so a settings change restyles all items through a single notification.
/// </summary>
internal sealed class DockAppearance : ObservableObject
{
    /// <summary>Length along the dock of the separator between pinned and running apps.</summary>
    public const double SeparatorLength = 13;

    /// <summary>Diameter of a running-indicator dot (matches the template).</summary>
    private const double IndicatorDotSize = 4;
    private const double ToolTipGap = 6;

    public DockPosition Position { get; private set; } = DockPosition.Bottom;

    public bool IsVertical => Position != DockPosition.Bottom;

    public double IconSize { get; private set; } = 48;

    /// <summary>Icon plus padding on both sides; the slot every icon sits in.</summary>
    public double CellSize { get; private set; } = 60;

    public double FallbackFontSize => Math.Max(10, IconSize * 0.45);

    public CornerRadius HighlightRadius { get; private set; } = new(12);

    public CornerRadius IconTileRadius => new(IconSize * 0.22);

    public double SeparatorSlotWidth => IsVertical ? CellSize : SeparatorLength;

    public double SeparatorSlotHeight => IsVertical ? SeparatorLength : CellSize;

    public double SeparatorLineWidth => IsVertical ? IconSize * 0.6 : 1;

    public double SeparatorLineHeight => IsVertical ? 1 : IconSize * 0.6;

    /// <summary>Size of the 3x3 "Show Applications" glyph.</summary>
    public double GlyphSize => IconSize * 0.5;

    public Visibility IndicatorVisibility { get; private set; } = Visibility.Visible;

    // Running dots sit on the screen-edge side of the icon, centred in the padding strip.
    public HorizontalAlignment IndicatorHorizontalAlignment => Position switch
    {
        DockPosition.Left => HorizontalAlignment.Left,
        DockPosition.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    public VerticalAlignment IndicatorVerticalAlignment => IsVertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;

    public Orientation IndicatorOrientation => IsVertical ? Orientation.Vertical : Orientation.Horizontal;

    public Thickness IndicatorMargin
    {
        get
        {
            var inset = Math.Max(1, ((CellSize - IconSize) / 2 - IndicatorDotSize) / 2);
            return Position switch
            {
                DockPosition.Left => new Thickness(inset, 0, 0, 0),
                DockPosition.Right => new Thickness(0, 0, inset, 0),
                _ => new Thickness(0, 0, 0, inset),
            };
        }
    }

    // Tooltips open on the side facing away from the screen edge.
    public PlacementMode ToolTipPlacement => Position switch
    {
        DockPosition.Left => PlacementMode.Right,
        DockPosition.Right => PlacementMode.Left,
        _ => PlacementMode.Top,
    };

    public double ToolTipHorizontalOffset => Position switch
    {
        DockPosition.Left => ToolTipGap,
        DockPosition.Right => -ToolTipGap,
        _ => 0,
    };

    public double ToolTipVerticalOffset => IsVertical ? 0 : -ToolTipGap;

    /// <summary>Updates every value and refreshes all bindings with one notification.</summary>
    public void Apply(DockSettings settings, double iconSize, double bodyCornerRadius)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Position = settings.Position;
        IconSize = iconSize;
        CellSize = iconSize + (2 * settings.IconSpacing);
        HighlightRadius = new CornerRadius(Math.Clamp(bodyCornerRadius - 4, 6, CellSize / 2));
        IndicatorVisibility = settings.ShowRunningIndicators ? Visibility.Visible : Visibility.Collapsed;

        // string.Empty tells WPF that every property changed.
        OnPropertyChanged(string.Empty);
    }
}
