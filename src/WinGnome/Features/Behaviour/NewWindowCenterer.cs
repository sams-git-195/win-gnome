using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Behaviour;

/// <summary>
/// GNOME's "Center new windows": the first time a normal application window appears, it is moved so its
/// visible frame sits in the middle of its monitor's work area.
/// </summary>
/// <remarks>
/// Only windows that appear after this object is created count as new: everything already on the desktop
/// (visible or hidden) is remembered as seen, and a window is never centred twice, so re-showing a window
/// that was hidden to the tray keeps the position the user gave it.
/// </remarks>
internal sealed class NewWindowCenterer : IDisposable
{
    /// <summary>
    /// How long to wait after a window is first shown. Many apps restore a saved position or resize
    /// themselves right after showing; moving them earlier would be undone or would fight the app.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(80);

    private readonly WindowTracker _tracker;
    private readonly HashSet<nint> _seen = [];
    private readonly Dictionary<nint, DateTime> _pending = [];
    private readonly DispatcherTimer _timer;

    public NewWindowCenterer(WindowTracker tracker, Dispatcher dispatcher)
    {
        _tracker = tracker;
        _timer = new DispatcherTimer(SettleDelay, DispatcherPriority.Normal, (_, _) => ProcessDue(), dispatcher)
        {
            IsEnabled = false,
        };

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            _seen.Add(hwnd);
            return true;
        }, 0);

        _tracker.RawWindowEvent += OnRawWindowEvent;
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        switch (eventType)
        {
            // Child controls raise EVENT_OBJECT_SHOW too; only top-level windows are candidates.
            case WinEventHook.EVENT_OBJECT_SHOW when NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) == hwnd && _seen.Add(hwnd):
                _pending[hwnd] = DateTime.UtcNow + SettleDelay;
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                }

                break;

            case WinEventHook.EVENT_OBJECT_DESTROY:
                // Handles are recycled, so a destroyed handle must count as unseen again.
                _seen.Remove(hwnd);
                _pending.Remove(hwnd);
                break;
        }
    }

    private void ProcessDue()
    {
        var now = DateTime.UtcNow;
        foreach (var (hwnd, due) in _pending.ToList())
        {
            if (due <= now)
            {
                _pending.Remove(hwnd);
                TryCenter(hwnd);
            }
        }

        if (_pending.Count == 0)
        {
            _timer.Stop();
        }
    }

    private void TryCenter(nint hwnd)
    {
        // Inspect returns null for windows that are gone or belong to WinGnome itself. Owned windows (dialogs,
        // palettes) position themselves relative to their owner; the task-switcher rule alone would still let
        // through owned windows that ask for a taskbar button (WS_EX_APPWINDOW).
        var info = _tracker.Inspect(hwnd);
        if (info is null
            || !WindowFilter.IsTaskSwitcherWindow(info)
            || info.HasOwner
            || info.IsMinimized
            || info.IsMaximized
            || !info.HasCaption
            || info.IsElevated)
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var (_, workArea) = NativeMethods.GetMonitorRects(monitor);
        var windowRect = NativeMethods.GetWindowBounds(hwnd);
        if (workArea.IsEmpty || windowRect.IsEmpty)
        {
            return;
        }

        var (x, y) = WindowGeometry.CenteredOrigin(windowRect, info.Bounds, workArea);
        if (x == windowRect.Left && y == windowRect.Top)
        {
            return;
        }

        // SWP_ASYNCWINDOWPOS: the window belongs to another thread; a synchronous SetWindowPos would
        // block our UI thread for as long as that app takes to respond (forever, if it is hung).
        const uint Flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_ASYNCWINDOWPOS;
        if (!NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, Flags))
        {
            Log.Warn($"Could not centre window 0x{hwnd:X} (error {Marshal.GetLastPInvokeError()})");
        }
    }

    public void Dispose()
    {
        _tracker.RawWindowEvent -= OnRawWindowEvent;
        _timer.Stop();
        _pending.Clear();
        _seen.Clear();
    }
}
