using System.Windows.Interop;
using WinGnome.Interop;

namespace WinGnome.Infrastructure;

/// <summary>
/// Hidden top-level window that turns a polite close request for the process into a clean shutdown.
/// </summary>
/// <remarks>
/// <c>taskkill /im WinGnome.exe</c> (without /f), installers and update tools ask an app to quit by posting
/// WM_CLOSE to its top-level windows. WinGnome's visible surfaces refuse or ignore that, so without this window
/// the request would only close individual surfaces and leave the process running in a broken state. Those tools
/// only signal top-level windows that are *visible* (message-only and hidden windows are skipped), so this is a
/// visible but zero-size, off-screen, non-activating tool window: nothing to see, not in Alt+Tab or the taskbar.
/// </remarks>
internal sealed class ControlWindow : IDisposable
{
    private const int WS_VISIBLE = 0x10000000;
    private const int OffScreen = -32000;

    private readonly HwndSource _source;
    private readonly Action _onCloseRequested;

    public ControlWindow(Action onCloseRequested)
    {
        _onCloseRequested = onCloseRequested;
        var parameters = new HwndSourceParameters("WinGnome")
        {
            PositionX = OffScreen,
            PositionY = OffScreen,
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)NativeMethods.WS_POPUP) | WS_VISIBLE,
            ExtendedWindowStyle = (int)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLOSE)
        {
            // Never let the window be destroyed here; shut the whole app down instead.
            handled = true;
            Log.Info("Close requested from outside (WM_CLOSE); shutting down");
            _onCloseRequested();
        }

        return 0;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
