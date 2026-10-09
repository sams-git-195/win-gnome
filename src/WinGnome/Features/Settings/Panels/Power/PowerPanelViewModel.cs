using WinGnome.Core.ControlCenter;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Power;

/// <summary>A power mode choice, labelled as GNOME does with Windows' name underneath.</summary>
internal sealed record PowerModeChoice(Guid Mode, string Label, string Description);

/// <summary>One timeout drop-down (screen blank or automatic suspend, for one power source).</summary>
internal sealed class TimeoutSetting : ObservableObject
{
    private readonly Action<int> _write;
    private TimeoutChoice? _selected;

    public TimeoutSetting(string title, IReadOnlyList<TimeoutChoice> choices, int current, Action<int> write)
    {
        Title = title;
        Choices = choices;
        _selected = choices.FirstOrDefault(c => c.Seconds == current);
        _write = write;
    }

    public string Title { get; }

    public IReadOnlyList<TimeoutChoice> Choices { get; }

    public TimeoutChoice? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetProperty(ref _selected, value))
            {
                _write(value.Seconds);
            }
        }
    }
}

/// <summary>
/// Power: battery state, power mode, and when the screen blanks and the PC suspends (for each power source on
/// machines with a battery), all on the active power plan.
/// </summary>
internal sealed class PowerPanelViewModel(SystemPanelContext context) : SystemPanelViewModel(context, PanelIds.Power)
{
    private readonly SystemSettingWriter _writer = context.CreateWriter();
    private BatteryStatus _battery = BatteryStatus.None;
    private PowerModeChoice? _powerMode;
    private bool _hasPowerMode;
    private IReadOnlyList<TimeoutSetting> _screenBlank = [];
    private IReadOnlyList<TimeoutSetting> _suspend = [];

    public IReadOnlyList<PowerModeChoice> PowerModes { get; } =
    [
        new(PowerModeApi.Performance, "Performance", "Best performance: high performance and power usage."),
        new(PowerModeApi.Balanced, "Balanced", "Standard performance and power usage."),
        new(PowerModeApi.PowerSaver, "Power Saver", "Best power efficiency: reduced performance and power usage."),
    ];

    public bool HasBattery => _battery.HasBattery;

    public string BatteryLevel => _battery.PercentText;

    /// <summary>0..100 for the level bar.</summary>
    public double BatteryPercent => _battery.Percent ?? 0;

    public string BatteryState => _battery.StateText;

    /// <summary>False when this Windows or power plan has no power mode (the row is hidden).</summary>
    public bool HasPowerMode => _hasPowerMode;

    public PowerModeChoice? PowerMode
    {
        get => _powerMode;
        set
        {
            if (value is null || !CanEdit || !SetProperty(ref _powerMode, value))
            {
                return;
            }

            var mode = value.Mode;
            _writer.Run($"set the power mode to {value.Label}", () => PowerModeApi.Write(mode), () =>
            {
                ReportWriteFailure("the power mode");
                ReadPowerMode();
            });
        }
    }

    public IReadOnlyList<TimeoutSetting> ScreenBlank
    {
        get => _screenBlank;
        private set => SetProperty(ref _screenBlank, value);
    }

    public IReadOnlyList<TimeoutSetting> Suspend
    {
        get => _suspend;
        private set => SetProperty(ref _suspend, value);
    }

    protected override void Open()
    {
        _battery = PowerService.Battery();
        ReadPowerMode();

        if (PowerService.ActiveScheme() is not { } scheme)
        {
            Problem = "WinGnome couldn't read the active power plan.";
            ScreenBlank = [];
            Suspend = [];
        }
        else
        {
            ScreenBlank = Timeouts(scheme, NativeMethods.GUID_VIDEO_SUBGROUP, NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT, PowerTimeouts.ScreenBlank, "Screen Blank");
            Suspend = Timeouts(scheme, NativeMethods.GUID_SLEEP_SUBGROUP, NativeMethods.GUID_STANDBY_TIMEOUT, PowerTimeouts.Suspend, "Automatic Suspend");
        }

        OnPropertyChanged(string.Empty);
    }

    protected override void Close()
    {
        ScreenBlank = [];
        Suspend = [];
    }

    private void ReadPowerMode()
    {
        var mode = PowerModeApi.Read();
        _hasPowerMode = mode is not null;
        _powerMode = PowerModes.FirstOrDefault(m => m.Mode == mode);
        OnPropertyChanged(nameof(HasPowerMode));
        OnPropertyChanged(nameof(PowerMode));
    }

    /// <summary>One drop-down per power source: plugged in only on desktops, both on machines with a battery.</summary>
    private List<TimeoutSetting> Timeouts(Guid scheme, Guid subgroup, Guid setting, IReadOnlyList<int> presets, string name)
    {
        var sources = _battery.HasBattery ? new[] { PowerSource.OnBattery, PowerSource.PluggedIn } : [PowerSource.PluggedIn];
        var settings = new List<TimeoutSetting>();
        foreach (var source in sources)
        {
            if (PowerService.ReadTimeout(scheme, subgroup, setting, source) is not { } current)
            {
                continue;
            }

            var title = !_battery.HasBattery ? name : source == PowerSource.OnBattery ? "On Battery Power" : "When Plugged In";
            settings.Add(new TimeoutSetting(title, PowerTimeouts.Choices(presets, current), current, seconds =>
                _writer.Run($"set {name} ({source}) to {PowerTimeouts.Label(seconds)}",
                    () => PowerService.WriteTimeout(scheme, subgroup, setting, source, seconds),
                    () => ReportWriteFailure(name.ToLowerInvariant()))));
        }

        return settings;
    }
}
