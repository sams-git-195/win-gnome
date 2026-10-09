using System.Globalization;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.RegionLanguage;

/// <summary>One day of the week in the first-day drop-down.</summary>
internal sealed record DayChoice(DayOfWeek Day, string Name);

/// <summary>A date or time format row: a drop-down of the locale's patterns, each with a live sample.</summary>
internal sealed class FormatRow : ObservableObject
{
    private readonly Action<FormatRow, FormatChoice> _chosen;
    private IReadOnlyList<FormatChoice> _choices = [];
    private FormatChoice? _selected;
    private bool _showing;

    public FormatRow(string title, Action<FormatRow, FormatChoice> chosen)
    {
        Title = title;
        _chosen = chosen;
    }

    public string Title { get; }

    public IReadOnlyList<FormatChoice> Choices
    {
        get => _choices;
        private set => SetProperty(ref _choices, value);
    }

    /// <summary>The pattern the user picked. Setting it asks the panel to write it; <see cref="Show"/> sets what Windows reports.</summary>
    public FormatChoice? Selected
    {
        get => _selected;
        set
        {
            if (_showing || value is null || !SetProperty(ref _selected, value))
            {
                return;
            }

            _chosen(this, value);
        }
    }

    /// <summary>Shows what Windows holds without asking for a write.</summary>
    public void Show(FormatState state)
    {
        _showing = true;
        try
        {
            Choices = state.Choices;
            _selected = state.Choices.FirstOrDefault(c => string.Equals(c.Pattern, state.Current, StringComparison.Ordinal));
            OnPropertyChanged(nameof(Selected));
        }
        finally
        {
            _showing = false;
        }
    }
}

/// <summary>
/// Region &amp; Language: the user's region, the date and time formats with the first day of the week, and the display
/// language (read-only; Windows changes the format locale and language itself). Every change is written, read back from
/// Windows and shown as Windows reports it.
/// </summary>
internal sealed class RegionLanguagePanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private IReadOnlyList<GeoEntry> _geos = [];
    private IReadOnlyList<GeoEntry> _filteredGeos = [];
    private string _geoSearch = "";
    private string _geoName = "";
    private string _localeName = "";
    private string _formatLocaleText = "";
    private string _displayLanguageText = "";
    private DayChoice? _firstDay;
    private bool _isChoosingGeo;
    private int _pending;

    public RegionLanguagePanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.RegionLanguage)
    {
        _writer = context.CreateWriter();
        FirstDayChoices = FirstDayOfWeek.All
            .Select(d => new DayChoice(d, CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(d)))
            .ToList();
        Formats =
        [
            new FormatRow("Short Date", (row, choice) => ChooseFormat(RegionFormat.ShortDate, row, choice)),
            new FormatRow("Long Date", (row, choice) => ChooseFormat(RegionFormat.LongDate, row, choice)),
            new FormatRow("Short Time", (row, choice) => ChooseFormat(RegionFormat.ShortTime, row, choice)),
            new FormatRow("Long Time", (row, choice) => ChooseFormat(RegionFormat.LongTime, row, choice)),
        ];
        ChooseGeoCommand = new RelayCommand(() => IsChoosingGeo = !IsChoosingGeo, () => CanEdit && !IsBusy);
        SetGeoCommand = new RelayCommand(p => SetGeo(p as GeoEntry));
        ResetCommand = new RelayCommand(Reset, () => CanEdit && !IsBusy);
        ChangeFormatsCommand = new RelayCommand(() => context.OpenLink("ms-settings:regionformatting"));
        LanguageCommand = new RelayCommand(() => context.OpenLink("ms-settings:regionlanguage"));
    }

    /// <summary>The format rows, in display order.</summary>
    public IReadOnlyList<FormatRow> Formats { get; }

    public FormatRow ShortDate => Formats[0];

    public FormatRow LongDate => Formats[1];

    public FormatRow ShortTime => Formats[2];

    public FormatRow LongTime => Formats[3];

    public IReadOnlyList<DayChoice> FirstDayChoices { get; }

    /// <summary>The first day of the week as Windows reports it.</summary>
    public DayChoice? FirstDay
    {
        get => _firstDay;
        set
        {
            if (_showing || value is null || value == _firstDay)
            {
                return;
            }

            _firstDay = value;
            OnPropertyChanged();
            var day = value.Day;
            Change($"set the first day of the week to {day}", "the first day of the week",
                () => RegionService.SetFirstDay(day), snapshot => snapshot.FirstDay == day);
        }
    }

    /// <summary>The region's name, e.g. "United Kingdom".</summary>
    public string RegionText
    {
        get
        {
            var entry = GeoList.Find(_geos, _geoName);
            return entry?.DisplayName ?? (_geoName.Length > 0 ? _geoName : "Unknown");
        }
    }

    public string FormatLocaleText => _formatLocaleText;

    public string DisplayLanguageText => _displayLanguageText;

    /// <summary>The countries matching <see cref="GeoSearch"/>.</summary>
    public IReadOnlyList<GeoEntry> FilteredGeos
    {
        get => _filteredGeos;
        private set => SetProperty(ref _filteredGeos, value);
    }

    public string GeoSearch
    {
        get => _geoSearch;
        set
        {
            if (SetProperty(ref _geoSearch, value ?? ""))
            {
                FilteredGeos = GeoList.Filter(_geos, _geoSearch);
            }
        }
    }

    public bool IsChoosingGeo
    {
        get => _isChoosingGeo;
        set
        {
            if (SetProperty(ref _isChoosingGeo, value) && value)
            {
                GeoSearch = "";
            }
        }
    }

    /// <summary>True while a change is being written and read back; the rows are disabled meanwhile.</summary>
    public bool IsBusy => _pending > 0;

    /// <summary>True when the rows can be used: not safe mode, no write in flight.</summary>
    public bool CanChange => CanEdit && !IsBusy;

    public ICommand ChooseGeoCommand { get; }

    public ICommand SetGeoCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand ChangeFormatsCommand { get; }

    public ICommand LanguageCommand { get; }

    // Set while Show assigns the drop-downs, so showing Windows' value is not taken for the user choosing it.
    private bool _showing;

    protected override void Open()
    {
        LoadAsync(() => (RegionService.Read(), RegionService.ReadGeos()), result =>
        {
            _geos = result.Item2;
            _filteredGeos = _geos;
            Show(result.Item1);
            OnPropertyChanged(nameof(FilteredGeos));
        });
    }

    protected override void Close() => _isChoosingGeo = false;

    private void ChooseFormat(RegionFormat format, FormatRow row, FormatChoice choice)
    {
        if (!CanEdit)
        {
            return;
        }

        var pattern = choice.Pattern;
        Change($"set the {format} pattern to \"{pattern}\"", row.Title.ToLowerInvariant(),
            () => RegionService.SetFormat(format, pattern),
            snapshot => string.Equals(snapshot.Formats[format].Current, pattern, StringComparison.Ordinal));
    }

    private void SetGeo(GeoEntry? entry)
    {
        IsChoosingGeo = false;
        if (entry is null || !CanEdit || IsBusy || string.Equals(entry.Name, _geoName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var name = entry.Name;
        Change($"set the region to {name}", "the region",
            () => RegionService.SetGeo(name), snapshot => string.Equals(snapshot.GeoName, name, StringComparison.OrdinalIgnoreCase));
    }

    private void Reset()
    {
        var locale = _localeName;
        Change("reset the date, time and first day formats to the locale's defaults", "the formats",
            () => RegionService.ResetFormats(locale), _ => true);
    }

    /// <summary>
    /// The verified-set write: <paramref name="write"/> runs on the writer thread, then Windows is read back and the
    /// panel shows that. When the read-back isn't what was asked for (<paramref name="took"/>), the problem banner says so.
    /// </summary>
    private void Change(string what, string failureWhat, Func<bool> write, Func<RegionSnapshot, bool> took)
    {
        if (!CanEdit)
        {
            return;
        }

        SetPending(_pending + 1);
        _writer.Run(what, () =>
        {
            var wrote = false;
            RegionSnapshot? snapshot = null;
            try
            {
                wrote = write();
            }
            finally
            {
                // Always read back and release the busy state, even when the write threw.
                try
                {
                    snapshot = RegionService.Read();
                }
                catch (Exception ex)
                {
                    Log.Warn("Settings: could not read the region settings back after a change", ex);
                }

                var verified = wrote && snapshot is not null && took(snapshot);
                Context.Dispatcher.BeginInvoke(() => Verified(snapshot, verified, failureWhat));
            }

            return wrote;
        }, () => { });
    }

    private void Verified(RegionSnapshot? snapshot, bool verified, string failureWhat)
    {
        SetPending(_pending - 1);
        if (snapshot is not null)
        {
            Show(snapshot);
        }

        if (!verified)
        {
            ReportWriteFailure(failureWhat);
        }
    }

    private void SetPending(int count)
    {
        _pending = count;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanChange));
    }

    private void Show(RegionSnapshot snapshot)
    {
        _localeName = snapshot.LocaleName;
        _geoName = snapshot.GeoName;
        _formatLocaleText = snapshot.LocaleName.Length > 0 ? RegionService.CultureName(snapshot.LocaleName) : "Unknown";
        _displayLanguageText = snapshot.DisplayLanguage.Length > 0 ? RegionService.CultureName(snapshot.DisplayLanguage) : "Unknown";

        _showing = true;
        try
        {
            _firstDay = FirstDayChoices.FirstOrDefault(d => d.Day == snapshot.FirstDay);
            OnPropertyChanged(nameof(FirstDay));
        }
        finally
        {
            _showing = false;
        }

        ShortDate.Show(snapshot.Formats[RegionFormat.ShortDate]);
        LongDate.Show(snapshot.Formats[RegionFormat.LongDate]);
        ShortTime.Show(snapshot.Formats[RegionFormat.ShortTime]);
        LongTime.Show(snapshot.Formats[RegionFormat.LongTime]);
        OnPropertyChanged(nameof(RegionText));
        OnPropertyChanged(nameof(FormatLocaleText));
        OnPropertyChanged(nameof(DisplayLanguageText));
    }
}
