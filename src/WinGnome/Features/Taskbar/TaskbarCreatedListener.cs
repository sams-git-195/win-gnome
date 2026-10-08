using System.Windows.Interop;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Tray;

namespace WinGnome.Features.Taskbar;

/// <summary>
/// Hidden top-level window that receives Explorer's "TaskbarCreated" broadcast, which Explorer sends after it
/// (re)starts and has created a fresh taskbar.
/// </summary>
/// <remarks>
/// The broadcast goes to top-level windows only, so a message-only window (HWND_MESSAGE parent) would never see
/// it. This window is an unowned, never-shown WS_POPUP tool window: invisible, absent from Alt+Tab, yet a
/// legitimate broadcast recipient.
/// </remarks>
internal sealed class TaskbarCreatedListener : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private readonly Action _onTaskbarCreated;

    public TaskbarCreatedListener(Action onTaskbarCreated)
    {
        _onTaskbarCreated = onTaskbarCreated ?? throw new ArgumentNullException(nameof(onTaskbarCreated));
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        var parameters = new HwndSourceParameters("WinGnome.TaskbarCreatedListener")
        {
            WindowStyle = unchecked((int)NativeMethods.WS_POPUP),
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        // Explorer runs at medium integrity. If WinGnome was started elevated, UIPI would silently drop the
        // broadcast unless this message is explicitly allowed through.
        if (_taskbarCreatedMessage == 0
            || !NativeMethods.ChangeWindowMessageFilterEx(_source.Handle, _taskbarCreatedMessage, NativeMethods.MSGFLT_ALLOW, 0))
        {
            Log.Warn("Could not allow the TaskbarCreated message; an Explorer restart may re-show the taskbar");
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // WinGnome's tray host sends TaskbarCreated too (to collect tray icons); Explorer did not restart then.
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage && !TrayHost.IsOwnBroadcast(wParam))
        {
            _onTaskbarCreated();
        }

        return 0;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
