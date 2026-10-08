using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinGnome.Features.Settings.Controls;

/// <summary>
/// Hex colour editor with validation, a live swatch and preset swatches. Set <see cref="FrameworkElement.DataContext"/> to a
/// <c>ColorEditor</c>. For optional colours, <see cref="DefaultBrush"/> is what the swatch shows while the default applies.
/// </summary>
internal sealed partial class ColorPickerRow : UserControl
{
    public static readonly DependencyProperty DefaultBrushProperty =
        DependencyProperty.Register(nameof(DefaultBrush), typeof(Brush), typeof(ColorPickerRow));

    public static readonly DependencyProperty DefaultLabelProperty =
        DependencyProperty.Register(nameof(DefaultLabel), typeof(string), typeof(ColorPickerRow), new PropertyMetadata("Default"));

    public ColorPickerRow()
    {
        InitializeComponent();
    }

    /// <summary>Brush of the swatch while an optional colour is unset (for example the accent colour).</summary>
    public Brush? DefaultBrush
    {
        get => (Brush?)GetValue(DefaultBrushProperty);
        set => SetValue(DefaultBrushProperty, value);
    }

    /// <summary>Text of the button that clears an optional colour.</summary>
    public string DefaultLabel
    {
        get => (string)GetValue(DefaultLabelProperty);
        set => SetValue(DefaultLabelProperty, value);
    }
}
