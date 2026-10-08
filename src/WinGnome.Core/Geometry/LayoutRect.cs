namespace WinGnome.Core.Geometry;

/// <summary>A rectangle in device-independent pixels (or any other floating point layout unit).</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>X + Width.</summary>
    public double Right => X + Width;

    /// <summary>Y + Height.</summary>
    public double Bottom => Y + Height;

    /// <summary>Horizontal centre.</summary>
    public double CenterX => X + (Width / 2);

    /// <summary>Vertical centre.</summary>
    public double CenterY => Y + (Height / 2);
}
