using System.Windows.Interop;
using WinGnome.Interop;

namespace WinGnome.Infrastructure;

/// <summary>
/// Turns a polite close request for the process into a clean shutdown.
/// </summary>
/// <remarks>
/// <para>
/// <c>taskkill /im WinGnome.exe</c> (without /f), installers and update tools ask an app to quit by posting
/// WM_CLOSE to one of its visible top-level windows (taskkill picks the first it finds). WinGnome's shell surfaces
/// (top bar, dock, backdrops, caption overlays) refuse or ignore WM_CLOSE, so without this the request would be
/// lost, or close a single surface and leave the process running in a broken state.
/// </para>
/// <para>
/// Two layers handle it. A message filter on the UI thread catches a <em>posted</em> WM_CLOSE aimed at any of our
/// windows: WinGnome never posts WM_CLOSE itself (closing a window from code or the caption button sends it), so a
/// posted one always comes from outside. And this zero-size, off-screen, non-activating tool window guarantees there
/// is always a visible top-level window to receive the request, even with every surface disabled.
/// </para>
/// </remarks>
internal sealed class ControlWindow : IDisposable
{
    private const int WS_VISIBLE = 0x10000000;
    private const int OffScreen = -32000;

    private readonly HwndSource _source;
    private readonly Action _onCloseRequested;
    private bool _requested;

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
        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
    }

    private void OnThreadFilterMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        if (msg.message == NativeMethods.WM_CLOSE && !handled)
        {
            handled = true;
            RequestClose();
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLOSE)
        {
            // Never let this window be destroyed; shut the whole app down instead.
            handled = true;
            RequestClose();
        }

        return 0;
    }

    private void RequestClose()
    {
        if (_requested)
        {
            return;
        }

        _requested = true;
        Log.Info("Close requested from outside (WM_CLOSE); shutting down");
        _onCloseRequested();
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
