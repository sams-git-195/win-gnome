using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// The mark in the top bar's logo button (spec 0021), replacing the old hard-coded <c>WindowsLogo</c>. One control
/// draws all three kinds: the four-pane Windows mark (device-pixel pane arithmetic, unchanged), a vector mark from
/// <c>Theme/LogoMarks.xaml</c> (snapped to whole pixels like <see cref="SymbolicIcon"/>), or a custom image rendered
/// as a solid silhouette in <see cref="Fill"/> through its mask. The selection is resolved by Core's
/// <see cref="LogoSelection"/> and handed in by the caller; a missing mark or mask falls back to the Windows panes so
/// the logo is never blank.
/// </summary>
internal sealed class LogoGlyph : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LogoGlyph),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LogoGlyph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(LogoKind), typeof(LogoGlyph),
        new FrameworkPropertyMetadata(LogoKind.WindowsMark, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GeometryKeyProperty = DependencyProperty.Register(
        nameof(GeometryKey), typeof(string), typeof(LogoGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaskProperty = DependencyProperty.Register(
        nameof(Mask), typeof(ImageSource), typeof(LogoGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // Cached snapped geometry (only rebuilt when the mark or its pixel size changes; repaints reuse it).
    private Geometry? _geoSource;
    private int _geoPx;
    private Geometry? _geoSnapped;

    // Cached opacity brush built from the Gray8 mask (only rebuilt when the mask changes).
    private ImageSource? _maskSource;
    private ImageBrush? _opacityBrush;

    public LogoGlyph() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

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

    /// <summary>Which mark to draw.</summary>
    public LogoKind Kind
    {
        get => (LogoKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Resource key of the vector mark when <see cref="Kind"/> is <see cref="LogoKind.Geometry"/>.</summary>
    public string? GeometryKey
    {
        get => (string?)GetValue(GeometryKeyProperty);
        set => SetValue(GeometryKeyProperty, value);
    }

    /// <summary>The Gray8 silhouette mask when <see cref="Kind"/> is <see cref="LogoKind.Image"/>.</summary>
    public ImageSource? Mask
    {
        get => (ImageSource?)GetValue(MaskProperty);
        set => SetValue(MaskProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        switch (Kind)
        {
            case LogoKind.Geometry:
                DrawGeometryMark(drawingContext, scale);
                break;
            case LogoKind.Image:
                DrawImageMark(drawingContext, scale);
                break;
            default:
                DrawWindowsMark(drawingContext, scale);
                break;
        }
    }

    /// <summary>The four-pane Windows mark, in whole device pixels (kept verbatim from the old WindowsLogo control).</summary>
    private void DrawWindowsMark(DrawingContext drawingContext, double scale)
    {
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

    private void DrawGeometryMark(DrawingContext drawingContext, double scale)
    {
        if (GeometryKey is not { } key || TryFindResource(key) is not Geometry source)
        {
            // A missing mark is a resource-merge bug; show the Windows panes rather than an empty logo button.
            DrawWindowsMark(drawingContext, scale);
            return;
        }

        var px = (int)Math.Round(BarMetrics.SnapToDevice(Size, scale) * scale);
        if (px <= 0)
        {
            return;
        }

        // The mark only changes with the setting; keep the last snapped shape for repaints (hover, colour).
        if (!ReferenceEquals(source, _geoSource) || px != _geoPx)
        {
            _geoSource = source;
            _geoPx = px;
            _geoSnapped = GeometrySnap.Snap(source, px, scale);
        }

        drawingContext.DrawGeometry(Fill, null, _geoSnapped);
    }

    private void DrawImageMark(DrawingContext drawingContext, double scale)
    {
        if (Mask is not { } mask)
        {
            // No mask decoded (yet): fall back to the Windows panes so the logo is never a blank box.
            DrawWindowsMark(drawingContext, scale);
            return;
        }

        if (!ReferenceEquals(mask, _maskSource))
        {
            _maskSource = mask;
            _opacityBrush = MakeOpacityBrush(mask);
        }

        if (_opacityBrush is null)
        {
            DrawWindowsMark(drawingContext, scale);
            return;
        }

        // Size is already a whole number of device pixels (ApplySizes snaps it), so the square lands on the grid.
        var side = BarMetrics.SnapToDevice(Size, scale);
        drawingContext.PushOpacityMask(_opacityBrush);
        drawingContext.DrawRectangle(Fill, null, new Rect(0, 0, side, side));
        drawingContext.Pop();
    }

    /// <summary>
    /// Turns the Gray8 mask (its value is the opacity we want) into an alpha-bearing brush, because a WPF opacity mask
    /// reads the alpha channel and Gray8 has none. RGB is set to white; only the alpha (the mask value) is used. Built
    /// once per mask and frozen, so there is no per-frame work.
    /// </summary>
    private static ImageBrush? MakeOpacityBrush(ImageSource mask)
    {
        if (mask is not BitmapSource source)
        {
            return null;
        }

        var gray = source.Format == PixelFormats.Gray8 ? source : new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        var width = gray.PixelWidth;
        var height = gray.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var maskBytes = new byte[width * height];
        gray.CopyPixels(maskBytes, width, 0);

        var bgra = new byte[width * height * 4];
        for (var i = 0; i < maskBytes.Length; i++)
        {
            bgra[i * 4] = 255;
            bgra[(i * 4) + 1] = 255;
            bgra[(i * 4) + 2] = 255;
            bgra[(i * 4) + 3] = maskBytes[i];
        }

        var alpha = BitmapSource.Create(width, height, gray.DpiX, gray.DpiY, PixelFormats.Bgra32, null, bgra, width * 4);
        alpha.Freeze();
        var brush = new ImageBrush(alpha)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        brush.Freeze();
        return brush;
    }
}
