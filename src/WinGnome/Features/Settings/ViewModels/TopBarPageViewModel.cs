using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.TopBar;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Features.TopBar.Controls;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>Top bar: appearance (colours, opacity, blur, size, floating margin) and what the bar shows.</summary>
internal sealed class TopBarPageViewModel : SettingsPageViewModel
{
    // The value of the display-only "Custom image…" drop-down entry. Selecting it opens the file picker instead of
    // persisting; it is never equal to a real TopBarLogo value, so the combo box can tell them apart.
    private static readonly object PickCustomImage = new();

    // Cancels an in-flight preview mask decode when the selection changes or the page closes (spec 0021 M5).
    private int _logoGeneration;

    public TopBarPageViewModel(SettingsService settings)
        : base(settings, "Top Bar", "")
    {
        Preview = new TopBarPreviewViewModel(settings.Current);

        Enabled = Toggle(s => s.TopBar.Enabled, (s, v) => s.TopBar.Enabled = v);
        Monitors = Choice(s => s.TopBar.Monitors, (s, v) => s.TopBar.Monitors = v,
            ChoiceOption.Of(BarMonitors.All, "All displays"),
            ChoiceOption.Of(BarMonitors.Primary, "Main display only"));
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
        Font = Choice(s => s.TopBar.FontFamily, (s, v) => s.TopBar.FontFamily = v,
            ChoiceOption.Of(TopBarFont.AdwaitaSans, "Adwaita Sans"),
            ChoiceOption.Of(TopBarFont.SegoeUI, "Segoe UI"));
        FontSize = Slider(s => s.TopBar.FontSize, (s, v) => s.TopBar.FontSize = v, 10, 20, 0.5, SliderSetting.Pixels);
        Margin = Slider(s => s.TopBar.Margin, (s, v) => s.TopBar.Margin = v, 0, 24, 1, SliderSetting.Pixels);
        CornerRadius = Slider(s => s.TopBar.CornerRadius, (s, v) => s.TopBar.CornerRadius = v, 0, 24, 1, SliderSetting.Pixels);
        ItemCornerRadius = Slider(s => s.TopBar.ItemCornerRadius, (s, v) => s.TopBar.ItemCornerRadius = v, 0, 100, 1, SliderSetting.Pixels);

        ShowLogoMenu = Toggle(s => s.TopBar.ShowLogoMenu, (s, v) => s.TopBar.ShowLogoMenu = v);
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

        LogoOptions =
        [
            ChoiceOption.Of(TopBarLogo.Windows, "Windows"),
            ChoiceOption.Of(TopBarLogo.Foot, "Foot"),
            ChoiceOption.Of(TopBarLogo.Star, "Star"),
            ChoiceOption.Of(TopBarLogo.Terminal, "Terminal"),
            new ChoiceOption("Custom image…", PickCustomImage),
        ];
    }

    public TopBarPreviewViewModel Preview { get; }

    public ToggleSetting Enabled { get; }

    public ChoiceSetting Monitors { get; }

    public ColorEditor Background { get; }

    public ColorEditor Foreground { get; }

    public SliderSetting Opacity { get; }

    public ChoiceSetting Blur { get; }

    public SliderSetting Height { get; }

    public ChoiceSetting Font { get; }

    public SliderSetting FontSize { get; }

    public SliderSetting Margin { get; }

    public SliderSetting CornerRadius { get; }

    public SliderSetting ItemCornerRadius { get; }

    public ToggleSetting ShowLogoMenu { get; }

    public ToggleSetting ShowActivities { get; }

    public ToggleSetting ShowWorkspaces { get; }

    public ToggleSetting ShowAppName { get; }

    public ToggleSetting ShowBattery { get; }

    public ToggleSetting ShowTrayIcons { get; }

    public ChoiceSetting ClockStyle { get; }

    public ToggleSetting ShowDate { get; }

    public ToggleSetting ShowWeekday { get; }

    public ToggleSetting ShowSeconds { get; }

    /// <summary>The five logo choices. The last one ("Custom image…") is display-only and opens the file picker.</summary>
    public IReadOnlyList<ChoiceOption> LogoOptions { get; }

    /// <summary>
    /// The logo drop-down's selection. The four built-in marks write straight through; "Custom image…" is display-only:
    /// choosing it opens the picker without persisting, and cancelling snaps the combo box back to the stored choice.
    /// </summary>
    public ChoiceOption? SelectedLogo
    {
        get
        {
            var logo = Settings.Current.TopBar.Logo;
            return logo == TopBarLogo.Custom
                ? LogoOptions[^1]
                : LogoOptions.FirstOrDefault(o => o.Value is TopBarLogo value && value == logo);
        }
        set
        {
            if (value is null)
            {
                return;
            }

            if (ReferenceEquals(value.Value, PickCustomImage))
            {
                PickCustomLogo();
                return;
            }

            if (value.Value is TopBarLogo chosen && chosen != Settings.Current.TopBar.Logo)
            {
                Settings.Update(s => s.TopBar.Logo = chosen);
            }
        }
    }

    /// <summary>The custom file's path while Custom is active, otherwise descriptive text.</summary>
    public string LogoSubtitle
    {
        get
        {
            var bar = Settings.Current.TopBar;
            return bar.Logo == TopBarLogo.Custom && !string.IsNullOrWhiteSpace(bar.LogoImagePath)
                ? bar.LogoImagePath
                : "The mark at the left end of the bar.";
        }
    }

    /// <summary>Opening the page re-resolves the preview, so a custom logo shows without waiting for a settings change.</summary>
    public override void OnSelected()
    {
        base.OnSelected();
        UpdateLogoPreview(Settings.Current);
    }

    /// <summary>Leaving the page cancels an in-flight preview decode (spec 0021 M5).</summary>
    public override void OnDeselected()
    {
        _logoGeneration++;
        base.OnDeselected();
    }

    public override void Dispose()
    {
        // Closing the page cancels an in-flight preview decode so its callback can't touch a torn-down page (M5).
        _logoGeneration++;
        base.Dispose();
    }

    /// <summary>
    /// "Custom image…" is display-only: open the picker without writing anything through. On success, persist Custom
    /// and the path; on cancel, snap the combo box back to the stored choice (it had optimistically shown the entry).
    /// Deferred so the dialog doesn't open re-entrantly inside the combo box's selection-changed handling.
    /// </summary>
    private void PickCustomLogo() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a logo image",
                Filter = "Images|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName;
                Settings.Update(s =>
                {
                    s.TopBar.Logo = TopBarLogo.Custom;
                    s.TopBar.LogoImagePath = path;
                });
            }
            else
            {
                OnPropertyChanged(nameof(SelectedLogo));
            }
        });

    /// <summary>
    /// Resolves the logo for the live preview and, for a custom image, decodes its mask off the dispatcher under a
    /// generation counter (the settings window can't reach the bar's shared <see cref="TopBar.LogoProvider"/>). The
    /// picker is the only dialog and opens on an explicit selection, so the self-test's VisitAllPages never shows one.
    /// </summary>
    private void UpdateLogoPreview(AppSettings settings)
    {
        var bar = settings.TopBar;
        var target = LogoSelection.Resolve(bar.Logo, bar.LogoImagePath, File.Exists);
        if (target.Kind != LogoKind.Image)
        {
            _logoGeneration++;
            Preview.SetLogo(target.Kind, target.GeometryKey, null);
            return;
        }

        var path = target.Path!;
        var generation = ++_logoGeneration;
        Task.Run(() =>
        {
            var decoded = LogoMask.TryDecode(path, out var mask) ? mask : null;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (generation != _logoGeneration)
                {
                    return;
                }

                Preview.SetLogo(decoded is not null ? LogoKind.Image : LogoKind.WindowsMark, null, decoded);
            });
        });
    }

    protected override void OnSettingsApplied(AppSettings settings)
    {
        Preview.Update(settings);
        UpdateLogoPreview(settings);
    }
}
