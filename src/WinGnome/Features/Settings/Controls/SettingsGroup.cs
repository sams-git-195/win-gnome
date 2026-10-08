using System.Windows;
using System.Windows.Controls;

namespace WinGnome.Features.Settings.Controls;

/// <summary>
/// A titled "boxed list": a rounded card holding <see cref="SettingsRow"/>s separated by hairlines,
/// with an optional header above and an explanatory <see cref="Description"/> below.
/// </summary>
internal sealed class SettingsGroup : ItemsControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsGroup));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsGroup));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
