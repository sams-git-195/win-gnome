using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// Opens dock context menus next to an item and makes them behave on a window that never activates.
/// </summary>
/// <remarks>
/// A WPF menu only notices clicks elsewhere, and only receives Escape and arrow keys, when its popup window
/// is the foreground window; the dock window never is (it is WS_EX_NOACTIVATE). This is the same rule
/// TrackPopupMenu documents for notification-area menus, and the fix is the same: bring the menu's own window
/// to the foreground when it opens. The user's window is re-activated when the menu closes without anything
/// else having taken the foreground, so the dock never leaves focus "lost".
/// </remarks>
internal sealed class DockMenuPresenter
{
    private const double Gap = 8;

    private readonly Dispatcher _dispatcher;
    private ContextMenu? _menu;
    private nint _restoreTo;

    public DockMenuPresenter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>Raised with true when a menu opens and false when it closes.</summary>
    public event EventHandler<bool>? OpenChanged;

    /// <summary>Opens <paramref name="menu"/> beside <paramref name="target"/>, on the side away from the screen edge.</summary>
    /// <param name="menu">The menu to show.</param>
    /// <param name="target">The dock item the menu belongs to.</param>
    /// <param name="position">Dock edge.</param>
    /// <param name="restoreTo">The user's window, re-activated when the menu closes without another app taking focus.</param>
    public void Show(ContextMenu menu, FrameworkElement target, DockPosition position, nint restoreTo)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(target);
        Close();

        _menu = menu;
        _restoreTo = restoreTo;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) => [Place(popupSize, targetSize, position)];
        menu.Opened += OnOpened;
        menu.Closed += OnClosed;
        OpenChanged?.Invoke(this, true);
        menu.IsOpen = true;
    }

    public void Close()
    {
        if (_menu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }
    }

    private static CustomPopupPlacement Place(Size popup, Size target, DockPosition position) => position switch
    {
        DockPosition.Left => new CustomPopupPlacement(new Point(target.Width + Gap, (target.Height - popup.Height) / 2), PopupPrimaryAxis.Vertical),
        DockPosition.Right => new CustomPopupPlacement(new Point(-popup.Width - Gap, (target.Height - popup.Height) / 2), PopupPrimaryAxis.Vertical),
        _ => new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height - Gap), PopupPrimaryAxis.Horizontal),
    };

    private void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu
            && PresentationSource.FromVisual(menu) is HwndSource source
            && !NativeMethods.SetForegroundWindow(source.Handle))
        {
            // The menu still works with the mouse; it just will not close on clicks in other apps.
            Log.Warn("Dock: could not give the context menu the foreground");
        }
    }

    private void OnClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Opened -= OnOpened;
            menu.Closed -= OnClosed;
        }

        if (!ReferenceEquals(sender, _menu))
        {
            return;
        }

        _menu = null;
        var restoreTo = _restoreTo;
        _restoreTo = 0;
        OpenChanged?.Invoke(this, false);

        // After the clicked command has run: if it activated something (a window, a new app), leave that alone.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => RestoreForeground(restoreTo));
    }

    private void RestoreForeground(nint restoreTo)
    {
        // Another menu opened in the meantime (Show closes the previous one): it needs the foreground itself.
        if (_menu is not null)
        {
            return;
        }

        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if ((foreground == 0 || NativeMethods.IsOwnWindow(foreground))
                && restoreTo != 0 && NativeMethods.IsWindow(restoreTo) && !NativeMethods.IsIconic(restoreTo))
            {
                WindowActivator.Activate(restoreTo);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: could not restore the foreground window after a menu", ex);
        }
    }
}
