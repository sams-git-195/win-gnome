using System.IO;
using System.Security;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Workspaces;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Tracks the number of virtual desktops and the current one from Explorer's registry state and switches
/// between them. Windows has no public API for either, so this reads the registry and sends Ctrl+Win+arrows.
/// </summary>
internal sealed class VirtualDesktopMonitor : IDisposable
{
    private const string DesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string DesktopIdsValue = "VirtualDesktopIDs";
    private const string CurrentDesktopValue = "CurrentVirtualDesktop";

    private readonly Dispatcher _dispatcher;
    private readonly string? _sessionKey;
    private readonly List<RegistryKeyWatcher> _watchers = [];
    private readonly DesktopSwitchPlanner _planner = new();
    private bool _disposed;

    public VirtualDesktopMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // Explorer keeps the live current desktop per logon session; the VirtualDesktops key holds the list
        // and, on some builds, a copy of the current id that is only written at sign-out.
        if (NativeMethods.ProcessIdToSessionId(NativeMethods.GetCurrentProcessId(), out var session))
        {
            _sessionKey = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{session}\VirtualDesktops";
        }
        else
        {
            Log.Warn("ProcessIdToSessionId failed; reading the current desktop from the shared key only");
        }

        foreach (var key in new[] { DesktopsKey, _sessionKey })
        {
            if (key is not null && RegistryKeyWatcher.TryCreate(key, OnRegistryChanged) is { } watcher)
            {
                _watchers.Add(watcher);
            }
        }

        State = Read();
    }

    /// <summary>Desktop count and current index.</summary>
    public VirtualDesktopState State { get; private set; }

    /// <summary>Raised on the UI thread when the count or current desktop changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Switches to the desktop at <paramref name="index"/> (clamped) by sending Ctrl+Win+Left/Right.</summary>
    public void SwitchTo(int index) =>
        ShellShortcuts.SwitchDesktop(_planner.PlanSwitchTo(State, index, DateTime.UtcNow));

    /// <summary>Moves <paramref name="delta"/> desktops left (negative) or right, stopping at the first and last.</summary>
    public void SwitchBy(int delta) =>
        ShellShortcuts.SwitchDesktop(_planner.PlanSwitchBy(State, delta, DateTime.UtcNow));

    /// <summary>Re-reads the registry and raises <see cref="Changed"/> when something moved.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var state = Read();
        _planner.Observe(state);
        if (state != State)
        {
            State = state;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRegistryChanged() => _dispatcher.BeginInvoke(Refresh);

    private VirtualDesktopState Read()
    {
        var ids = ReadBinary(DesktopsKey, DesktopIdsValue);
        var current = (_sessionKey is null ? null : ReadBinary(_sessionKey, CurrentDesktopValue))
            ?? ReadBinary(DesktopsKey, CurrentDesktopValue);
        return VirtualDesktopState.Parse(ids, current);
    }

    private static byte[]? ReadBinary(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as byte[];
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Cannot read HKCU\\{subKey}\\{name}", ex);
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
