using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Single source of truth for the desktop's top-level windows. Installs one set of WinEvent hooks
/// and fans the events out to features, so each feature does not need its own hooks.
/// </summary>
/// <remarks>
/// <see cref="Windows"/> holds task-switcher (Alt+Tab-style) windows in z-order, which approximates
/// most-recently-used order. It is refreshed on a short debounce after any relevant event.
/// All events are raised on the UI thread.
/// </remarks>
internal sealed partial class WindowTracker : IDisposable
{
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(60);

    private readonly DispatcherTimer _refreshTimer;
    private readonly ConcurrentDictionary<uint, ProcessFacts> _processCache = new();
    private readonly uint _ownProcessId = NativeMethods.GetCurrentProcessId();
    private WinEventHook? _hook;
    private nint _foreground;

    public WindowTracker(Dispatcher dispatcher)
    {
        _refreshTimer = new DispatcherTimer(RefreshDebounce, DispatcherPriority.Background, (_, _) => RefreshNow(), dispatcher)
        {
            IsEnabled = false,
        };
    }

    /// <summary>Task-switcher windows in z-order (topmost / most recent first).</summary>
    public IReadOnlyList<WindowInfo> Windows { get; private set; } = [];

    /// <summary>The current foreground window (may belong to WinGnome itself or be a non-task window).</summary>
    public nint Foreground => _foreground;

    /// <summary>Raised (debounced) whenever the set, order, titles or states of task windows change.</summary>
    public event EventHandler? WindowsChanged;

    /// <summary>Raised immediately when the foreground window changes.</summary>
    public event EventHandler<nint>? ForegroundChanged;

    /// <summary>
    /// Raised immediately for every top-level window event (see <see cref="WinEventHook"/> constants),
    /// including location changes. Handlers must be cheap.
    /// </summary>
    public event Action<uint, nint>? RawWindowEvent;

    public void Start()
    {
        if (_hook is not null)
        {
            return;
        }

        _foreground = NativeMethods.GetForegroundWindow();
        _hook = new WinEventHook(OnWinEvent, skipOwnProcess: true,
            (WinEventHook.EVENT_SYSTEM_FOREGROUND, WinEventHook.EVENT_SYSTEM_FOREGROUND),
            (WinEventHook.EVENT_SYSTEM_MOVESIZESTART, WinEventHook.EVENT_SYSTEM_MOVESIZEEND),
            (WinEventHook.EVENT_SYSTEM_MINIMIZESTART, WinEventHook.EVENT_SYSTEM_MINIMIZEEND),
            (WinEventHook.EVENT_OBJECT_CREATE, WinEventHook.EVENT_OBJECT_REORDER),
            (WinEventHook.EVENT_OBJECT_LOCATIONCHANGE, WinEventHook.EVENT_OBJECT_NAMECHANGE),
            (WinEventHook.EVENT_OBJECT_CLOAKED, WinEventHook.EVENT_OBJECT_UNCLOAKED));
        RefreshNow();
    }

    /// <summary>Re-enumerates windows immediately.</summary>
    public void RefreshNow()
    {
        _refreshTimer.Stop();
        var list = new List<WindowInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                var info = Inspect(hwnd);
                if (info is not null && WindowFilter.IsTaskSwitcherWindow(info))
                {
                    list.Add(info);
                }
            }

            return true;
        }, 0);

        Windows = list;
        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds a <see cref="WindowInfo"/> snapshot for any top-level window, or null if it is gone or ours.</summary>
    public WindowInfo? Inspect(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        var pid = NativeMethods.GetProcessId(hwnd);
        if (pid == 0 || pid == _ownProcessId)
        {
            return null;
        }

        var className = NativeMethods.GetClassName(hwnd);
        var style = NativeMethods.GetStyle(hwnd);
        var exStyle = NativeMethods.GetExStyle(hwnd);

        // UWP apps are hosted by ApplicationFrameHost; the real app process owns the CoreWindow child.
        var appPid = className == "ApplicationFrameWindow" ? FindCoreWindowProcess(hwnd) ?? pid : pid;
        var facts = GetProcessFacts(appPid);

        var aumid = WindowProperties.GetAppUserModelId(hwnd) ?? facts.PackagedAppUserModelId;

        return new WindowInfo(
            Handle: hwnd,
            Title: NativeMethods.GetWindowTitle(hwnd),
            ClassName: className,
            ProcessId: (int)appPid,
            ProcessPath: facts.Path,
            AppUserModelId: aumid,
            IsVisible: NativeMethods.IsWindowVisible(hwnd),
            IsCloaked: NativeMethods.IsCloaked(hwnd),
            IsMinimized: NativeMethods.IsIconic(hwnd),
            IsMaximized: NativeMethods.IsZoomed(hwnd),
            IsToolWindow: (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0,
            IsNoActivate: (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0,
            HasOwner: NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != 0,
            HasCaption: (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION,
            HasSystemMenu: (style & NativeMethods.WS_SYSMENU) != 0,
            IsElevated: facts.IsElevated,
            Bounds: NativeMethods.GetVisibleBounds(hwnd));
    }

    /// <summary>Friendly application name for a window (file description, else exe name).</summary>
    public string GetAppName(WindowInfo window)
    {
        var facts = GetProcessFacts((uint)window.ProcessId);
        return facts.DisplayName;
    }

    private void OnWinEvent(uint eventType, nint hwnd)
    {
        if (eventType == WinEventHook.EVENT_SYSTEM_FOREGROUND)
        {
            _foreground = hwnd;
            ForegroundChanged?.Invoke(this, hwnd);
        }

        RawWindowEvent?.Invoke(eventType, hwnd);

        if (eventType == WinEventHook.EVENT_OBJECT_DESTROY)
        {
            // Process ids are recycled; drop facts lazily when windows go away.
            if (_processCache.Count > 512)
            {
                _processCache.Clear();
            }
        }

        // Location changes are far too frequent to re-enumerate on; everything else may change the list.
        if (eventType is not WinEventHook.EVENT_OBJECT_LOCATIONCHANGE
            and not WinEventHook.EVENT_SYSTEM_MOVESIZESTART
            and not WinEventHook.EVENT_SYSTEM_MOVESIZEEND
            and not WinEventHook.EVENT_OBJECT_CREATE)
        {
            ScheduleRefresh();
        }
    }

    private void ScheduleRefresh()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private static uint? FindCoreWindowProcess(nint frame)
    {
        var framePid = NativeMethods.GetProcessId(frame);
        uint? result = null;
        NativeMethods.EnumChildWindows(frame, (child, _) =>
        {
            var childPid = NativeMethods.GetProcessId(child);
            if (childPid != framePid && NativeMethods.GetClassName(child) == "Windows.UI.Core.CoreWindow")
            {
                result = childPid;
                return false;
            }

            return true;
        }, 0);
        return result;
    }

    private ProcessFacts GetProcessFacts(uint pid)
    {
        if (_processCache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var path = NativeMethods.GetProcessPath(pid);
        var facts = new ProcessFacts(
            path,
            DescribeProcess(path),
            NativeMethods.IsProcessElevated(pid),
            GetPackagedAppUserModelId(pid));
        _processCache[pid] = facts;
        return facts;
    }

    private static string DescribeProcess(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return AppIdentity.DisplayNameFromPath(path);
        }

        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fall through to the file name.
        }

        return AppIdentity.DisplayNameFromPath(path);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetApplicationUserModelId(nint process, ref int length, [Out] char[]? buffer);

    /// <summary>The package AUMID of a packaged (MSIX/UWP) process, or null for classic desktop apps.</summary>
    private static string? GetPackagedAppUserModelId(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var length = 0;
            const int ErrorInsufficientBuffer = 122;
            if (GetApplicationUserModelId(process, ref length, null) != ErrorInsufficientBuffer || length <= 0)
            {
                return null;
            }

            var buffer = new char[length];
            return GetApplicationUserModelId(process, ref length, buffer) == 0
                ? new string(buffer, 0, Math.Max(0, length - 1))
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _hook?.Dispose();
        _hook = null;
    }

    private sealed record ProcessFacts(string? Path, string DisplayName, bool IsElevated, string? PackagedAppUserModelId);
}
