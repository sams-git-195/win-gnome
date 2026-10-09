namespace WinGnome.Core.ControlCenter;

/// <summary>The value ranges behind Windows' mouse and keyboard settings (SystemParametersInfo) and how to read them.</summary>
public static class InputTuning
{
    /// <summary>SPI_SETMOUSESPEED range; 10 is the Windows default.</summary>
    public const int MinMouseSpeed = 1;

    public const int MaxMouseSpeed = 20;

    /// <summary>SPI_SETKEYBOARDDELAY steps (0..3) and SPI_SETKEYBOARDSPEED steps (0..31).</summary>
    public const int MaxRepeatDelayIndex = 3;

    public const int MaxRepeatSpeed = 31;

    public const int MinScrollLines = 1;

    public const int MaxScrollLines = 100;

    private const double MinRepeatsPerSecond = 2.5;
    private const double MaxRepeatsPerSecond = 30;

    /// <summary>Repeat delay in milliseconds for a SPI_GETKEYBOARDDELAY step: 250 ms per step, starting at 250 ms.</summary>
    public static int RepeatDelayMs(int index) => (Math.Clamp(index, 0, MaxRepeatDelayIndex) + 1) * 250;

    /// <summary>Approximate repeats per second for a SPI_GETKEYBOARDSPEED step (documented as 2.5 to 30, linear).</summary>
    public static double RepeatsPerSecond(int speed) =>
        MinRepeatsPerSecond + (Math.Clamp(speed, 0, MaxRepeatSpeed) * (MaxRepeatsPerSecond - MinRepeatsPerSecond) / MaxRepeatSpeed);

    /// <summary>
    /// True when "Enhance pointer precision" is on: SPI_GETMOUSE returns {threshold1, threshold2, acceleration} and
    /// acceleration is non-zero.
    /// </summary>
    public static bool IsAccelerationOn(int[] mouseParameters) => mouseParameters is [_, _, not 0];

    /// <summary>The SPI_SETMOUSE values Windows' own checkbox writes: {6, 10, 1} on, {0, 0, 0} off.</summary>
    public static int[] AccelerationParameters(bool on) => on ? [6, 10, 1] : [0, 0, 0];

    public static int ClampMouseSpeed(int speed) => Math.Clamp(speed, MinMouseSpeed, MaxMouseSpeed);

    public static int ClampScrollLines(int lines) => Math.Clamp(lines, MinScrollLines, MaxScrollLines);
}
