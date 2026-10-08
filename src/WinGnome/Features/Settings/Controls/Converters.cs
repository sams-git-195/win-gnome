using System.Windows.Controls;

namespace WinGnome.Features.Settings.Controls;

/// <summary>Shared value converters, reachable from XAML with <c>x:Static</c> so user controls need no resource lookups.</summary>
internal static class Converters
{
    public static readonly BooleanToVisibilityConverter BoolToVisibility = new();
}
