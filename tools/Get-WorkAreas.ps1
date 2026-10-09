# Prints every monitor's bounds and work area in physical pixels (DPI-aware) and whether Explorer's taskbar is visible.
# Used in QA to check AppBar strips are reserved and restored. Read-only.
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class WA {
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MI { public int cb; public R mon; public R work; public int flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dev; }
 public delegate bool P(IntPtr h, IntPtr dc, ref R r, IntPtr d);
 [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, P cb, IntPtr d);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr h, ref MI mi);
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public static List<string> All() { SetProcessDpiAwarenessContext(new IntPtr(-4)); var l=new List<string>(); EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref R r, IntPtr d) => { var mi=new MI(); mi.cb=Marshal.SizeOf(mi); GetMonitorInfo(h, ref mi); l.Add(mi.dev+" mon="+mi.mon.L+","+mi.mon.T+","+mi.mon.Ri+","+mi.mon.B+" work="+mi.work.L+","+mi.work.T+","+mi.work.Ri+","+mi.work.B); return true; }, IntPtr.Zero); l.Add("Shell_TrayWnd visible=" + IsWindowVisible(FindWindow("Shell_TrayWnd", null))); return l; } }
'@
[WA]::All()
