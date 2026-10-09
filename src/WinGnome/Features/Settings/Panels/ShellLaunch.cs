using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// Starts Windows tools and planned commands (uninstallers) for the settings panels. Everything goes through
/// ShellExecuteEx on a <see cref="ShellThread"/>: never on the dispatcher (it can block on handler and elevation checks)
/// and never CreateProcess, so a program whose manifest asks for elevation or uiAccess (osk.exe, magnify.exe, Narrator,
/// most uninstallers) gets its own UAC prompt instead of failing with ERROR_ELEVATION_REQUIRED. WinGnome never asks
/// for elevation itself (no "runas").
/// </summary>
internal static class ShellLaunch
{
    /// <summary>Starts <c>%SystemRoot%\System32\<paramref name="exe"/></c>, rooted so the search path can't substitute another program.</summary>
    /// <param name="exe">A file name in System32, for example "osk.exe". Paths are rejected.</param>
    /// <param name="arguments">Command-line arguments, already quoted; empty for none.</param>
    public static void SystemTool(string exe, string arguments = "")
    {
        if (string.IsNullOrWhiteSpace(exe) || exe.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) >= 0)
        {
            throw new ArgumentException($"\"{exe}\" is not a plain file name.", nameof(exe));
        }

        var path = Path.Combine(Environment.SystemDirectory, exe);
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        ShellThread.Run($"ShellLaunch: could not start {path}", () => Execute(path, arguments, keepProcess: false, out _));
    }

    /// <summary>
    /// Starts <paramref name="command"/> and posts <paramref name="exited"/> to the caller's synchronisation context (the
    /// dispatcher) when its process ends, so the panel can refresh. When there is no process to wait on (the launch
    /// failed, or Windows handed it to an already-running process) <paramref name="exited"/> is posted at once.
    /// Dispose the result when the panel closes: the wait is cancelled, the process handle closed, and
    /// <paramref name="exited"/> no longer runs.
    /// </summary>
    public static IDisposable StartAndWatch(PlannedCommand command, Action exited)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exited);
        if (!Path.IsPathFullyQualified(command.Executable))
        {
            throw new ArgumentException($"\"{command.Executable}\" is not a fully qualified path.", nameof(command));
        }

        var watch = new ProcessWatch(SynchronizationContext.Current, exited);
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        ShellThread.Run($"ShellLaunch: could not start {command.Executable}", () =>
        {
            try
            {
                Execute(command.Executable, command.Arguments, keepProcess: true, out var process);
                watch.Attach(process);
            }
            catch
            {
                // ShellThread logs the exception; the panel still needs its refresh.
                watch.Attach(0);
                throw;
            }

            return true;
        });
        return watch;
    }

    /// <summary>Runs ShellExecuteEx. "No" on a UAC prompt is the user's choice, so it is logged at Info, not as a failure.</summary>
    /// <param name="process">With <paramref name="keepProcess"/>, the started process's handle (the caller closes it), or 0.</param>
    private static bool Execute(string file, string arguments, bool keepProcess, out nint process)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            // NOASYNC: the launch runs on a short-lived shell thread that exits straight after the call.
            fMask = NativeMethods.SEE_MASK_NOASYNC | (keepProcess ? NativeMethods.SEE_MASK_NOCLOSEPROCESS : 0),
            lpFile = file,
            lpParameters = string.IsNullOrEmpty(arguments) ? null : arguments,
            lpDirectory = Path.GetDirectoryName(file),
            nShow = NativeMethods.SW_SHOWNORMAL,
        };

        if (NativeMethods.ShellExecuteEx(ref info))
        {
            process = info.hProcess;
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == NativeMethods.ERROR_CANCELLED)
        {
            Log.Info($"ShellLaunch: start of {file} cancelled");
        }
        else
        {
            Log.Warn($"ShellLaunch: ShellExecuteEx({file}) failed with Win32 error {error}");
        }

        process = 0;
        return false;
    }

    /// <summary>A one-shot wait on a started process, posting a callback when it exits unless it was disposed first.</summary>
    private sealed class ProcessWatch(SynchronizationContext? context, Action exited) : IDisposable
    {
        private readonly object _lock = new();
        private ProcessWaitHandle? _process;
        private RegisteredWaitHandle? _wait;
        private bool _finished;
        private volatile bool _cancelled;

        /// <summary>Called once on the shell thread with the process handle, or 0 when there is none to wait on.</summary>
        public void Attach(nint process)
        {
            lock (_lock)
            {
                if (_finished)
                {
                    // Disposed before the launch returned (or attached twice after a failure): just close the handle.
                    CloseUnwatched(process);
                    return;
                }

                if (process != 0)
                {
                    _process = new ProcessWaitHandle(process);
                    _wait = ThreadPool.RegisterWaitForSingleObject(_process, (_, _) => OnExited(), null, Timeout.Infinite, executeOnlyOnce: true);
                    return;
                }

                Release();
            }

            Post();
        }

        public void Dispose()
        {
            _cancelled = true;
            lock (_lock)
            {
                Release();
            }
        }

        private void OnExited()
        {
            lock (_lock)
            {
                if (_finished)
                {
                    return;
                }

                Release();
            }

            Post();
        }

        /// <summary>Cancels the wait and closes the process handle. Call under the lock; safe to call twice.</summary>
        private void Release()
        {
            _finished = true;
            _wait?.Unregister(null);
            _wait = null;
            _process?.Dispose();
            _process = null;
        }

        private void Post()
        {
            void Run()
            {
                // Dispose runs on the same context, so this check is exact there: a closed panel gets no callback.
                if (!_cancelled)
                {
                    exited();
                }
            }

            if (context is null)
            {
                Run();
            }
            else
            {
                context.Post(_ => Run(), null);
            }
        }

        private static void CloseUnwatched(nint process)
        {
            if (process != 0)
            {
                new SafeWaitHandle(process, ownsHandle: true).Dispose();
            }
        }
    }

    /// <summary>Wraps a process handle (which is signalled when the process ends) so the thread pool can wait on it; disposing closes it.</summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(nint process) => SafeWaitHandle = new SafeWaitHandle(process, ownsHandle: true);
    }
}
