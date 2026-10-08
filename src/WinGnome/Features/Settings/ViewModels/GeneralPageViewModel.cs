using System.Windows.Input;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Services;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>General: start with Windows, taskbar, theme and window behaviour.</summary>
internal sealed class GeneralPageViewModel : SettingsPageViewModel
{
    private string? _restoreStatus;

    public GeneralPageViewModel(SettingsService settings, bool isSafeMode)
        : base(settings, "General", "")
    {
        StartWithWindows = Toggle(s => s.General.StartWithWindows, (s, v) => s.General.StartWithWindows = v);
        HideWindowsTaskbar = Toggle(s => s.General.HideWindowsTaskbar, (s, v) => s.General.HideWindowsTaskbar = v);
        Theme = Choice(s => s.General.Theme, (s, v) => s.General.Theme = v,
            ChoiceOption.Of(ThemeMode.System, "System"),
            ChoiceOption.Of(ThemeMode.Light, "Light"),
            ChoiceOption.Of(ThemeMode.Dark, "Dark"));
        CenterNewWindows = Toggle(s => s.General.CenterNewWindows, (s, v) => s.General.CenterNewWindows = v);
        FocusFollowsMouse = Toggle(s => s.General.FocusFollowsMouse, (s, v) => s.General.FocusFollowsMouse = v);
        StartWithWindowsSubtitle = isSafeMode
            ? "Safe mode: the sign-in entry is not changed while this session runs."
            : "Launch WinGnome automatically when you sign in.";
        RestoreTaskbarCommand = new RelayCommand(RestoreTaskbar);
    }

    public ToggleSetting StartWithWindows { get; }

    public string StartWithWindowsSubtitle { get; }

    public ToggleSetting HideWindowsTaskbar { get; }

    public ChoiceSetting Theme { get; }

    public ToggleSetting CenterNewWindows { get; }

    public ToggleSetting FocusFollowsMouse { get; }

    public ICommand RestoreTaskbarCommand { get; }

    /// <summary>Result of the last "Restore taskbar now" click, shown under the button.</summary>
    public string? RestoreStatus
    {
        get => _restoreStatus;
        private set => SetProperty(ref _restoreStatus, value);
    }

    private void RestoreTaskbar()
    {
        var restored = TaskbarController.RestoreFromMarker(Settings.Directory);
        RestoreStatus = restored
            ? "The taskbar is visible again."
            : "The taskbar is already visible.";
    }
}
