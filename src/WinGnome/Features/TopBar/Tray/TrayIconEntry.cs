using System.Windows.Media.Imaging;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>
/// One notification-area icon, shared by every bar: its state and frozen bitmap (converted once), and whether a
/// version 4 pop-up is open for it. Bars wrap it in a <see cref="TrayIconViewModel"/> each.
/// </summary>
internal sealed class TrayIconEntry : ObservableObject
{
    private TrayIconState _state;
    private BitmapSource? _image;

    public TrayIconEntry(TrayIconState state)
    {
        _state = state;
    }

    public TrayIconState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    /// <summary>The icon at its own pixel size and 96 DPI, frozen so every bar can draw it.</summary>
    public BitmapSource? Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    /// <summary>True while NIN_POPUPOPEN has been sent without its NIN_POPUPCLOSE (whichever bar it was sent from).</summary>
    public bool PopupOpen { get; set; }
}
