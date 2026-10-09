using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinGnome.Core.TopBar;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>One notification-area icon in the bar.</summary>
internal sealed class TrayIconViewModel : ObservableObject
{
    private TrayIconState _state;
    private BitmapSource? _image;
    private int _slotPx;
    private double _scale = 1;
    private TrayIconPlacement _placement;

    public TrayIconViewModel(TrayIconState state)
    {
        _state = state;
    }

    public TrayIconState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    /// <summary>The icon at its own pixel size and 96 DPI, so one bitmap pixel is one DIP before placement.</summary>
    public BitmapSource? Image
    {
        get => _image;
        set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                UpdatePlacement();
            }
        }
    }

    /// <summary>Edge length the image is drawn at, in DIPs: a whole number of device pixels (see TrayIconPlacement).</summary>
    public double ImageSize => _placement.SizePx / _scale;

    /// <summary>Offset of the image inside its slot, so a smaller icon sits centred on whole device pixels.</summary>
    public Thickness ImageMargin => new(_placement.OffsetPx / _scale, _placement.OffsetPx / _scale, 0, 0);

    /// <summary>Whole-number upscales keep hard pixel edges; downscales are filtered.</summary>
    public BitmapScalingMode ScalingMode =>
        _image is { } image && _placement.SizePx > image.PixelWidth ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;

    /// <summary>Hidden icons (NIS_HIDDEN) and icons whose image could not be read take no space.</summary>
    public bool IsVisible => _state.IsVisible && _image is not null;

    /// <summary>The standard tooltip, or null when the app draws its own pop-up (version 4 without NIF_SHOWTIP).</summary>
    public string? ToolTip => _state.ShowsToolTip ? _state.Tip : null;

    /// <summary>True while NIN_POPUPOPEN has been sent without its NIN_POPUPCLOSE.</summary>
    public bool PopupOpen { get; set; }

    /// <summary>Sets the square slot the icon is drawn in: <paramref name="slotPx"/> device pixels on a monitor at <paramref name="scale"/>.</summary>
    public void SetSlot(int slotPx, double scale)
    {
        _slotPx = slotPx;
        _scale = scale > 0 ? scale : 1;
        UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        _placement = TrayIconPlacement.Choose(_image?.PixelWidth ?? 0, _slotPx);
        OnPropertyChanged(nameof(ImageSize));
        OnPropertyChanged(nameof(ImageMargin));
        OnPropertyChanged(nameof(ScalingMode));
    }
}
