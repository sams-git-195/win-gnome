using System.Collections.ObjectModel;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.RemovableMedia;

/// <summary>One media type's drop-down. It shows what Windows holds; picking a choice asks the panel to write it.</summary>
internal sealed class MediaRowViewModel : ObservableObject
{
    private readonly Action<MediaRowViewModel, AutoplayChoice> _choose;
    private AutoplayChoice _current;
    private IReadOnlyList<AutoplayChoice> _choices;

    public MediaRowViewModel(AutoplayRow row, Action<MediaRowViewModel, AutoplayChoice> choose)
    {
        EventId = row.EventId;
        Label = row.Label;
        _choices = row.Choices;
        _current = row.Selected;
        _choose = choose;
    }

    public string EventId { get; }

    public string Label { get; }

    public IReadOnlyList<AutoplayChoice> Choices => _choices;

    /// <summary>
    /// Shows what Windows holds now. The row is updated in place rather than replaced, so a keyboard or screen-reader
    /// user's focus stays on the drop-down they just used.
    /// </summary>
    public void Update(AutoplayRow row)
    {
        _choices = row.Choices;
        _current = row.Selected;
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>
    /// The current choice. The setter never stores the new value: it asks for a write, and the panel then re-reads
    /// Windows and replaces the row, so a refused write snaps back instead of showing a choice Windows doesn't hold.
    /// </summary>
    public AutoplayChoice Selected
    {
        get => _current;
        set
        {
            if (value is not null && !value.Equals(_current))
            {
                _choose(this, value);
            }
        }
    }
}

/// <summary>
/// Removable Media: whether AutoPlay is on at all and, per media type, what Windows does when it appears (ask, do
/// nothing, open the folder or start an installed handler). Everything lives in HKCU; there is nothing to restore.
/// Changes are verified-set: written, read back, and shown as Windows holds them.
/// </summary>
internal sealed class RemovableMediaPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private bool _autoplayOff;
    private bool _isBusy;

    public RemovableMediaPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.RemovableMedia)
    {
        _writer = context.CreateWriter();
    }

    public ObservableCollection<MediaRowViewModel> Rows { get; } = [];

    /// <summary>True while a read or a change is in flight; the controls wait.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChange));
                OnPropertyChanged(nameof(CanChooseHandlers));
            }
        }
    }

    /// <summary>True when a setting can be changed now (not in safe mode, nothing in flight).</summary>
    public bool CanChange => CanEdit && !IsBusy;

    /// <summary>The per-media drop-downs apply only while AutoPlay is on.</summary>
    public bool CanChooseHandlers => CanChange && !_autoplayOff;

    /// <summary>
    /// "Never prompt or start programs on media insertion" (AutoPlay off). Verified-set: the field changes only when
    /// the value read back says so.
    /// </summary>
    public bool AutoplayOff
    {
        get => _autoplayOff;
        set
        {
            if (value == _autoplayOff || !CanChange)
            {
                OnPropertyChanged();
                return;
            }

            Change($"turn AutoPlay {(value ? "off" : "on")}", () => AutoplayStore.WriteDisabled(value), "AutoPlay");
        }
    }

    protected override void Open() => Reload();

    protected override void Close() => Rows.Clear();

    private void Reload()
    {
        IsBusy = true;
        LoadAsync(AutoplayStore.Read, Show);
    }

    private void Show(AutoplayState state)
    {
        _autoplayOff = state.Disabled;
        if (Rows.Select(r => r.EventId).SequenceEqual(state.Rows.Select(r => r.EventId)))
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].Update(state.Rows[i]);
            }
        }
        else
        {
            Rows.Clear();
            foreach (var row in state.Rows)
            {
                Rows.Add(new MediaRowViewModel(row, Choose));
            }
        }

        OnPropertyChanged(nameof(AutoplayOff));
        IsBusy = false;
        OnPropertyChanged(nameof(CanChooseHandlers));
    }

    private void Choose(MediaRowViewModel row, AutoplayChoice choice)
    {
        if (!CanChooseHandlers)
        {
            return;
        }

        Change($"set \"{choice.Label}\" for {row.Label}", () => AutoplayStore.WriteChoice(row.EventId, choice.HandlerId), row.Label);
    }

    /// <summary>Runs a change on the writer thread, then reads everything again so the panel shows what Windows holds.</summary>
    private void Change(string what, Func<bool> write, string failureLabel)
    {
        Problem = null;
        IsBusy = true;
        _writer.Run(what, () =>
        {
            try
            {
                return write();
            }
            finally
            {
                // Success, refusal or exception: the panel shows the real state either way.
                Context.Dispatcher.BeginInvoke(Reload);
            }
        }, () => ReportWriteFailure(failureLabel));
    }
}
