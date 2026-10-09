using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Apps;

/// <summary>
/// Starts apps from dock pins and search results. Launch ids can be URIs, file paths, packaged AUMIDs,
/// desktop AUMIDs or known-folder AppsFolder parsing names. Everything is launched in-process; nothing goes
/// through explorer.exe, so launching never starts Explorer (which matters when Explorer is not the shell).
/// </summary>
internal sealed class AppLauncher : IAppLauncher
{
    private const string AppsFolderPrefix = "shell:AppsFolder\\";
    private const string RunAsVerb = "runas";

    private readonly Func<string, string> _resolvePath;

    /// <param name="resolvePath">Expands "{KNOWNFOLDERID}\..." launch ids to file paths (wired to <see cref="IAppCatalog.ResolvePath"/>).</param>
    public AppLauncher(Func<string, string> resolvePath)
    {
        ArgumentNullException.ThrowIfNull(resolvePath);
        _resolvePath = resolvePath;
    }

    public bool Launch(string launchId, string? arguments = null) =>
        Launch(new LaunchRequest(launchId ?? "", arguments, Elevate: false));

    public bool Launch(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LaunchId))
        {
            Log.Warn("AppLauncher: empty launch id");
            return false;
        }

        var id = request.LaunchId.Trim();
        var elevate = request.Elevate && LaunchPlanner.CanElevate(id);

        // We are (usually) the foreground process right after a click; let the new app take focus.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        try
        {
            if (LaunchPlanner.IsUri(id))
            {
                return ShellExecute(id, arguments: null, workingDirectory: null);
            }

            if (Path.IsPathRooted(id) && File.Exists(id))
            {
                return StartFile(id, request.Arguments, elevate);
            }

            if (KnownFolderPath.TryParse(id, out _, out _) && _resolvePath(id) is { } resolved
                && Path.IsPathRooted(resolved) && File.Exists(resolved)
                && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return StartFile(resolved, request.Arguments, elevate);
            }

            if (id.Contains('!'))
            {
                // Packaged apps have no "runas"; LaunchPlanner.CanElevate is false for them, so elevate is too.
                ActivatePackagedApp(id, request.Arguments);
                return true;
            }

            if (elevate)
            {
                RunOnShellThread(id, () => OpenAppsFolderItem(id, RunAsVerb));
                return true;
            }

            return OpenAppsFolderItem(id, verb: null);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or COMException or InvalidOperationException)
        {
            Log.Warn($"AppLauncher: could not launch '{id}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Opens an existing file. Executables start in their own folder; shortcuts keep their "Start in".
    /// An elevated launch returns as soon as the UAC prompt is requested; its outcome is logged.
    /// </summary>
    private static bool StartFile(string path, string? arguments, bool elevate)
    {
        var workingDirectory = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : null;
        if (elevate)
        {
            RunOnShellThread(path, () => ShellExecuteElevated(path, arguments, workingDirectory));
            return true;
        }

        return ShellExecute(path, arguments, workingDirectory);
    }

    private static bool ShellExecute(string target, string? arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(target) { UseShellExecute = true };
        if (!string.IsNullOrEmpty(arguments))
        {
            info.Arguments = arguments;
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        // Process.Start may return null when the shell reuses an existing process; that is still success.
        using var process = Process.Start(info);
        return true;
    }

    private static bool ShellExecuteElevated(string path, string? arguments, string? workingDirectory)
    {
        var info = NewExecuteInfo(RunAsVerb);
        info.lpFile = path;
        info.lpParameters = string.IsNullOrEmpty(arguments) ? null : arguments;
        info.lpDirectory = workingDirectory;
        return Execute(ref info, path);
    }

    /// <summary>
    /// Invokes an AppsFolder item like clicking it in Start, through its ID list, so it runs in this process
    /// rather than via explorer.exe. AppsFolder items take no arguments, so any are ignored.
    /// </summary>
    private static bool OpenAppsFolderItem(string id, string? verb)
    {
        var hr = NativeMethods.SHParseDisplayName(AppsFolderPrefix + id, 0, out var idList, 0, out _);
        if (hr < 0 || idList == 0)
        {
            Log.Warn($"AppLauncher: SHParseDisplayName('{AppsFolderPrefix}{id}') failed with 0x{hr:X8}");
            return false;
        }

        try
        {
            var info = NewExecuteInfo(verb);
            info.fMask |= NativeMethods.SEE_MASK_INVOKEIDLIST;
            if (verb is null)
            {
                // A normal launch runs on the dispatcher: a shell error box would block it, so failures are only logged.
                // Elevated launches keep the shell's UI, since they run on their own thread and must show UAC.
                info.fMask |= NativeMethods.SEE_MASK_FLAG_NO_UI;
            }

            info.lpIDList = idList;
            return Execute(ref info, id);
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    private static SHELLEXECUTEINFO NewExecuteInfo(string? verb) => new()
    {
        cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
        // NOASYNC: the launch may run on a short-lived worker thread that exits straight after the call.
        fMask = NativeMethods.SEE_MASK_NOASYNC,
        lpVerb = verb,
        nShow = NativeMethods.SW_SHOWNORMAL,
    };

    /// <summary>Runs ShellExecuteEx. "No" on a UAC prompt is the user's choice, so it is logged at Info, not as a failure.</summary>
    private static bool Execute(ref SHELLEXECUTEINFO info, string what)
    {
        var verb = info.lpVerb ?? "default";
        if (info.lpVerb == RunAsVerb)
        {
            Log.Info($"AppLauncher: launching '{what}' elevated (verb {verb}, mask 0x{info.fMask:X})");
        }

        if (NativeMethods.ShellExecuteEx(ref info))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == NativeMethods.ERROR_CANCELLED)
        {
            Log.Info($"AppLauncher: launch of '{what}' cancelled (verb {verb})");
        }
        else
        {
            Log.Warn($"AppLauncher: ShellExecuteEx('{what}', verb {verb}) failed with Win32 error {error}");
        }

        return false;
    }

    /// <summary>
    /// Runs a blocking launch on its own STA thread. An elevated ShellExecuteEx waits until the UAC prompt is answered
    /// and packaged activation waits for a cold start, so neither may run on the dispatcher; the shell wants STA.
    /// </summary>
    private static void RunOnShellThread(string what, Action launch)
    {
        var thread = new Thread(() =>
        {
            try
            {
                launch();
            }
            catch (Exception ex)
            {
                // Thread boundary: an exception escaping here would end the shell process, so everything is logged.
                Log.Warn($"AppLauncher: could not launch '{what}'", ex);
            }
        })
        {
            IsBackground = true,
            Name = "WinGnome launch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Activates a packaged app through IApplicationActivationManager, falling back to its AppsFolder item.</summary>
    private static void ActivatePackagedApp(string aumid, string? arguments) =>
        RunOnShellThread(aumid, () =>
        {
            if (!TryActivate(aumid, arguments))
            {
                OpenAppsFolderItem(aumid, verb: null);
            }
        });

    private static bool TryActivate(string aumid, string? arguments)
    {
        object? managerObject = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellGuids.ApplicationActivationManager, throwOnError: true)!;
            managerObject = Activator.CreateInstance(type);
            if (managerObject is not IApplicationActivationManager manager)
            {
                return false;
            }

            var hr = manager.ActivateApplication(aumid, arguments, ActivateOptions.NoErrorUI, out _);
            if (hr < 0)
            {
                Log.Warn($"AppLauncher: ActivateApplication('{aumid}') failed with 0x{hr:X8}");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn($"AppLauncher: ActivateApplication('{aumid}') threw", ex);
            return false;
        }
        finally
        {
            if (managerObject is not null)
            {
                Marshal.ReleaseComObject(managerObject);
            }
        }
    }
}
