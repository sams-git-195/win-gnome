using System.Windows.Media;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>One notification-area icon in the bar.</summary>
internal sealed class TrayIconViewModel : ObservableObject
{
    private TrayIconState _state;
    private ImageSource? _image;

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

    public ImageSource? Image
    {
        get => _image;
        set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    /// <summary>Hidden icons (NIS_HIDDEN) and icons whose image could not be read take no space.</summary>
    public bool IsVisible => _state.IsVisible && _image is not null;

    /// <summary>The standard tooltip, or null when the app draws its own pop-up (version 4 without NIF_SHOWTIP).</summary>
    public string? ToolTip => _state.ShowsToolTip ? _state.Tip : null;

    /// <summary>True while NIN_POPUPOPEN has been sent without its NIN_POPUPCLOSE.</summary>
    public bool PopupOpen { get; set; }
}
