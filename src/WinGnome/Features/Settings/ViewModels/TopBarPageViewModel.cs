using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>Top bar: appearance (colours, opacity, blur, size, floating margin) and what the bar shows.</summary>
internal sealed class TopBarPageViewModel : SettingsPageViewModel
{
    public TopBarPageViewModel(SettingsService settings)
        : base(settings, "Top Bar", "")
    {
        Preview = new TopBarPreviewViewModel(settings.Current);

        Enabled = Toggle(s => s.TopBar.Enabled, (s, v) => s.TopBar.Enabled = v);
        Background = Color(s => s.TopBar.BackgroundColor, (s, v) => s.TopBar.BackgroundColor = v, ColorPresets.TopBarBackgrounds,
            applyPreset: (s, preset) =>
            {
                s.TopBar.BackgroundColor = preset.Hex;
                if (preset.SuggestedForeground is not null)
                {
                    s.TopBar.ForegroundColor = preset.SuggestedForeground;
                }
            });
        Foreground = Color(s => s.TopBar.ForegroundColor, (s, v) => s.TopBar.ForegroundColor = v, ColorPresets.Text);
        Opacity = Slider(s => s.TopBar.Opacity, (s, v) => s.TopBar.Opacity = v, 0, 1, 0.01, SliderSetting.Percent);
        Blur = Choice(s => s.TopBar.Blur, (s, v) => s.TopBar.Blur = v,
            ChoiceOption.Of(BlurEffect.None, "None"),
            ChoiceOption.Of(BlurEffect.Blur, "Blur"),
            ChoiceOption.Of(BlurEffect.Acrylic, "Acrylic"));
        Height = Slider(s => s.TopBar.Height, (s, v) => s.TopBar.Height = v, 24, 48, 1, SliderSetting.Pixels);
        FontSize = Slider(s => s.TopBar.FontSize, (s, v) => s.TopBar.FontSize = v, 10, 20, 0.5, SliderSetting.Pixels);
        Margin = Slider(s => s.TopBar.Margin, (s, v) => s.TopBar.Margin = v, 0, 24, 1, SliderSetting.Pixels);
        CornerRadius = Slider(s => s.TopBar.CornerRadius, (s, v) => s.TopBar.CornerRadius = v, 0, 24, 1, SliderSetting.Pixels);

        ShowActivities = Toggle(s => s.TopBar.ShowActivitiesButton, (s, v) => s.TopBar.ShowActivitiesButton = v);
        ShowWorkspaces = Toggle(s => s.TopBar.ShowWorkspaceIndicator, (s, v) => s.TopBar.ShowWorkspaceIndicator = v);
        ShowAppName = Toggle(s => s.TopBar.ShowFocusedAppName, (s, v) => s.TopBar.ShowFocusedAppName = v);
        ShowBattery = Toggle(s => s.TopBar.ShowBatteryPercentage, (s, v) => s.TopBar.ShowBatteryPercentage = v);
        ShowTrayIcons = Toggle(s => s.TopBar.ShowTrayIcons, (s, v) => s.TopBar.ShowTrayIcons = v);

        ClockStyle = Choice(s => s.TopBar.ClockStyle, (s, v) => s.TopBar.ClockStyle = v,
            ChoiceOption.Of(Core.Settings.ClockStyle.TwentyFourHour, "24-hour"),
            ChoiceOption.Of(Core.Settings.ClockStyle.TwelveHour, "12-hour"));
        ShowDate = Toggle(s => s.TopBar.ShowDate, (s, v) => s.TopBar.ShowDate = v);
        ShowWeekday = Toggle(s => s.TopBar.ShowWeekday, (s, v) => s.TopBar.ShowWeekday = v);
        ShowSeconds = Toggle(s => s.TopBar.ShowSeconds, (s, v) => s.TopBar.ShowSeconds = v);
    }

    public TopBarPreviewViewModel Preview { get; }

    public ToggleSetting Enabled { get; }

    public ColorEditor Background { get; }

    public ColorEditor Foreground { get; }

    public SliderSetting Opacity { get; }

    public ChoiceSetting Blur { get; }

    public SliderSetting Height { get; }

    public SliderSetting FontSize { get; }

    public SliderSetting Margin { get; }

    public SliderSetting CornerRadius { get; }

    public ToggleSetting ShowActivities { get; }

    public ToggleSetting ShowWorkspaces { get; }

    public ToggleSetting ShowAppName { get; }

    public ToggleSetting ShowBattery { get; }

    public ToggleSetting ShowTrayIcons { get; }

    public ChoiceSetting ClockStyle { get; }

    public ToggleSetting ShowDate { get; }

    public ToggleSetting ShowWeekday { get; }

    public ToggleSetting ShowSeconds { get; }

    protected override void OnSettingsApplied(AppSettings settings) => Preview.Update(settings);
}
