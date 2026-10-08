using System.Windows;
using System.Windows.Media;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// The four-pane Windows logo, drawn as vector rectangles snapped to whole device pixels so the panes and the gaps
/// between them stay sharp at every size and DPI (a font glyph or a stretched path blurs the one-pixel gaps).
/// </summary>
internal sealed class WindowsLogo : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(WindowsLogo),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WindowsLogo),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Edge length of the logo in DIPs.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        // Work in device pixels: the gap is about 1/16 of the logo (as in the real mark) but never under one pixel,
        // and both panes get the same whole number of pixels.
        var totalPx = Math.Max(4, Math.Round(Size * scale));
        var gapPx = Math.Max(1, Math.Round(totalPx / 16));
        var panePx = Math.Floor((totalPx - gapPx) / 2);
        var pane = panePx / scale;
        var second = (panePx + gapPx) / scale;

        // Centre the snapped mark in the layout box, keeping the offset on the pixel grid.
        var offset = Math.Floor((totalPx - ((2 * panePx) + gapPx)) / 2) / scale;
        drawingContext.DrawRectangle(Fill, null, new Rect(offset, offset, pane, pane));
        drawingContext.DrawRectangle(Fill, null, new Rect(offset + second, offset, pane, pane));
        drawingContext.DrawRectangle(Fill, null, new Rect(offset, offset + second, pane, pane));
        drawingContext.DrawRectangle(Fill, null, new Rect(offset + second, offset + second, pane, pane));
    }
}
