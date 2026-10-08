using System.IO;
using System.Security;
using Microsoft.Win32;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Watches the values of one HKCU key with <c>RegNotifyChangeKeyValue</c> and calls back on a thread-pool
/// thread whenever a value is written. Event-driven, so it costs nothing while the key is quiet (unlike polling).
/// </summary>
internal sealed class RegistryKeyWatcher : IDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly RegistryKey _key;
    private readonly Action _changed;
    private readonly AutoResetEvent _signal = new(false);
    private RegisteredWaitHandle? _wait;
    private int _disposed;

    private RegistryKeyWatcher(string path, RegistryKey key, Action changed)
    {
        _path = path;
        _key = key;
        _changed = changed;
    }

    /// <summary>Starts watching <paramref name="subKey"/> under HKCU, or returns null if it does not exist or cannot be watched.</summary>
    public static RegistryKeyWatcher? TryCreate(string subKey, Action changed)
    {
        RegistryKey? key;
        try
        {
            key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Cannot open HKCU\\{subKey} for change notifications", ex);
            return null;
        }

        if (key is null)
        {
            return null;
        }

        var watcher = new RegistryKeyWatcher(subKey, key, changed);
        if (!watcher.Arm())
        {
            watcher.Dispose();
            return null;
        }

        watcher._wait = ThreadPool.RegisterWaitForSingleObject(watcher._signal, watcher.OnSignaled, null,
            Timeout.Infinite, executeOnlyOnce: false);
        return watcher;
    }

    /// <summary>Notifications are one-shot, so this runs again after every change.</summary>
    private bool Arm()
    {
        var error = NativeMethods.RegNotifyChangeKeyValue(_key.Handle, false,
            NativeMethods.REG_NOTIFY_CHANGE_LAST_SET | NativeMethods.REG_NOTIFY_THREAD_AGNOSTIC,
            _signal.SafeWaitHandle, asynchronous: true);
        if (error != 0)
        {
            Log.Warn($"RegNotifyChangeKeyValue failed for HKCU\\{_path} (error {error})");
            return false;
        }

        return true;
    }

    private void OnSignaled(object? state, bool timedOut)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Re-arm before reporting so a change made while the callback runs is not missed. This runs on a thread-pool
        // thread, where an escaping exception (e.g. the key closed by a Dispose that gave up waiting) ends the process.
        try
        {
            if (Arm())
            {
                _changed();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Change notification for HKCU\\{_path} failed", ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_wait is not null)
        {
            // Wait for an in-flight callback before closing the key it re-arms.
            using var done = new ManualResetEvent(false);
            if (_wait.Unregister(done))
            {
                done.WaitOne(DisposeTimeout);
            }
        }

        // Closing the key cancels the pending notification.
        _key.Dispose();
        _signal.Dispose();
    }
}
