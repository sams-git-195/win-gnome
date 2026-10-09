using System.Collections.ObjectModel;
using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>
/// One bar's view of the shared <see cref="TrayModel"/>: its own icon view models (slot size and DPI scale are per
/// bar), mirrored from the model's structure changes, and its own strip for pointer events. The model is the single
/// source of order; state changes reach every bar through the shared entries.
/// </summary>
internal sealed class TrayBarIcons : ObservableObject, IDisposable
{
    private readonly TrayModel _model;
    private int _iconSlotPx;
    private double _iconScale = 1;

    public TrayBarIcons(TrayModel model)
    {
        _model = model;
        foreach (var entry in model.Entries)
        {
            Icons.Add(Wrap(entry));
        }

        _model.Changed += OnModelChanged;
        _model.EnabledChanged += OnEnabledChanged;
    }

    /// <summary>Icons in the order they were added, including hidden ones (the view collapses those).</summary>
    public ObservableCollection<TrayIconViewModel> Icons { get; } = [];

    public bool IsEnabled => _model.IsEnabled;

    /// <summary>This bar's strip in physical pixels (anchors version 4 menus under this bar).</summary>
    public PixelRect BarBounds { get; set; }

    /// <summary>Size of every icon's square slot: <paramref name="slotPx"/> device pixels on a monitor at <paramref name="scale"/>.</summary>
    public void SetIconSlot(int slotPx, double scale)
    {
        _iconSlotPx = slotPx;
        _iconScale = scale;
        foreach (var icon in Icons)
        {
            icon.SetSlot(slotPx, scale);
        }
    }

    /// <summary>Delivers a pointer event on <paramref name="icon"/>, whose on-screen rectangle is <paramref name="iconBounds"/>.</summary>
    public void Send(TrayIconViewModel icon, TrayPointerAction action, PixelRect iconBounds) =>
        _model.Send(icon.Entry, action, iconBounds, BarBounds);

    private TrayIconViewModel Wrap(TrayIconEntry entry)
    {
        var icon = new TrayIconViewModel(entry);
        icon.SetSlot(_iconSlotPx, _iconScale);
        return icon;
    }

    private void OnModelChanged(object? sender, TrayStructureChange change)
    {
        switch (change.Kind)
        {
            case TrayChangeKind.Added when change.Entry is not null:
                Icons.Insert(Math.Min(change.Index, Icons.Count), Wrap(change.Entry));
                break;
            case TrayChangeKind.Removed when change.Index < Icons.Count:
                var removed = Icons[change.Index];
                Icons.RemoveAt(change.Index);
                removed.Dispose();
                break;
        }
    }

    private void OnEnabledChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsEnabled));

    public void Dispose()
    {
        _model.Changed -= OnModelChanged;
        _model.EnabledChanged -= OnEnabledChanged;
        foreach (var icon in Icons)
        {
            icon.Dispose();
        }

        Icons.Clear();
    }
}
