namespace WinGnome.Core.Theming;

/// <summary>The resolved colours used to draw one set of round caption buttons.</summary>
/// <param name="Close">Fill of the close button.</param>
/// <param name="Minimize">Fill of the minimise button.</param>
/// <param name="Maximize">Fill of the maximise button.</param>
/// <param name="Inactive">Fill of every button while the window is not focused.</param>
/// <param name="Glyph">Colour of the ×, − and + symbols.</param>
/// <param name="Border">Thin outline drawn around each circle.</param>
/// <param name="AlwaysShowGlyphs">True when the symbols are visible without hovering (GNOME style).</param>
public sealed record TrafficLightColors(
    HexColor Close,
    HexColor Minimize,
    HexColor Maximize,
    HexColor Inactive,
    HexColor Glyph,
    HexColor Border,
    bool AlwaysShowGlyphs);
