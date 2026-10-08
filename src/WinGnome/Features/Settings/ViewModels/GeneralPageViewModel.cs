using System.Windows.Input;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Services;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>General: start with Windows, which taskbar to use, theme and window behaviour.</summary>
internal sealed class GeneralPageViewModel : SettingsPageViewModel
{
    private string? _restoreStatus;

    /// <param name="settings">The live settings.</param>
    /// <param name="isSafeMode">True in safe mode, where the sign-in entry is not touched.</param>
    /// <param name="showTaskbarTweaks">Navigates to the Taskbar tweaks on the Streamline page.</param>
    public GeneralPageViewModel(SettingsService settings, bool isSafeMode, Action showTaskbarTweaks)
        : base(settings, "General", "")
    {
        StartWithWindows = Toggle(s => s.General.StartWithWindows, (s, v) => s.General.StartWithWindows = v);
        TaskbarMode = Choice(s => s.General.HideWindowsTaskbar, SetTaskbarMode,
            ChoiceOption.Of(true, "WinGnome dock", "Hides the Windows taskbar and uses the WinGnome dock."),
            ChoiceOption.Of(false, "Native Windows taskbar", "Keeps the Windows taskbar (with tray icons and Start button). The WinGnome dock is turned off."));
        NativeTaskbarAutoHide = Toggle(s => s.General.NativeTaskbarAutoHide, (s, v) => s.General.NativeTaskbarAutoHide = v);
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
        StyleTaskbarCommand = new RelayCommand(showTaskbarTweaks);
    }

    public ToggleSetting StartWithWindows { get; }

    public string StartWithWindowsSubtitle { get; }

    /// <summary>Dock mode (taskbar hidden) or native mode; the value is <c>HideWindowsTaskbar</c>.</summary>
    public ChoiceSetting TaskbarMode { get; }

    public ToggleSetting NativeTaskbarAutoHide { get; }

    /// <summary>True when the Windows taskbar stays visible, which reveals the native taskbar options.</summary>
    public bool IsNativeTaskbar => !Settings.Current.General.HideWindowsTaskbar;

    public ChoiceSetting Theme { get; }

    public ToggleSetting CenterNewWindows { get; }

    public ToggleSetting FocusFollowsMouse { get; }

    public ICommand RestoreTaskbarCommand { get; }

    public ICommand StyleTaskbarCommand { get; }

    /// <summary>Result of the last "Restore taskbar" click, shown under the button.</summary>
    public string? RestoreStatus
    {
        get => _restoreStatus;
        private set => SetProperty(ref _restoreStatus, value);
    }

    /// <summary>Choosing a mode switches the dock along with the taskbar; the dock can still be re-enabled on its own page.</summary>
    private static void SetTaskbarMode(AppSettings settings, bool hideWindowsTaskbar)
    {
        settings.General.HideWindowsTaskbar = hideWindowsTaskbar;
        settings.Dock.Enabled = hideWindowsTaskbar;
    }

    private void RestoreTaskbar()
    {
        var restored = TaskbarController.RestoreFromMarker(Settings.Directory);
        RestoreStatus = restored
            ? "The taskbar is visible again."
            : "The taskbar is already visible.";
    }
}
