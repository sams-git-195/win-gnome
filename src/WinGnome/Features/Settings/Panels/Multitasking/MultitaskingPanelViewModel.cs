using WinGnome.Core.ControlCenter;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Multitasking;

/// <summary>
/// Multitasking: WinGnome's hot corner and workspace indicator, Windows window snapping, and whether Alt+Tab shows
/// windows from every virtual desktop. Snap and Alt+Tab options are the HKCU values Windows Settings writes.
/// </summary>
internal sealed class MultitaskingPanelViewModel : SystemPanelViewModel
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string SnapFlyoutTweak = "disable-snap-flyout";
    private const string AllDesktops = "All workspaces";
    private const string CurrentDesktop = "Current workspace only";

    private readonly SystemSettingWriter _writer;
    private readonly RegistryStore _registry = new();
    private bool _snapWindows;
    private bool _snapLayouts;
    private bool _snapSuggestions;
    private bool _altTabAllDesktops;

    public MultitaskingPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Multitasking)
    {
        _writer = context.CreateWriter();
        HotCorner = Toggle(s => s.Activities.HotCorner, (s, v) => s.Activities.HotCorner = v);
        WorkspaceIndicator = Toggle(s => s.TopBar.ShowWorkspaceIndicator, (s, v) => s.TopBar.ShowWorkspaceIndicator = v);
    }

    /// <summary>WinGnome: the top-left corner opens the Activities overview.</summary>
    public ToggleSetting HotCorner { get; }

    /// <summary>WinGnome: workspace dots in the top bar.</summary>
    public ToggleSetting WorkspaceIndicator { get; }

    /// <summary>Windows' master snap switch (drag to an edge to snap; SPI_SETWINARRANGING takes the BOOL in pvParam).</summary>
    public bool SnapWindows
    {
        get => _snapWindows;
        set
        {
            if (SetProperty(ref _snapWindows, value))
            {
                _writer.Run($"turn window snapping {(value ? "on" : "off")}",
                    () => Spi.Set(NativeMethods.SPI_SETWINARRANGING, 0, value ? 1 : 0, "window snapping"),
                    () => ReportWriteFailure("window snapping"));
            }
        }
    }

    /// <summary>The layout picker on the maximise button (EnableSnapAssistFlyout).</summary>
    public bool SnapLayouts
    {
        get => _snapLayouts;
        set
        {
            if (CanChangeSnapLayouts && SetProperty(ref _snapLayouts, value))
            {
                WriteAdvanced("EnableSnapAssistFlyout", value, "snap layouts");
            }
        }
    }

    /// <summary>After snapping, suggest windows for the rest of the screen (SnapAssist).</summary>
    public bool SnapSuggestions
    {
        get => _snapSuggestions;
        set
        {
            if (SetProperty(ref _snapSuggestions, value))
            {
                WriteAdvanced("SnapAssist", value, "snap suggestions");
            }
        }
    }

    /// <summary>The choices for Alt+Tab, as GNOME's App Switching labels them.</summary>
    public IReadOnlyList<string> AltTabScopes { get; } = [AllDesktops, CurrentDesktop];

    /// <summary>Alt+Tab shows windows from all virtual desktops or only the current one (VirtualDesktopAltTabFilter).</summary>
    public string AltTabScope
    {
        get => _altTabAllDesktops ? AllDesktops : CurrentDesktop;
        set
        {
            if (value is null || (value == AllDesktops) == _altTabAllDesktops)
            {
                return;
            }

            _altTabAllDesktops = value == AllDesktops;
            OnPropertyChanged();

            // 0 = all desktops, 1 = only the current one.
            var data = _altTabAllDesktops ? 0 : 1;
            _writer.Run($"make Alt+Tab show windows from {value.ToLowerInvariant()}",
                () => SetAdvanced("VirtualDesktopAltTabFilter", data),
                () => ReportWriteFailure("app switching"));
        }
    }

    public bool CanChangeSnapLayouts => CanEdit && !SnapFlyoutTweakIsOn;

    /// <summary>Why snap layouts are locked, or null.</summary>
    public string? SnapLayoutsNote => SnapFlyoutTweakIsOn
        ? "The \"Disable the snap layouts flyout\" tweak on the Streamline page sets this; turn it off there to choose here."
        : "The picker shown when you hover over a window's maximise button.";


    private bool SnapFlyoutTweakIsOn => Settings.Current.EnabledTweaks.Contains(SnapFlyoutTweak, StringComparer.OrdinalIgnoreCase);

    protected override void Open()
    {
        _snapWindows = Spi.Get(NativeMethods.SPI_GETWINARRANGING, 1, "window snapping") != 0;
        try
        {
            // Windows treats a missing value as its default: snap layouts and suggestions on, Alt+Tab current desktop only.
            _snapLayouts = ReadAdvanced("EnableSnapAssistFlyout", 1) != 0;
            _snapSuggestions = ReadAdvanced("SnapAssist", 1) != 0;
            _altTabAllDesktops = ReadAdvanced("VirtualDesktopAltTabFilter", 1) == 0;
        }
        catch (RegistryAccessException ex)
        {
            Log.Warn("Could not read the snap settings", ex);
            Problem = "WinGnome couldn't read the Windows snap settings.";
        }

        OnPropertyChanged(string.Empty);
    }


    private int ReadAdvanced(string name, int fallback) =>
        _registry.GetValue(AdvancedKey, name) is { Kind: RegistryValueKind.DWord, Data: int value } ? value : fallback;

    private void WriteAdvanced(string name, bool on, string what) =>
        _writer.Run($"turn {what} {(on ? "on" : "off")}", () => SetAdvanced(name, on ? 1 : 0), () => ReportWriteFailure(what));

    /// <summary>Writes an Explorer option and tells Explorer, as Windows Settings does. Runs on the writer thread.</summary>
    private bool SetAdvanced(string name, int value)
    {
        _registry.SetValue(AdvancedKey, name, RegistryValue.DWord(value));
        SystemBroadcast.SettingChanged("TraySettings");
        return true;
    }
}
