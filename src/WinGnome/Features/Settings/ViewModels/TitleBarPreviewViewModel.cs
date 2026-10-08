using System.Windows.Media;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One circle of the title bar preview, positioned in a canvas.</summary>
internal sealed record PreviewButton(
    double Left, double Top, double Diameter, Brush Fill, Brush Outline, Brush GlyphBrush, string Glyph, double GlyphSize, bool GlyphAlwaysVisible);

/// <summary>A fake title bar with the circles as they would be drawn for a focused or an unfocused window.</summary>
internal sealed record PreviewTitleBar(string Label, IReadOnlyList<PreviewButton> Buttons);

/// <summary>
/// Builds the title bar previews with the same layout code the real overlay uses, so side, order, size and
/// spacing in the preview are exactly what windows get.
/// </summary>
internal sealed class TitleBarPreviewViewModel : ObservableObject
{
    /// <summary>Width of the fake title bar in device-independent pixels.</summary>
    public const double BarWidth = 400;

    /// <summary>Height of the fake title bar.</summary>
    public const double BarHeight = 36;

    private const int NativeButtonsWidth = 138;

    public TitleBarPreviewViewModel(WindowButtonSettings settings)
    {
        Bars = [];
        Update(settings);
    }

    public IReadOnlyList<PreviewTitleBar> Bars { get; private set; }

    /// <summary>Recomputes both previews from the settings.</summary>
    public void Update(WindowButtonSettings settings)
    {
        var colors = TrafficLightPalette.For(settings);
        var window = PixelRect.FromSize(0, 0, (int)BarWidth, (int)BarHeight);
        var native = new PixelRect(window.Right - NativeButtonsWidth, 0, window.Right, window.Bottom);
        var layout = CaptionButtonLayout.Compute(native, window, 1.0, settings);
        if (layout is null)
        {
            Bars = [];
        }
        else
        {
            Bars =
            [
                new PreviewTitleBar("Focused window", Build(layout, colors, settings, active: true)),
                new PreviewTitleBar("Unfocused window", Build(layout, colors, settings, active: false)),
            ];
        }

        OnPropertyChanged(nameof(Bars));
    }

    private static List<PreviewButton> Build(CaptionOverlayLayout layout, TrafficLightColors colors, WindowButtonSettings settings, bool active)
    {
        var dimmed = !active && settings.DimInactiveWindows;
        var outline = ColorConversion.ToBrush(colors.Border);
        var glyphBrush = ColorConversion.ToBrush(colors.Glyph);
        var alwaysVisible = active && (colors.AlwaysShowGlyphs || !settings.ShowSymbolsOnHover);
        var diameter = layout.Diameter;

        return layout.Buttons.Select(slot =>
        {
            var fill = dimmed ? colors.Inactive : slot.Kind switch
            {
                CaptionButtonKind.Close => colors.Close,
                CaptionButtonKind.Minimize => colors.Minimize,
                _ => colors.Maximize,
            };
            var glyph = slot.Kind switch
            {
                CaptionButtonKind.Close => "×",
                CaptionButtonKind.Minimize => "−",
                _ => "+",
            };
            return new PreviewButton(
                layout.Bounds.Left + slot.CenterX - (diameter / 2),
                slot.CenterY - (diameter / 2),
                diameter,
                ColorConversion.ToBrush(fill),
                outline,
                glyphBrush,
                glyph,
                diameter * 0.95,
                alwaysVisible);
        }).ToList();
    }
}
