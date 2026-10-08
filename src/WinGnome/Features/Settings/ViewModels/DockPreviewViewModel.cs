using System.Windows;
using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One app icon in the dock preview.</summary>
/// <param name="Icon">The app's real icon, when the shell can provide one.</param>
/// <param name="Fallback">Tile colour drawn when there is no icon.</param>
/// <param name="IsRunning">Whether the running indicator is drawn under it.</param>
internal sealed record DockPreviewItem(ImageSource? Icon, Brush Fallback, bool IsRunning);

/// <summary>A scaled-down mock dock that mirrors colour, opacity, corner radius, icon size, spacing and panel mode.</summary>
internal sealed class DockPreviewViewModel : ObservableObject
{
    /// <summary>The preview draws everything at this fraction of its real size so the largest dock still fits.</summary>
    private const double Scale = 0.75;
    private const int MaxItems = 5;

    private static readonly string[] FallbackColors = ["#3584E4", "#E5A50A", "#2EC27E", "#9141AC", "#E66100"];

    private readonly IIconProvider _icons;

    public DockPreviewViewModel(AppSettings settings, IIconProvider icons)
    {
        _icons = icons;
        Background = Brushes.Transparent;
        IndicatorBrush = Brushes.White;
        Items = [];
        Update(settings);
    }

    public Brush Background { get; private set; }

    public Brush IndicatorBrush { get; private set; }

    public IReadOnlyList<DockPreviewItem> Items { get; private set; }

    public double IconSize { get; private set; }

    public Thickness ItemPadding { get; private set; }

    public Thickness Margin { get; private set; }

    public CornerRadius CornerRadius { get; private set; }

    public HorizontalAlignment Alignment { get; private set; }

    public bool ShowIndicators { get; private set; }

    public bool ShowAppsButton { get; private set; }

    /// <summary>Recomputes the preview from the settings and the current theme.</summary>
    public void Update(AppSettings settings)
    {
        var dock = settings.Dock;
        var themeBackground = ColorConversion.Palette("DockBackgroundColor");
        var background = HexColor.TryParse(dock.BackgroundColor, out var custom) ? custom : themeBackground;
        Background = ColorConversion.ToBrush(background, dock.Opacity);

        var accent = ColorConversion.Palette("AccentColor");
        IndicatorBrush = ColorConversion.ToBrush(HexColor.TryParse(dock.IndicatorColor, out var indicator) ? indicator : accent);

        IconSize = dock.IconSize * Scale;
        ItemPadding = new Thickness(dock.IconSpacing * Scale);
        var radius = dock.CornerRadius * Scale;
        CornerRadius = dock.ExtendToEdges ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(radius);
        Margin = new Thickness(0, 0, 0, dock.ExtendToEdges ? 0 : dock.EdgeMargin * Scale);
        Alignment = dock.ExtendToEdges ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        ShowIndicators = dock.ShowRunningIndicators;
        ShowAppsButton = dock.ShowAppsButton;
        Items = BuildItems(dock);
        OnPropertyChanged(string.Empty);
    }

    private List<DockPreviewItem> BuildItems(DockSettings dock)
    {
        var apps = dock.PinnedApps.Take(MaxItems).ToList();
        var count = Math.Max(apps.Count, 3);
        var items = new List<DockPreviewItem>(count);
        for (var i = 0; i < count; i++)
        {
            var icon = i < apps.Count ? _icons.GetAppIcon(apps[i].LaunchId, 64) : null;
            var fallback = ColorConversion.ToBrush(FallbackColors[i % FallbackColors.Length], default);
            items.Add(new DockPreviewItem(icon, fallback, IsRunning: i % 2 == 0));
        }

        return items;
    }
}
