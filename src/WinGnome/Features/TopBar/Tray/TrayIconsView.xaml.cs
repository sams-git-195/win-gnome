using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>The bar's notification area: turns WPF pointer events on each icon into tray callbacks.</summary>
internal sealed partial class TrayIconsView : UserControl
{
    public TrayIconsView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when an icon is pressed, so the bar can close its own popups before the app opens its menu.</summary>
    public event EventHandler? IconPressed;

    private void OnIconMouseEnter(object sender, MouseEventArgs e) => Send(sender, TrayPointerAction.Enter);

    private void OnIconMouseLeave(object sender, MouseEventArgs e) => Send(sender, TrayPointerAction.Leave);

    private void OnIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Handled: the bar's empty-space handler would otherwise also run and move focus back to the previous window.
        e.Handled = true;
        IconPressed?.Invoke(this, EventArgs.Empty);

        // Like Win32, the second press of a double click is reported as the double click itself.
        var doubleClick = e.ClickCount == 2;
        var action = e.ChangedButton switch
        {
            MouseButton.Left => doubleClick ? TrayPointerAction.LeftDoubleClick : TrayPointerAction.LeftDown,
            MouseButton.Right => doubleClick ? TrayPointerAction.RightDoubleClick : TrayPointerAction.RightDown,
            MouseButton.Middle => doubleClick ? TrayPointerAction.MiddleDoubleClick : TrayPointerAction.MiddleDown,
            _ => (TrayPointerAction?)null,
        };
        if (action is { } a)
        {
            Send(sender, a);
        }
    }

    private void OnIconMouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var action = e.ChangedButton switch
        {
            MouseButton.Left => TrayPointerAction.LeftUp,
            MouseButton.Right => TrayPointerAction.RightUp,
            MouseButton.Middle => TrayPointerAction.MiddleUp,
            _ => (TrayPointerAction?)null,
        };
        if (action is { } a)
        {
            Send(sender, a);
        }
    }

    private void Send(object sender, TrayPointerAction action)
    {
        if (sender is FrameworkElement { DataContext: TrayIconViewModel icon } element && DataContext is TrayViewModel tray
            && PresentationSource.FromVisual(element) is not null)
        {
            tray.Send(icon, action, ScreenBounds(element));
        }
    }

    /// <summary>The element's rectangle in physical screen pixels (PointToScreen returns device pixels).</summary>
    private static PixelRect ScreenBounds(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new PixelRect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y));
    }
}
