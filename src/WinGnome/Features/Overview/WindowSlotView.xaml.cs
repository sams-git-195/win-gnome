using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinGnome.Features.Overview;

/// <summary>
/// Hit-testing, highlight ring, title and close button for one window thumbnail in the overview.
/// The view is laid out around the thumbnail: <see cref="FramePadding"/> on every side, plus
/// <see cref="CaptionHeight"/> underneath.
/// </summary>
internal sealed partial class WindowSlotView : UserControl
{
    /// <summary>Gap between the thumbnail and the highlight ring, in DIPs.</summary>
    public const double FramePadding = 6;

    /// <summary>Height of the title row under the thumbnail, in DIPs.</summary>
    public const double CaptionHeight = 34;

    private static readonly Brush TitleHighlight = CreateFrozenBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private bool _pressed;
    private bool _isSelected;

    public WindowSlotView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the thumbnail is clicked.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised by the close button or a middle click.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the pointer enters the slot, so keyboard selection can follow the mouse.</summary>
    public event EventHandler? Hovered;

    public void SetContent(string title, ImageSource? icon, double maxTitleWidth)
    {
        TitleText.Text = title;
        TitleText.MaxWidth = Math.Max(40, maxTitleWidth);
        AppIcon.Source = icon;
        PlaceholderIcon.Source = icon;
    }

    /// <summary>Keyboard or hover selection: shows the accent ring and the close button.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            if (value)
            {
                Frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                TitlePill.Background = TitleHighlight;
            }
            else
            {
                Frame.BorderBrush = Brushes.Transparent;
                TitlePill.Background = Brushes.Transparent;
            }

            CloseButton.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Hovered?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            // Handled, so the overview does not treat this as a click on the empty background.
            _pressed = true;
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        e.Handled = true;
        if (e.ChangedButton == MouseButton.Left)
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
