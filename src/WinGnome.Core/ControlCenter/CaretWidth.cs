namespace WinGnome.Core.ControlCenter;

/// <summary>The text cursor thickness range Windows Settings offers (SPI_SETCARETWIDTH, in pixels).</summary>
public static class CaretWidth
{
    public const int Min = 1;

    public const int Max = 20;

    public static int Clamp(int width) => Math.Clamp(width, Min, Max);
}
