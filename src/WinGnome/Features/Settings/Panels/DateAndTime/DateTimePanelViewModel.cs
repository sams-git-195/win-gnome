using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.DateAndTime;

/// <summary>
/// Date &amp; Time: the Windows time zone (searchable list) and the top bar clock's format. Automatic date, time and
/// time zone are in Windows Settings (linked) because they need administrator rights or services WinGnome doesn't touch.
/// </summary>
internal sealed class DateTimePanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private IReadOnlyList<TimeZoneEntry> _zones = [];
    private IReadOnlyList<TimeZoneEntry> _filteredZones = [];
    private TimeZoneEntry? _timeZone;
    private string _zoneSearch = "";
    private bool _isChoosingZone;
    private bool _isAutomatic;

    public DateTimePanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.DateTime)
    {
        _writer = context.CreateWriter();
        ClockStyle = Choice(s => s.TopBar.ClockStyle, (s, v) => s.TopBar.ClockStyle = v,
            ChoiceOption.Of(Core.Settings.ClockStyle.TwentyFourHour, "24-hour"),
            ChoiceOption.Of(Core.Settings.ClockStyle.TwelveHour, "AM / PM"));
        ShowWeekday = Toggle(s => s.TopBar.ShowWeekday, (s, v) => s.TopBar.ShowWeekday = v);
        ShowDate = Toggle(s => s.TopBar.ShowDate, (s, v) => s.TopBar.ShowDate = v);
        ShowSeconds = Toggle(s => s.TopBar.ShowSeconds, (s, v) => s.TopBar.ShowSeconds = v);
        ChooseZoneCommand = new RelayCommand(() => IsChoosingZone = !IsChoosingZone, () => CanEdit);
        AutomaticCommand = new RelayCommand(() => context.OpenLink("ms-settings:dateandtime"));
        SetZoneCommand = new RelayCommand(p => SetZone(p as TimeZoneEntry));
    }

    /// <summary>WinGnome: the top bar clock's 24-hour or AM/PM format.</summary>
    public ChoiceSetting ClockStyle { get; }

    public ToggleSetting ShowWeekday { get; }

    public ToggleSetting ShowDate { get; }

    public ToggleSetting ShowSeconds { get; }

    public string TimeZoneName => _timeZone?.DisplayName ?? "Unknown";

    /// <summary>The time zones matching <see cref="ZoneSearch"/>, west to east.</summary>
    public IReadOnlyList<TimeZoneEntry> FilteredZones
    {
        get => _filteredZones;
        private set => SetProperty(ref _filteredZones, value);
    }

    public string ZoneSearch
    {
        get => _zoneSearch;
        set
        {
            if (SetProperty(ref _zoneSearch, value ?? ""))
            {
                FilteredZones = TimeZoneList.Filter(_zones, _zoneSearch);
            }
        }
    }

    /// <summary>Changes the Windows time zone to the chosen <see cref="TimeZoneEntry"/>.</summary>
    public ICommand SetZoneCommand { get; }

    /// <summary>True while the searchable zone list is open.</summary>
    public bool IsChoosingZone
    {
        get => _isChoosingZone;
        set
        {
            if (SetProperty(ref _isChoosingZone, value) && value)
            {
                ZoneSearch = "";
            }
        }
    }

    /// <summary>Shown when Windows sets the time zone from the location and may undo a manual change.</summary>
    public string? AutomaticNote => _isAutomatic
        ? "Windows sets the time zone automatically and may change it back. Turn that off in Windows Settings to choose here."
        : null;

    public ICommand ChooseZoneCommand { get; }

    public ICommand AutomaticCommand { get; }

    protected override void Open()
    {
        _zones = TimeZoneService.List();
        _filteredZones = _zones;
        _isAutomatic = TimeZoneService.IsAutomatic();
        ReadZone();
        OnPropertyChanged(string.Empty);
    }

    protected override void Close() => _isChoosingZone = false;

    private void SetZone(TimeZoneEntry? zone)
    {
        IsChoosingZone = false;
        if (zone is null || !CanEdit || zone == _timeZone)
        {
            return;
        }

        _timeZone = zone;
        OnPropertyChanged(nameof(TimeZoneName));
        var id = zone.Id;
        _writer.Run($"set the time zone to {id}", () => TimeZoneService.Set(id), () =>
        {
            ReportWriteFailure("the time zone");
            ReadZone();
        });
    }

    private void ReadZone()
    {
        var current = TimeZoneService.CurrentId();
        var index = TimeZoneList.IndexOf(_zones, current);
        _timeZone = index >= 0 ? _zones[index] : null;
        OnPropertyChanged(nameof(TimeZoneName));
    }
}
