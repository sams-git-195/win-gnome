// Experiment for the user session (spec 0010, lost strips after hiding the taskbar). NOT for use while any WinGnome
// runs. Usage: TbExp.exe <variant> <logfile>
//   immediate : ABM_SETSTATE auto-hide, then SW_HIDE every taskbar window at once (what WinGnome does today)
//   settle    : ABM_SETSTATE auto-hide, wait until the primary's work area no longer excludes the taskbar (max 5 s),
//               then SW_HIDE
//   nohide    : ABM_SETSTATE auto-hide only, taskbar windows stay visible
//   spi       : like immediate, and if the strip is still missing 2 s after docking, SPI_SETWORKAREA the primary's
//               work area to (current work area minus our strip) without SPIF_UPDATEINIFILE
// Then docks a 40 px top AppBar on the primary, logs every monitor's work area every 250 ms for 60 s, and restores:
// ABM_REMOVE, shows the taskbar windows, puts the auto-hide state back.
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class P
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; public override string ToString() => $"{L},{T},{R},{B}"; }
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public nint hWnd; public uint cb; public uint edge; public RECT rc; public nint lParam; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    delegate bool MonEnum(nint h, nint dc, ref RECT r, nint d);
    [DllImport("shell32.dll")] static extern nuint SHAppBarMessage(uint m, ref APPBARDATA d);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc, nint clip, MonEnum cb, nint d);
    [DllImport("user32.dll")] static extern bool GetMonitorInfoW(nint h, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool SystemParametersInfoW(uint a, uint b, ref RECT r, uint c);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowExW(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string s);
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(long pt, uint flags);

    static StreamWriter? s_log;
    static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();

    static void W(string s)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} +{s_clock.ElapsedMilliseconds,6} ms {s}";
        Console.WriteLine(line);
        s_log?.WriteLine(line);
        s_log?.Flush();
    }

    static string WorkAreas()
    {
        var sb = new StringBuilder();
        EnumDisplayMonitors(0, 0, (nint h, nint dc, ref RECT r, nint d) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(h, ref mi);
            sb.Append($"[mon {mi.rcMonitor} work {mi.rcWork}{((mi.dwFlags & 1) != 0 ? " primary" : "")}] ");
            return true;
        }, 0);
        return sb.ToString();
    }

    static RECT PrimaryWork(out RECT monitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(0, 1), ref mi);
        monitor = mi.rcMonitor;
        return mi.rcWork;
    }

    static List<nint> Taskbars()
    {
        var list = new List<nint>();
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint h = 0;
            while ((h = FindWindowExW(0, h, cls, null)) != 0) list.Add(h);
        }

        return list;
    }

    static nint Tray() => FindWindowExW(0, 0, "Shell_TrayWnd", null);

    static int GetState()
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = Tray() };
        return (int)SHAppBarMessage(0x04, ref d);
    }

    static void SetState(int state)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = Tray(), lParam = state };
        SHAppBarMessage(0x0A, ref d);
    }

    [STAThread]
    static int Main(string[] args)
    {
        var variant = args.Length > 0 ? args[0] : "immediate";
        if (args.Length > 1) s_log = new StreamWriter(args[1], append: true);
        if (System.Diagnostics.Process.GetProcessesByName("WinGnome").Length > 0)
        {
            W("WinGnome is running; quit it first. Nothing changed.");
            return 2;
        }

        var originalState = GetState();
        var taskbars = Taskbars();
        W($"variant {variant}; taskbar state {originalState} (1 = auto-hide); {taskbars.Count} taskbar windows; {WorkAreas()}");

        var form = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, Text = "TbExp AppBar", BackColor = System.Drawing.Color.DarkOrange };
        var hwnd = form.Handle;
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, cb = RegisterWindowMessageW("TbExp") };
        var docked = false;
        RECT strip = default;

        void Restore()
        {
            if (docked)
            {
                var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
                SHAppBarMessage(0x01, ref d);
                docked = false;
            }

            foreach (var t in Taskbars()) ShowWindow(t, 8 /* SW_SHOWNA */);
            SetState(originalState == 0 ? 2 /* ABS_ALWAYSONTOP */ : originalState);
            W($"restored: state {GetState()}; {WorkAreas()}");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
        try
        {
            SetState(1 /* ABS_AUTOHIDE */);
            W("auto-hide set");
            if (variant == "settle")
            {
                var until = s_clock.ElapsedMilliseconds + 5000;
                while (s_clock.ElapsedMilliseconds < until)
                {
                    var w = PrimaryWork(out var m);
                    if (w.B == m.B) break;
                    Application.DoEvents();
                    Thread.Sleep(50);
                }

                W($"settled: {WorkAreas()}");
            }

            if (variant != "nohide")
            {
                foreach (var t in Taskbars()) if (IsWindowVisible(t)) ShowWindow(t, 0 /* SW_HIDE */);
                W($"taskbar windows hidden; {WorkAreas()}");
            }

            SHAppBarMessage(0x00, ref data);
            docked = true;
            PrimaryWork(out var mon);
            data.edge = 1;
            data.rc = new RECT { L = mon.L, T = mon.T, R = mon.R, B = mon.T + 40 };
            SHAppBarMessage(0x02, ref data);
            data.rc.B = data.rc.T + 40;
            SHAppBarMessage(0x03, ref data);
            strip = data.rc;
            SetWindowPos(hwnd, -1, strip.L, strip.T, strip.R - strip.L, strip.B - strip.T, 0x10);
            form.Show();
            var wpc = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
            SHAppBarMessage(0x09, ref wpc);
            W($"docked {strip}; {WorkAreas()}");

            var dockedAt = s_clock.ElapsedMilliseconds;
            var spiDone = false;
            string last = "";
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (_, _) =>
            {
                var now = WorkAreas();
                if (now != last)
                {
                    W($"changed: {now}");
                    last = now;
                }

                var work = PrimaryWork(out _);
                if (variant == "spi" && !spiDone && s_clock.ElapsedMilliseconds - dockedAt > 2000 && work.T < strip.B)
                {
                    spiDone = true;
                    var fixedArea = work with { T = strip.B };
                    W($"SPI_SETWORKAREA {fixedArea} -> {SystemParametersInfoW(0x2F, 0, ref fixedArea, 0x2)}; {WorkAreas()}");
                }

                if (s_clock.ElapsedMilliseconds - dockedAt > 60_000)
                {
                    timer.Stop();
                    form.Close();
                }
            };
            timer.Start();
            Application.Run(form);
        }
        finally
        {
            Restore();
        }

        return 0;
    }
}
