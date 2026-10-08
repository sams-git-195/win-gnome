using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinGnome.Features.Settings.Controls;

/// <summary>
/// One row of a boxed list: a title and optional subtitle on the left and the control (the row's content) on the right.
/// <see cref="Details"/> adds extra content under the subtitle, and <see cref="IsStacked"/> moves the content below the
/// text for wide editors. The row gives its first input control the row title as its accessible name.
/// </summary>
internal sealed class SettingsRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsRow));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SettingsRow));

    public static readonly DependencyProperty DetailsProperty =
        DependencyProperty.Register(nameof(Details), typeof(object), typeof(SettingsRow));

    public static readonly DependencyProperty IsStackedProperty =
        DependencyProperty.Register(nameof(IsStacked), typeof(bool), typeof(SettingsRow));

    public SettingsRow()
    {
        Loaded += (_, _) => NameInputControl();
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Extra content shown under the subtitle.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    /// <summary>Places the content under the title instead of on the right.</summary>
    public bool IsStacked
    {
        get => (bool)GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    private void NameInputControl()
    {
        if (string.IsNullOrEmpty(Title))
        {
            return;
        }

        var input = FindInput(this);
        if (input is not null && string.IsNullOrEmpty(AutomationProperties.GetName(input)))
        {
            AutomationProperties.SetName(input, Title);
        }
    }

    private static DependencyObject? FindInput(DependencyObject root)
    {
        if (root is Slider or ComboBox or ToggleButton or TextBox or ListBox)
        {
            return root;
        }

        var count = root is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (var i = 0; i < count; i++)
        {
            var found = FindInput(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
