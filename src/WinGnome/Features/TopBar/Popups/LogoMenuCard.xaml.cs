using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>
/// The Windows-logo menu, modelled on the macOS Apple menu: about, settings, Start and Task Manager, power, session
/// and quit. Every row raises a <see cref="TopBarAction"/>, so power actions share the quick-settings code path
/// (including its confirmation dialogs and self-test guard).
/// </summary>
internal sealed partial class LogoMenuCard : UserControl
{
    private readonly List<Button> _items;

    public LogoMenuCard()
    {
        InitializeComponent();
        LogOutItem.Content = $"Log Out {NativeMethods.GetUserDisplayName()}…";
        _items = Items.Children.OfType<Button>().ToList();
        foreach (var item in _items)
        {
            item.Click += OnItemClick;
            item.MouseEnter += OnItemMouseEnter;
            item.MouseLeave += OnItemMouseLeave;
        }
    }

    /// <summary>Raised when a row is chosen.</summary>
    public event EventHandler<TopBarAction>? ActionRequested;

    /// <summary>Called whenever the menu opens: nothing selected, arrow keys ready.</summary>
    public void Reset() => Focus();

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TopBarAction action })
        {
            ActionRequested?.Invoke(this, action);
        }
    }

    // The pointer and the arrow keys share one selection, as in a native menu.
    private void OnItemMouseEnter(object sender, MouseEventArgs e) => ((Button)sender).Focus();

    private void OnItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (((Button)sender).IsKeyboardFocused)
        {
            Focus();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        e.Handled = true;
        var current = _items.FindIndex(item => item.IsKeyboardFocused);
        var next = current < 0
            ? (step > 0 ? 0 : _items.Count - 1)
            : (current + step + _items.Count) % _items.Count;
        _items[next].Focus();
    }
}
