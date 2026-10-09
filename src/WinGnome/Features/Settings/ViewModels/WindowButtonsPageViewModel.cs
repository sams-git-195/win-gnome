using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>A colour preset card showing its three circles.</summary>
internal sealed class PresetPreview : ObservableObject
{
    public PresetPreview(TrafficLightPreset preset, string name)
    {
        Preset = preset;
        Name = name;
        Close = Minimize = Maximize = Brushes.Gray;
    }

    public TrafficLightPreset Preset { get; }

    public string Name { get; }

    public Brush Close { get; private set; }

    public Brush Minimize { get; private set; }

    public Brush Maximize { get; private set; }

    public void Show(TrafficLightColors colors)
    {
        Close = ColorConversion.ToBrush(colors.Close);
        Minimize = ColorConversion.ToBrush(colors.Minimize);
        Maximize = ColorConversion.ToBrush(colors.Maximize);
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Window buttons: the traffic-light colours, layout, behaviour and per-app exclusions, with a live preview.</summary>
internal sealed class WindowButtonsPageViewModel : SettingsPageViewModel
{
    public WindowButtonsPageViewModel(SettingsService settings)
        : base(settings, "Window Buttons", "")
    {
        Preview = new TitleBarPreviewViewModel(settings.Current.WindowButtons);
        Excluded = new ExcludedAppsViewModel(settings);
        Presets = TrafficLightPalette.Presets.Select(p => new PresetPreview(p, PresetName(p))).ToList();
        RefreshPresets(settings.Current);

        Enabled = Toggle(s => s.WindowButtons.Enabled, (s, v) => s.WindowButtons.Enabled = v);
        CloseColor = Color(s => s.WindowButtons.CloseColor, (s, v) => s.WindowButtons.CloseColor = v, []);
        MinimizeColor = Color(s => s.WindowButtons.MinimizeColor, (s, v) => s.WindowButtons.MinimizeColor = v, []);
        MaximizeColor = Color(s => s.WindowButtons.MaximizeColor, (s, v) => s.WindowButtons.MaximizeColor = v, []);
        Side = Choice(s => s.WindowButtons.Side, (s, v) => s.WindowButtons.Side = v,
            ChoiceOption.Of(ButtonSide.Right, "Right"),
            ChoiceOption.Of(ButtonSide.Left, "Left"));
        Order = Choice(s => s.WindowButtons.Order, (s, v) => s.WindowButtons.Order = v,
            ChoiceOption.Of(ButtonOrder.MinimizeMaximizeClose, "Windows  (−  □  ×)"),
            ChoiceOption.Of(ButtonOrder.CloseMinimizeMaximize, "macOS  (×  −  +)"));
        Diameter = Slider(s => s.WindowButtons.Diameter, (s, v) => s.WindowButtons.Diameter = v, 8, 24, 1, SliderSetting.Pixels);
        Spacing = Slider(s => s.WindowButtons.Spacing, (s, v) => s.WindowButtons.Spacing = v, 2, 20, 1, SliderSetting.Pixels);
        ShowSymbolsOnHover = Toggle(s => s.WindowButtons.ShowSymbolsOnHover, (s, v) => s.WindowButtons.ShowSymbolsOnHover = v);
        DimInactiveWindows = Toggle(s => s.WindowButtons.DimInactiveWindows, (s, v) => s.WindowButtons.DimInactiveWindows = v);
        UnifyTitleBarColor = Toggle(s => s.WindowButtons.UnifyTitleBarColor, (s, v) => s.WindowButtons.UnifyTitleBarColor = v);
        DecorateCustomTitleBars = Toggle(s => s.WindowButtons.DecorateCustomTitleBars, (s, v) => s.WindowButtons.DecorateCustomTitleBars = v);
        DecorateWebTitleBarButtons = Toggle(s => s.WindowButtons.DecorateWebTitleBarButtons, (s, v) => s.WindowButtons.DecorateWebTitleBarButtons = v);
    }

    public TitleBarPreviewViewModel Preview { get; }

    public ExcludedAppsViewModel Excluded { get; }

    public IReadOnlyList<PresetPreview> Presets { get; }

    public ToggleSetting Enabled { get; }

    public ColorEditor CloseColor { get; }

    public ColorEditor MinimizeColor { get; }

    public ColorEditor MaximizeColor { get; }

    public ChoiceSetting Side { get; }

    public ChoiceSetting Order { get; }

    public SliderSetting Diameter { get; }

    public SliderSetting Spacing { get; }

    public ToggleSetting ShowSymbolsOnHover { get; }

    public ToggleSetting DimInactiveWindows { get; }

    public ToggleSetting UnifyTitleBarColor { get; }

    public ToggleSetting DecorateCustomTitleBars { get; }

    public ToggleSetting DecorateWebTitleBarButtons { get; }

    /// <summary>The preset card matching the settings; choosing a card switches the preset.</summary>
    public PresetPreview? SelectedPreset
    {
        get => Presets.FirstOrDefault(p => p.Preset == Settings.Current.WindowButtons.Preset);
        set
        {
            if (value is not null && value.Preset != Settings.Current.WindowButtons.Preset)
            {
                Settings.Update(s => s.WindowButtons.Preset = value.Preset);
            }
        }
    }

    /// <summary>True when the three custom colour editors apply.</summary>
    public bool IsCustomPreset => Settings.Current.WindowButtons.Preset == TrafficLightPreset.Custom;

    public override void Dispose()
    {
        Excluded.Dispose();
        base.Dispose();
    }

    protected override void OnSettingsApplied(AppSettings settings)
    {
        RefreshPresets(settings);
        Preview.Update(settings.WindowButtons);
    }

    /// <summary>The Custom card previews the user's own colours; the others are fixed.</summary>
    private void RefreshPresets(AppSettings settings)
    {
        var buttons = settings.WindowButtons;
        var custom = new WindowButtonSettings
        {
            Preset = TrafficLightPreset.Custom,
            CloseColor = buttons.CloseColor,
            MinimizeColor = buttons.MinimizeColor,
            MaximizeColor = buttons.MaximizeColor,
        };
        foreach (var preview in Presets)
        {
            preview.Show(preview.Preset == TrafficLightPreset.Custom
                ? TrafficLightPalette.For(custom)
                : TrafficLightPalette.ForPreset(preview.Preset));
        }
    }

    private static string PresetName(TrafficLightPreset preset) => preset switch
    {
        TrafficLightPreset.MacOS => "macOS",
        TrafficLightPreset.Gnome => "GNOME",
        _ => preset.ToString(),
    };
}
