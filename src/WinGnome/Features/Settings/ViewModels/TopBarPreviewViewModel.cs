using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.TopBar;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Features.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>A mock top bar that mirrors the colour, text, opacity, size and layout settings.</summary>
internal sealed class TopBarPreviewViewModel : ObservableObject
{
    /// <summary>Same alpha as the bar's own hover highlight (0x26).</summary>
    private const double HoverOpacity = 0x26 / 255.0;

    private static readonly DateTime SampleTime = new(2026, 10, 8, 14, 5, 0, DateTimeKind.Unspecified);

    public TopBarPreviewViewModel(AppSettings settings)
    {
        Background = Brushes.Black;
        Foreground = Brushes.White;
        ItemHover = Brushes.Transparent;
        FontFamily = TopBarFonts.For(settings.TopBar.FontFamily);
        ClockText = "";
        Update(settings);
    }

    public Brush Background { get; private set; }

    public Brush Foreground { get; private set; }

    public double Height { get; private set; }

    public double FontSize { get; private set; }

    public FontFamily FontFamily { get; private set; }

    /// <summary>Status icon size in DIPs, in the same ratio to the text as on the bar.</summary>
    public double IconSize { get; private set; }

    public Thickness Margin { get; private set; }

    public double CornerRadius { get; private set; }

    /// <summary>Hover highlight of bar items (the preview shows Activities hovered).</summary>
    public Brush ItemHover { get; private set; }

    public double ItemCornerRadius { get; private set; }

    public string ClockText { get; private set; }

    public bool ShowLogo { get; private set; }

    public bool ShowActivities { get; private set; }

    public bool ShowWorkspaces { get; private set; }

    public bool ShowAppName { get; private set; }

    public bool ShowBatteryText { get; private set; }

    /// <summary>The logo mark the preview's <c>LogoGlyph</c> draws; set by the page as the selection resolves.</summary>
    public LogoKind LogoKind { get; private set; } = LogoKind.WindowsMark;

    public string? LogoGeometryKey { get; private set; }

    public BitmapSource? LogoMask { get; private set; }

    /// <summary>Recomputes the preview from the settings.</summary>
    public void Update(AppSettings settings)
    {
        var bar = settings.TopBar;
        Background = ColorConversion.ToBrush(bar.BackgroundColor, HexColor.FromRgb(0, 0, 0), bar.Opacity);
        Foreground = ColorConversion.ToBrush(bar.ForegroundColor, HexColor.FromRgb(255, 255, 255));
        Height = bar.Height;
        FontSize = bar.FontSize;
        FontFamily = TopBarFonts.For(bar.FontFamily);
        IconSize = BarMetrics.SymbolicIconPx(bar.FontSize, 1);
        Margin = new Thickness(bar.Margin);
        CornerRadius = bar.CornerRadius;
        ItemHover = ColorConversion.ToBrush(bar.ForegroundColor, HexColor.FromRgb(255, 255, 255), HoverOpacity);
        ItemCornerRadius = bar.ItemCornerRadius;
        ClockText = ClockFormatter.Format(SampleTime, bar, CultureInfo.CurrentCulture);
        ShowLogo = bar.ShowLogoMenu;
        ShowActivities = bar.ShowActivitiesButton;
        ShowWorkspaces = bar.ShowWorkspaceIndicator;
        ShowAppName = bar.ShowFocusedAppName;
        ShowBatteryText = bar.ShowBatteryPercentage;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Points the preview's logo glyph at a resolved mark (and its mask, for a custom image).</summary>
    public void SetLogo(LogoKind kind, string? geometryKey, BitmapSource? mask)
    {
        LogoKind = kind;
        LogoGeometryKey = geometryKey;
        LogoMask = mask;
        OnPropertyChanged(nameof(LogoKind));
        OnPropertyChanged(nameof(LogoGeometryKey));
        OnPropertyChanged(nameof(LogoMask));
    }
}
