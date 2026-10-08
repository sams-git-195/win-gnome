using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;
using WinGnome.Theme;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>Dock: placement and behaviour, appearance (colours, opacity, blur, sizes) and the pinned apps.</summary>
internal sealed class DockPageViewModel : SettingsPageViewModel
{
    private readonly ThemeManager _theme;

    public DockPageViewModel(SettingsService settings, ThemeManager theme, IIconProvider icons, IDialogService dialogs)
        : base(settings, "Dock", "")
    {
        _theme = theme;
        Preview = new DockPreviewViewModel(settings.Current, icons);
        PinnedApps = new PinnedAppsViewModel(settings, icons, dialogs);
        theme.ThemeChanged += OnThemeChanged;

        Enabled = Toggle(s => s.Dock.Enabled, (s, v) => s.Dock.Enabled = v);
        Position = Choice(s => s.Dock.Position, (s, v) => s.Dock.Position = v,
            ChoiceOption.Of(DockPosition.Bottom, "Bottom"),
            ChoiceOption.Of(DockPosition.Left, "Left"),
            ChoiceOption.Of(DockPosition.Right, "Right"));
        Visibility = Choice(s => s.Dock.Visibility, (s, v) => s.Dock.Visibility = v,
            ChoiceOption.Of(DockVisibility.AlwaysVisible, "Always visible", "The dock stays on screen and reserves space, so maximised windows stop above it."),
            ChoiceOption.Of(DockVisibility.Intellihide, "Intellihide", "The dock hides only while a window overlaps it, and returns when the pointer touches the screen edge."),
            ChoiceOption.Of(DockVisibility.Autohide, "Autohide", "The dock stays hidden until the pointer touches the screen edge."));
        ClickAction = Choice(s => s.Dock.ClickAction, (s, v) => s.Dock.ClickAction = v,
            ChoiceOption.Of(DockClickAction.FocusOrMinimize, "Focus or minimise", "Focuses the app; clicking an already focused app minimises it."),
            ChoiceOption.Of(DockClickAction.Cycle, "Cycle windows", "Focuses the app; clicking again steps through its windows."),
            ChoiceOption.Of(DockClickAction.Previews, "Show previews", "Opens the overview with only the app's windows when it has several."));

        ShowRunningApps = Toggle(s => s.Dock.ShowRunningApps, (s, v) => s.Dock.ShowRunningApps = v);
        ShowRunningIndicators = Toggle(s => s.Dock.ShowRunningIndicators, (s, v) => s.Dock.ShowRunningIndicators = v);
        ShowAppsButton = Toggle(s => s.Dock.ShowAppsButton, (s, v) => s.Dock.ShowAppsButton = v);
        ShowRecycleBin = Toggle(s => s.Dock.ShowRecycleBin, (s, v) => s.Dock.ShowRecycleBin = v);

        Background = Color(s => s.Dock.BackgroundColor, (s, v) => s.Dock.BackgroundColor = v, ColorPresets.DockBackgrounds, isOptional: true);
        Opacity = Slider(s => s.Dock.Opacity, (s, v) => s.Dock.Opacity = v, 0, 1, 0.01, SliderSetting.Percent);
        Blur = Choice(s => s.Dock.Blur, (s, v) => s.Dock.Blur = v,
            ChoiceOption.Of(BlurEffect.None, "None"),
            ChoiceOption.Of(BlurEffect.Blur, "Blur"),
            ChoiceOption.Of(BlurEffect.Acrylic, "Acrylic"));
        IconSize = Slider(s => s.Dock.IconSize, (s, v) => s.Dock.IconSize = v, 24, 128, 1, SliderSetting.Pixels);
        IconSpacing = Slider(s => s.Dock.IconSpacing, (s, v) => s.Dock.IconSpacing = v, 0, 24, 1, SliderSetting.Pixels);
        EdgeMargin = Slider(s => s.Dock.EdgeMargin, (s, v) => s.Dock.EdgeMargin = v, 0, 48, 1, SliderSetting.Pixels);
        CornerRadius = Slider(s => s.Dock.CornerRadius, (s, v) => s.Dock.CornerRadius = v, 0, 40, 1, SliderSetting.Pixels);
        Magnification = Slider(s => s.Dock.Magnification, (s, v) => s.Dock.Magnification = v, 1, 2, 0.05, FormatMagnification);
        IndicatorColor = Color(s => s.Dock.IndicatorColor, (s, v) => s.Dock.IndicatorColor = v, ColorPresets.Accents, isOptional: true);
        PanelMode = Toggle(s => s.Dock.ExtendToEdges, (s, v) => s.Dock.ExtendToEdges = v);
    }

    public DockPreviewViewModel Preview { get; }

    public PinnedAppsViewModel PinnedApps { get; }

    public ToggleSetting Enabled { get; }

    public ChoiceSetting Position { get; }

    public ChoiceSetting Visibility { get; }

    public ChoiceSetting ClickAction { get; }

    public ToggleSetting ShowRunningApps { get; }

    public ToggleSetting ShowRunningIndicators { get; }

    public ToggleSetting ShowAppsButton { get; }

    public ToggleSetting ShowRecycleBin { get; }

    public ColorEditor Background { get; }

    public SliderSetting Opacity { get; }

    public ChoiceSetting Blur { get; }

    public SliderSetting IconSize { get; }

    public SliderSetting IconSpacing { get; }

    public SliderSetting EdgeMargin { get; }

    public SliderSetting CornerRadius { get; }

    public SliderSetting Magnification { get; }

    public ColorEditor IndicatorColor { get; }

    public ToggleSetting PanelMode { get; }

    public override void Dispose()
    {
        _theme.ThemeChanged -= OnThemeChanged;
        PinnedApps.Dispose();
        base.Dispose();
    }

    protected override void OnSettingsApplied(AppSettings settings) => Preview.Update(settings);

    private static string FormatMagnification(double value) =>
        value < 1.005 ? "Off" : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{value:0.00}×");

    private void OnThemeChanged(object? sender, EventArgs e) => Preview.Update(Settings.Current);
}
