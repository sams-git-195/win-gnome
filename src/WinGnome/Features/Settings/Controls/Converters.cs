using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WinGnome.Features.Settings.Controls;

/// <summary>Shared value converters, reachable from XAML with <c>x:Static</c> so user controls need no resource lookups.</summary>
internal static class Converters
{
    public static readonly BooleanToVisibilityConverter BoolToVisibility = new();

    /// <summary>Visible when the bound value is non-null, collapsed otherwise.</summary>
    public static readonly IValueConverter NotNullToVisibility = new NotNullToVisibilityConverter();

    private sealed class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
