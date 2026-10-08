using System.Windows;
using System.Windows.Controls;
using WinGnome.Core.Geometry;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// Attached <c>Radius</c> for a <see cref="Border"/>: rounds its corners by the requested radius, capped at half
/// its shorter side and refitted whenever it resizes. Setting <see cref="Border.CornerRadius"/> directly with a
/// large value gives oval ends instead of a pill (see <see cref="CornerRadiusFit"/>).
/// </summary>
internal static class PillCorners
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(PillCorners), new PropertyMetadata(0.0, OnRadiusChanged));

    public static double GetRadius(Border border) => (double)border.GetValue(RadiusProperty);

    public static void SetRadius(Border border, double value) => border.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
        {
            return;
        }

        border.SizeChanged -= OnSizeChanged;
        border.SizeChanged += OnSizeChanged;
        Apply(border);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((Border)sender);

    private static void Apply(Border border) =>
        border.CornerRadius = new CornerRadius(CornerRadiusFit.Fit(GetRadius(border), border.ActualWidth, border.ActualHeight));
}
