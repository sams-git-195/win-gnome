using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>The Displays panel. Dragging a display tile only moves it visually; the drop asks the view model where it lands.</summary>
internal sealed partial class DisplaysPanel : UserControl
{
    private Point _dragStart;
    private bool _dragging;

    public DisplaysPanel() => InitializeComponent();

    private DisplaysPanelViewModel? ViewModel => DataContext as DisplaysPanelViewModel;

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: DisplayItem display } tile || ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Selected = display;
        _dragStart = e.GetPosition(Arrangement);
        _dragging = tile.CaptureMouse();
        e.Handled = true;
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || sender is not Border { RenderTransform: TranslateTransform shift })
        {
            return;
        }

        var offset = e.GetPosition(Arrangement) - _dragStart;
        shift.X = offset.X;
        shift.Y = offset.Y;
    }

    private void OnTileMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || sender is not Border { DataContext: DisplayItem display } tile || ViewModel is not { } viewModel)
        {
            return;
        }

        var offset = e.GetPosition(Arrangement) - _dragStart;
        EndDrag(tile);
        if (Math.Abs(offset.X) < 2 && Math.Abs(offset.Y) < 2)
        {
            return;
        }

        var scale = viewModel.DesktopPixelsPerPreviewPixel;
        viewModel.MoveDisplay(display,
            display.Staged.X + (int)Math.Round(offset.X * scale),
            display.Staged.Y + (int)Math.Round(offset.Y * scale));
    }

    private void OnTileLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is Border tile)
        {
            EndDrag(tile);
        }
    }

    private void EndDrag(Border tile)
    {
        _dragging = false;
        if (tile.RenderTransform is TranslateTransform shift)
        {
            shift.X = 0;
            shift.Y = 0;
        }

        if (tile.IsMouseCaptured)
        {
            tile.ReleaseMouseCapture();
        }
    }
}
