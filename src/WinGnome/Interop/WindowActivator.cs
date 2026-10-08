using System.Runtime.InteropServices;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>Brings other applications' windows to the foreground, working around focus-stealing rules.</summary>
internal static partial class WindowActivator
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly long _padding; // INPUT is a union; keep it as large as MOUSEINPUT.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Marker placed in dwExtraInfo of keystrokes WinGnome injects, so its own hooks can ignore them.</summary>
    public const nint InjectedMarker = 0x574E47; // "WNG"

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);

    /// <summary>Restores (if minimised) and focuses <paramref name="hwnd"/>.</summary>
    public static void Activate(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        if (NativeMethods.SetForegroundWindow(hwnd) && NativeMethods.GetForegroundWindow() == hwnd)
        {
            return;
        }

        // Windows only lets the process that received the last input event change the foreground.
        // A synthetic Alt tap satisfies that rule. It is not entirely free of side effects: the window that was
        // in front may see a lone Alt press and highlight its menu bar.
        TapKey(NativeMethods.VK_MENU);
        if (!NativeMethods.SetForegroundWindow(hwnd))
        {
            Log.Warn($"SetForegroundWindow failed for 0x{hwnd:X}");
        }

        NativeMethods.BringWindowToTop(hwnd);
    }

    public static void Minimize(nint hwnd)
    {
        if (hwnd != 0)
        {
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, 0);
        }
    }

    public static void ToggleMaximize(nint hwnd)
    {
        if (hwnd != 0)
        {
            var command = NativeMethods.IsZoomed(hwnd) ? NativeMethods.SC_RESTORE : NativeMethods.SC_MAXIMIZE;
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, command, 0);
        }
    }

    /// <summary>Politely asks the window to close (same as clicking its close button).</summary>
    public static void Close(nint hwnd)
    {
        if (hwnd != 0)
        {
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_CLOSE, 0);
        }
    }

    /// <summary>Sends a key chord such as Ctrl+Win+Right. Keys are pressed in order and released in reverse.</summary>
    public static void SendChord(params ushort[] virtualKeys)
    {
        var inputs = new INPUT[virtualKeys.Length * 2];
        for (var i = 0; i < virtualKeys.Length; i++)
        {
            inputs[i] = Key(virtualKeys[i], up: false);
            inputs[inputs.Length - 1 - i] = Key(virtualKeys[i], up: true);
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Presses and releases a single key.</summary>
    public static void TapKey(ushort virtualKey) => SendChord(virtualKey);

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT
        {
            wVk = vk,
            dwFlags = up ? KEYEVENTF_KEYUP : 0,
            dwExtraInfo = InjectedMarker,
        },
    };
}
