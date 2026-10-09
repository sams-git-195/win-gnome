using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Geometry;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>A resolution choice with its label, "1920 × 1080 (16∶9)".</summary>
internal sealed record ResolutionChoice(Resolution Size, string Label)
{
    public static ResolutionChoice For(Resolution size) => new(size, DisplayModes.Label(size));
}

/// <summary>One display in the panel: what Windows reports and the not-yet-applied changes to it.</summary>
internal sealed class DisplayItem(DisplayInfo info, int number) : ObservableObject
{
    private DisplaySetting _staged = info.Current;
    private LayoutRect _preview;

    public DisplayInfo Info { get; } = info;

    /// <summary>1-based number shown on the preview, as GNOME numbers its displays.</summary>
    public int Number { get; } = number;

    public string Id => Info.DeviceName;

    public string Name => Info.Name;

    public string Label => $"{Number}  {Info.Name}";

    public DisplaySetting Staged
    {
        get => _staged;
        set
        {
            if (SetProperty(ref _staged, value))
            {
                OnPropertyChanged(nameof(IsPrimary));
            }
        }
    }

    public bool IsPrimary => _staged.IsPrimary;

    /// <summary>Where the display is drawn in the arrangement preview.</summary>
    public LayoutRect Preview
    {
        get => _preview;
        set => SetProperty(ref _preview, value);
    }

    public PixelRect Bounds => PixelRect.FromSize(_staged.X, _staged.Y, _staged.Width, _staged.Height);
}

/// <summary>
/// Displays: arrangement (drag in the preview), primary display, resolution and refresh rate, with GNOME's
/// "Keep changes?" countdown: an applied change reverts after 15 seconds unless kept, also when the window closes or
/// WinGnome stops (through <see cref="DisplayRevertFile"/>). Scale is shown read-only and links to Windows Settings
/// because changing it needs an undocumented API (KI-062).
/// </summary>
internal sealed class DisplaysPanelViewModel : SystemPanelViewModel
{
    /// <summary>Size of the arrangement preview in device-independent pixels.</summary>
    public const double PreviewWidth = 600;

    public const double PreviewHeight = 220;

    private const double PreviewPadding = 16;

    private readonly SystemSettingWriter _writer;
    private readonly KeepChangesCountdown _countdown = new();
    private readonly DispatcherTimer _timer;
    private DisplayRevert? _pending;
    private DisplayItem? _selected;
    private bool _isApplying;
    private bool _open;
    private bool _disposing;
    private int _generation;
    private int _secondsLeft;

    public DisplaysPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Displays)
    {
        _writer = context.CreateWriter();
        _timer = new DispatcherTimer(DispatcherPriority.Normal, context.Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        ApplyCommand = new RelayCommand(Apply, () => IsDirty && CanEdit && !_isApplying && !IsWaiting);
        ResetCommand = new RelayCommand(Load, () => IsDirty && !_isApplying);
        KeepCommand = new RelayCommand(Keep);
        RevertCommand = new RelayCommand(Revert);
        ScaleCommand = new RelayCommand(() => context.OpenLink("ms-settings:display"));
    }

    public ObservableCollection<DisplayItem> Displays { get; } = [];

    public bool HasSeveralDisplays => Displays.Count > 1;

    public DisplayItem? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetProperty(ref _selected, value))
            {
                OnSelectedChanged();
            }
        }
    }

    /// <summary>The selected display's resolutions, largest first.</summary>
    public IReadOnlyList<ResolutionChoice> Resolutions => _selected is null
        ? []
        : DisplayModes.Resolutions(_selected.Info.Modes).Select(ResolutionChoice.For).ToList();

    public ResolutionChoice? Resolution
    {
        get => _selected is null ? null : ResolutionChoice.For(new Resolution(_selected.Staged.Width, _selected.Staged.Height));
        set
        {
            if (_selected is null || value is null || value == Resolution)
            {
                return;
            }

            var size = value.Size;

            var rates = DisplayModes.RefreshRates(_selected.Info.Modes, size);
            var refresh = DisplayModes.PickRefresh(rates, _selected.Staged.RefreshHz);
            var resized = DisplayArrangement.Resize(Placements(), _selected.Id, PrimaryId, size.Width, size.Height);
            Restage(resized, primaryId: PrimaryId, change: (id, setting) =>
                id == _selected.Id ? setting with { Width = size.Width, Height = size.Height, RefreshHz = refresh } : setting);
        }
    }

    /// <summary>The selected display's refresh rates at its staged resolution, fastest first.</summary>
    public IReadOnlyList<int> RefreshRates => _selected is null
        ? []
        : DisplayModes.RefreshRates(_selected.Info.Modes, new Resolution(_selected.Staged.Width, _selected.Staged.Height));

    public int? RefreshRate
    {
        get => _selected?.Staged.RefreshHz;
        set
        {
            if (_selected is null || value is not { } hz || hz == _selected.Staged.RefreshHz)
            {
                return;
            }

            _selected.Staged = _selected.Staged with { RefreshHz = hz };
            OnStagedChanged();
        }
    }

    /// <summary>The primary display (GNOME's "Primary Display" drop-down).</summary>
    public DisplayItem? Primary
    {
        get => Displays.FirstOrDefault(d => d.IsPrimary);
        set
        {
            if (value is null || value.IsPrimary)
            {
                return;
            }

            Restage(DisplayArrangement.MakePrimary(Placements(), value.Id), primaryId: value.Id, change: (_, setting) => setting);
        }
    }

    public string ScaleText => _selected is null ? "" : _selected.Info.ScalePercent.ToString(CultureInfo.CurrentCulture) + " %";

    /// <summary>True when there are changes not yet applied.</summary>
    public bool IsDirty => Displays.Any(d => d.Staged != d.Info.Current);

    /// <summary>True while an applied change waits for "Keep changes?".</summary>
    public bool IsWaiting => _countdown.State == KeepChangesState.Waiting;

    public string CountdownText => $"Settings will be reverted in {_secondsLeft} second{(_secondsLeft == 1 ? "" : "s")}.";

    public ICommand ApplyCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand KeepCommand { get; }

    public ICommand RevertCommand { get; }

    public ICommand ScaleCommand { get; }

    /// <summary>
    /// Moves a display in the preview to a proposed desktop position; it snaps flush to its neighbours, and the
    /// primary display stays at the origin.
    /// </summary>
    public void MoveDisplay(DisplayItem display, int left, int top)
    {
        var snap = Math.Max(display.Staged.Width, display.Staged.Height) / 20;
        var moved = DisplayArrangement.Move(Placements(), display.Id, left, top, snap);
        Restage(DisplayArrangement.MakePrimary(moved, PrimaryId), primaryId: PrimaryId, change: (_, setting) => setting);
    }

    /// <summary>Desktop pixels per preview pixel, for turning a drag into a position.</summary>
    public double DesktopPixelsPerPreviewPixel =>
        Displays.FirstOrDefault() is { } d && d.Preview.Width > 0 ? d.Staged.Width / d.Preview.Width : 1;

    public override void Dispose()
    {
        // Closing the window (which is also how WinGnome quits) reverts an unconfirmed change before returning.
        _disposing = true;
        base.Dispose();
    }

    protected override void Open()
    {
        _open = true;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Load();
    }

    protected override void Close()
    {
        _open = false;
        _generation++;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Stop();
        if (_countdown.Revert() && _pending is { } pending)
        {
            Log.Info("Displays panel closed during the countdown; reverting the display change");
            if (_disposing)
            {
                // The window is closing, possibly because WinGnome is quitting: revert now, before the process can exit.
                RevertNow(pending);
            }
            else
            {
                RevertInBackground(pending);
            }
        }

        Displays.Clear();
        _selected = null;
    }

    private string PrimaryId => (Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays[0]).Id;

    private List<DisplayPlacement> Placements() => Displays.Select(d => new DisplayPlacement(d.Id, d.Bounds)).ToList();

    /// <summary>Applies new bounds (and the primary flag) to the staged settings, then lets <paramref name="change"/> edit each.</summary>
    private void Restage(IReadOnlyList<DisplayPlacement> placements, string primaryId, Func<string, DisplaySetting, DisplaySetting> change)
    {
        foreach (var display in Displays)
        {
            var bounds = placements.First(p => p.Id == display.Id).Bounds;
            var moved = display.Staged with { X = bounds.Left, Y = bounds.Top, IsPrimary = display.Id == primaryId };
            display.Staged = change(display.Id, moved);
        }

        OnStagedChanged();
    }

    /// <summary>Reads the displays off the UI thread (mode lists take a while) and shows them, dropping staged changes.</summary>
    private void Load()
    {
        var generation = ++_generation;
        Task.Run(DisplayService.Read).ContinueWith(task => Context.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _generation || !_open)
            {
                return;
            }

            if (task.IsFaulted)
            {
                Log.Warn("Could not read the displays", task.Exception);
            }

            Show(task.IsFaulted ? [] : task.Result);
        }), TaskScheduler.Default);
    }

    private void Show(IReadOnlyList<DisplayInfo> displays)
    {
        Displays.Clear();
        var number = 1;
        foreach (var info in displays)
        {
            Displays.Add(new DisplayItem(info, number++));
        }

        if (Displays.Count == 0)
        {
            Problem = "WinGnome couldn't read the displays.";
        }

        _selected = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
        UpdatePreview();
        OnPropertyChanged(string.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Windows changed the displays (a monitor plugged in, another app): re-read unless the user is mid-change.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Context.Dispatcher.BeginInvoke(() =>
    {
        if (_open && !_isApplying && !IsWaiting && !IsDirty)
        {
            Load();
        }
    });

    private void OnSelectedChanged()
    {
        OnPropertyChanged(nameof(Resolutions));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(RefreshRates));
        OnPropertyChanged(nameof(RefreshRate));
        OnPropertyChanged(nameof(ScaleText));
    }

    private void OnStagedChanged()
    {
        UpdatePreview();
        OnSelectedChanged();
        OnPropertyChanged(nameof(Primary));
        OnPropertyChanged(nameof(IsDirty));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdatePreview()
    {
        var rects = DisplayArrangement.Fit(Placements(), PreviewWidth, PreviewHeight, PreviewPadding);
        for (var i = 0; i < Displays.Count; i++)
        {
            Displays[i].Preview = rects[i];
        }
    }

    private void Apply()
    {
        if (!DisplayArrangement.IsValid(Placements()))
        {
            Problem = "Displays must touch along an edge without overlapping. Drag them next to each other and try again.";
            return;
        }

        var target = Displays.Select(d => d.Staged).ToList();
        Problem = null;
        SetApplying(true);
        _writer.Run("apply the new display settings", () =>
        {
            var outcome = ApplyOnWorker(target);
            Context.Dispatcher.BeginInvoke(() => OnApplied(outcome));
            return true;
        }, () => SetApplying(false));
    }

    /// <summary>
    /// Re-reads the current settings (the panel's copy may be stale), tests the target, records both before the change,
    /// and applies the target for this session only.
    /// </summary>
    private (DisplayRevert? Pending, string? Problem) ApplyOnWorker(IReadOnlyList<DisplaySetting> target)
    {
        var original = DisplayService.Current();
        if (original.Count != target.Count
            || !target.All(t => original.Any(o => string.Equals(o.DeviceName, t.DeviceName, StringComparison.OrdinalIgnoreCase))))
        {
            return (null, "The displays changed while you were editing. Your changes were reset; try again.");
        }

        if (!DisplayService.Test(target))
        {
            return (null, "Windows can't show this display mode. Choose another resolution or refresh rate.");
        }

        var pending = new DisplayRevert(original, target);
        if (!DisplayRevertFile.Write(Context.SettingsDirectory, pending))
        {
            return (null, "WinGnome couldn't record the current display settings, so it didn't change them.");
        }

        var (applied, restored) = DisplayService.ApplyTemporarily(target, original);
        if (applied)
        {
            return (pending, null);
        }

        if (restored)
        {
            DisplayRevertFile.Delete(Context.SettingsDirectory);
            return (null, "Windows didn't accept the new display settings, so nothing changed.");
        }

        return (null, "Windows didn't accept the new display settings and WinGnome couldn't put the old ones back. It will try again when it next starts; you can also fix them in Windows Settings.");
    }

    private void OnApplied((DisplayRevert? Pending, string? Problem) outcome)
    {
        SetApplying(false);
        if (outcome.Pending is not { } pending)
        {
            Problem = outcome.Problem;
            if (_open)
            {
                Load();
            }

            return;
        }

        _pending = pending;
        _countdown.Start(DateTime.UtcNow);
        if (!_open)
        {
            // The panel closed while the change was being applied: nobody can confirm it.
            Log.Info("Displays panel closed while applying; reverting the display change");
            _countdown.Revert();
            RevertInBackground(pending);
            return;
        }

        _secondsLeft = _countdown.SecondsLeft(DateTime.UtcNow);
        _timer.Start();
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(CountdownText));
        CommandManager.InvalidateRequerySuggested();
        Load();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_countdown.Tick(now))
        {
            Log.Info("Display change not confirmed in time; reverting");
            AfterCountdown();
            return;
        }

        _secondsLeft = _countdown.SecondsLeft(now);
        OnPropertyChanged(nameof(CountdownText));
    }

    private void Keep()
    {
        if (!_countdown.Keep() || _pending is not { } pending)
        {
            return;
        }

        _timer.Stop();
        _pending = null;
        OnPropertyChanged(nameof(IsWaiting));
        CommandManager.InvalidateRequerySuggested();
        Log.Info("Display change kept");
        _writer.Run("save the kept display settings", () =>
        {
            var saved = DisplayService.Persist(pending.Target);
            DisplayRevertFile.Delete(Context.SettingsDirectory);
            return saved;
        }, () => Problem = "The new display settings are showing, but Windows didn't save them; they'll be undone when you sign out.");
    }

    private void Revert()
    {
        if (_countdown.Revert())
        {
            AfterCountdown();
        }
    }

    private void AfterCountdown()
    {
        _timer.Stop();
        OnPropertyChanged(nameof(IsWaiting));
        CommandManager.InvalidateRequerySuggested();
        if (_pending is { } pending)
        {
            RevertInBackground(pending);
        }
    }

    /// <summary>Puts back the settings from before the change on the writer thread, then re-reads if the panel is open.</summary>
    private void RevertInBackground(DisplayRevert pending)
    {
        _pending = null;
        SetApplying(true);
        _writer.Run("revert the display settings", () =>
        {
            var restored = RevertAndForget(pending);
            Context.Dispatcher.BeginInvoke(() =>
            {
                SetApplying(false);
                if (_open)
                {
                    Load();
                }
            });
            return restored;
        }, () => Problem = "WinGnome couldn't put the previous display settings back. It will try again when it next starts.");
    }

    private void RevertNow(DisplayRevert pending)
    {
        _pending = null;
        RevertAndForget(pending);
    }

    /// <summary>Reverts and deletes the record only when the displays really show the original again.</summary>
    private bool RevertAndForget(DisplayRevert pending)
    {
        var restored = DisplayService.Revert(pending.Original);
        if (restored)
        {
            DisplayRevertFile.Delete(Context.SettingsDirectory);
        }

        return restored;
    }

    private void SetApplying(bool applying)
    {
        _isApplying = applying;
        CommandManager.InvalidateRequerySuggested();
    }
}