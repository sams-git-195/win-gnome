using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinGnome.Core.TopBar;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>
/// One notification-area icon in one bar: the shared <see cref="TrayIconEntry"/> plus this bar's slot size and DPI
/// scale (bars on monitors with different DPIs draw the same bitmap differently).
/// </summary>
internal sealed class TrayIconViewModel : ObservableObject, IDisposable
{
    private int _slotPx;
    private double _scale = 1;
    private TrayIconPlacement _placement;

    public TrayIconViewModel(TrayIconEntry entry)
    {
        Entry = entry;
        Entry.PropertyChanged += OnEntryChanged;
        UpdatePlacement();
    }

    public TrayIconEntry Entry { get; }

    public TrayIconState State => Entry.State;

    /// <summary>The icon at its own pixel size and 96 DPI, so one bitmap pixel is one DIP before placement.</summary>
    public BitmapSource? Image => Entry.Image;

    /// <summary>Edge length the image is drawn at, in DIPs: a whole number of device pixels (see TrayIconPlacement).</summary>
    public double ImageSize => _placement.SizePx / _scale;

    /// <summary>Offset of the image inside its slot, so a smaller icon sits centred on whole device pixels.</summary>
    public Thickness ImageMargin => new(_placement.OffsetPx / _scale, _placement.OffsetPx / _scale, 0, 0);

    /// <summary>Whole-number upscales keep hard pixel edges; downscales are filtered.</summary>
    public BitmapScalingMode ScalingMode =>
        Image is { } image && _placement.SizePx > image.PixelWidth ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;

    /// <summary>Hidden icons (NIS_HIDDEN) and icons whose image could not be read take no space.</summary>
    public bool IsVisible => State.IsVisible && Image is not null;

    /// <summary>The standard tooltip, or null when the app draws its own pop-up (version 4 without NIF_SHOWTIP).</summary>
    public string? ToolTip => State.ShowsToolTip ? State.Tip : null;

    /// <summary>Sets the square slot the icon is drawn in: <paramref name="slotPx"/> device pixels on a monitor at <paramref name="scale"/>.</summary>
    public void SetSlot(int slotPx, double scale)
    {
        _slotPx = slotPx;
        _scale = scale > 0 ? scale : 1;
        UpdatePlacement();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TrayIconEntry.State):
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(ToolTip));
                break;
            case nameof(TrayIconEntry.Image):
                OnPropertyChanged(nameof(Image));
                OnPropertyChanged(nameof(IsVisible));
                UpdatePlacement();
                break;
        }
    }

    private void UpdatePlacement()
    {
        _placement = TrayIconPlacement.Choose(Image?.PixelWidth ?? 0, _slotPx);
        OnPropertyChanged(nameof(ImageSize));
        OnPropertyChanged(nameof(ImageMargin));
        OnPropertyChanged(nameof(ScalingMode));
    }

    public void Dispose() => Entry.PropertyChanged -= OnEntryChanged;
}
