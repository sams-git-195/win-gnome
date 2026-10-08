using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>Activities: hot corner, Super key, dock shortcuts, global hotkey and backdrop.</summary>
internal sealed class ActivitiesPageViewModel : SettingsPageViewModel
{
    public ActivitiesPageViewModel(SettingsService settings)
        : base(settings, "Activities", "")
    {
        HotCorner = Toggle(s => s.Activities.HotCorner, (s, v) => s.Activities.HotCorner = v);
        HotCornerDelay = Slider(s => s.Activities.HotCornerDelayMs, (s, v) => s.Activities.HotCornerDelayMs = (int)Math.Round(v),
            0, 1000, 10, value => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{value:0} ms"));
        SuperKey = Toggle(s => s.Activities.SuperKeyOpensOverview, (s, v) => s.Activities.SuperKeyOpensOverview = v);
        SuperNumber = Toggle(s => s.Activities.SuperNumberActivatesDock, (s, v) => s.Activities.SuperNumberActivatesDock = v);
        Hotkey = Track(new ValidatedTextSetting(settings, s => s.Activities.Hotkey, (s, v) => s.Activities.Hotkey = v, ValidateHotkey));
        BackdropOpacity = Slider(s => s.Activities.BackdropOpacity, (s, v) => s.Activities.BackdropOpacity = v, 0.2, 1, 0.05, SliderSetting.Percent);
    }

    public ToggleSetting HotCorner { get; }

    public SliderSetting HotCornerDelay { get; }

    public ToggleSetting SuperKey { get; }

    public ToggleSetting SuperNumber { get; }

    public ValidatedTextSetting Hotkey { get; }

    public SliderSetting BackdropOpacity { get; }

    private static TextValidation ValidateHotkey(string text) =>
        Core.Input.Hotkey.TryParse(text, out var hotkey)
            ? TextValidation.Valid(hotkey.ToString())
            : TextValidation.Invalid("Use a combination such as Alt+F1 or Ctrl+Alt+T.");
}
