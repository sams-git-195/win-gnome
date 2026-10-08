using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>The clock's popup: big date header, month calendar, and shortcuts to notifications and date settings.</summary>
internal sealed partial class CalendarCard : UserControl
{
    public CalendarCard()
    {
        InitializeComponent();
    }

    /// <summary>Raised when one of the card's buttons asks for an action.</summary>
    public event EventHandler<TopBarAction>? ActionRequested;

    /// <summary>Resets the card to today; called every time it opens.</summary>
    public void ShowToday()
    {
        var today = DateTime.Today;
        var culture = CultureInfo.CurrentCulture;
        WeekdayText.Text = CalendarHeader.Weekday(today, culture);
        DateText.Text = CalendarHeader.LongDate(today, culture);
        MonthCalendar.DisplayMode = CalendarMode.Month;
        MonthCalendar.SelectedDate = null;
        MonthCalendar.DisplayDate = today;
    }

    // WPF's Calendar keeps mouse capture after a day or month is clicked, which would swallow the next click
    // anywhere else (including the click that should dismiss the popup). Release it once the click is done.
    private void OnCalendarPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Mouse.Captured is CalendarItem or CalendarDayButton or CalendarButton)
        {
            Mouse.Capture(null);
        }
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TopBarAction action })
        {
            ActionRequested?.Invoke(this, action);
        }
    }
}
