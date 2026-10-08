using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinGnome.Core.Input;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Overview;

/// <summary>
/// One system-wide hotkey (RegisterHotKey) delivered to a hidden message-only window. Unlike a keyboard
/// hook this needs no special care: Windows does the matching, and keystrokes are never delayed.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x4F56; // "OV"

    private readonly HwndSource _window;
    private Hotkey? _registered;

    public GlobalHotkey()
    {
        // A message-only window (parent HWND_MESSAGE) is invisible, never enumerated and never activated.
        _window = new HwndSource(new HwndSourceParameters("WinGnome overview hotkey")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Registers <paramref name="text"/> (e.g. "Alt+F1"), replacing any previous hotkey. Logs and leaves no
    /// hotkey registered when the text is invalid or another program already owns the combination.
    /// </summary>
    public void Register(string text)
    {
        if (!Hotkey.TryParse(text, out var hotkey))
        {
            Unregister();
            Log.Warn($"Overview hotkey '{text}' is not a valid hotkey");
            return;
        }

        if (_registered == hotkey)
        {
            return;
        }

        Unregister();

        // MOD_NOREPEAT: holding the keys down must not toggle the overview open and closed repeatedly.
        var modifiers = (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, modifiers, (uint)hotkey.VirtualKey))
        {
            Log.Warn($"Could not register the overview hotkey {hotkey} (error {Marshal.GetLastPInvokeError()}); another program may already use it");
            return;
        }

        _registered = hotkey;
        Log.Info($"Overview hotkey {hotkey} registered");
    }

    /// <summary>Releases the hotkey, if one is registered.</summary>
    public void Unregister()
    {
        if (_registered is null)
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId))
        {
            Log.Warn($"UnregisterHotKey failed (error {Marshal.GetLastPInvokeError()})");
        }

        _registered = null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
