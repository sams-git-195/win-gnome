using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Window-region declarations used to keep the hidden Windows taskbar from drawing.</summary>
internal static partial class NativeMethods
{
    /// <summary>Creates a rectangular region; the caller owns it unless SetWindowRgn accepted it.</summary>
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    /// <summary>True when the window's thread has not handled messages for several seconds.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(nint hwnd);

    /// <summary>Copies the window's region into <paramref name="region"/> and returns its type (0 error or none, 1 empty, 2 simple, 3 complex).</summary>
    [LibraryImport("user32.dll")]
    public static partial int GetWindowRgn(nint hwnd, nint region);
}
