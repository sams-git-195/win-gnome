using System.Runtime.InteropServices;

namespace WinGnome.Interop;

internal static partial class NativeMethods
{
    // ---- user32: monitors (per-monitor bars and docks, spec 0010) ----------------------------
    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(in RECT rect, uint flags);
}
