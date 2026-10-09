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
        Launch(new LaunchRequest(launchId ?? "", arguments, Elevate: false), started: null);

    public bool Launch(LaunchRequest request, Action? started = null, nint owner = 0)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LaunchId))
        {
            Log.Warn("AppLauncher: empty launch id");
            return false;
        }

        var id = request.LaunchId.Trim();
        var arguments = request.Arguments;

        // We are (usually) the foreground process right after a click; let the new app take focus.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        try
        {
            if (LaunchPlanner.IsUri(id))
            {
                // URIs go to their scheme's handler and are never elevated.
                ShellExecute(id, arguments: null, workingDirectory: null);
                started?.Invoke();
                return true;
            }

            var path = ExistingFile(id);
            if (request.Elevate)
            {
                // LaunchPlanner decided this (full-trust packaged apps need the catalogue's host, which this class
                // doesn't have). Packaged apps are elevated through their AppsFolder item, as Start does.
                BringOwnerToFront(owner);
                RunOnShellThread(
                    id,
                    () => path is not null
                        ? ShellExecuteElevated(path, arguments, WorkingDirectoryFor(path), owner)
                        : OpenAppsFolderItem(id, RunAsVerb, owner),
                    started);
                return true;
            }

            if (path is not null)
            {
                ShellExecute(path, arguments, WorkingDirectoryFor(path));
                started?.Invoke();
                return true;
            }

            if (id.Contains('!'))
            {
                ActivatePackagedApp(id, arguments);
                started?.Invoke();
                return true;
            }

            // ShellExecuteEx on an ID list goes through the item's context-menu handler, which can take seconds and
            // pumps messages while it waits, so it never runs on the dispatcher.
            RunOnShellThread(id, () => OpenAppsFolderItem(id, verb: null, owner: 0), started);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or COMException or InvalidOperationException)
        {
            Log.Warn($"AppLauncher: could not launch '{id}'", ex);
            return false;
        }
    }

    /// <summary>
    /// UAC shows its prompt in front only when the window that asked for it is the foreground window; otherwise it
    /// starts as a flashing taskbar button the user may never see (the dock is WS_EX_NOACTIVATE, so a click on it
    /// leaves another app in front). We just received the user's click, so we may take the foreground.
    /// </summary>
    private static void BringOwnerToFront(nint owner)
    {
        if (owner == 0 || NativeMethods.GetForegroundWindow() == owner)
        {
            return;
        }

        if (!NativeMethods.SetForegroundWindow(owner))
        {
            Log.Warn($"AppLauncher: SetForegroundWindow(0x{owner:X}) failed; the UAC prompt may open behind other windows");
        }
    }

    /// <summary>The file a launch id names: an existing path, or a known-folder "{GUID}\app.exe" that resolves to one.</summary>
    private string? ExistingFile(string id)
    {
        if (Path.IsPathRooted(id) && File.Exists(id))
        {
            return id;
        }

        return KnownFolderPath.TryParse(id, out _, out _) && _resolvePath(id) is { } resolved
            && Path.IsPathRooted(resolved) && File.Exists(resolved)
            && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? resolved
                : null;
    }

    /// <summary>Executables start in their own folder; shortcuts keep their "Start in".</summary>
    private static string? WorkingDirectoryFor(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : null;

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

    private static bool ShellExecuteElevated(string path, string? arguments, string? workingDirectory, nint owner)
    {
        var info = NewExecuteInfo(RunAsVerb, owner);
        info.lpFile = path;
        info.lpParameters = string.IsNullOrEmpty(arguments) ? null : arguments;
        info.lpDirectory = workingDirectory;
        return Execute(ref info, path);
    }

    /// <summary>
    /// Invokes an AppsFolder item like clicking it in Start, through its ID list, so it runs in this process
    /// rather than via explorer.exe. AppsFolder items take no arguments, so any are ignored.
    /// </summary>
    private static bool OpenAppsFolderItem(string id, string? verb, nint owner)
    {
        var hr = NativeMethods.SHParseDisplayName(AppsFolderPrefix + id, 0, out var idList, 0, out _);
        if (hr < 0 || idList == 0)
        {
            Log.Warn($"AppLauncher: SHParseDisplayName('{AppsFolderPrefix}{id}') failed with 0x{hr:X8}");
            return false;
        }

        try
        {
            var info = NewExecuteInfo(verb, owner);
            info.fMask |= NativeMethods.SEE_MASK_INVOKEIDLIST;
            if (verb is null)
            {
                // An error box for a normal launch would have no owner and outlive the click, so failures are only
                // logged. Elevated launches keep the shell's UI, which must show the UAC prompt.
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

    /// <param name="owner">Window UAC treats as the requester; its being in front decides whether the prompt is too.</param>
    private static SHELLEXECUTEINFO NewExecuteInfo(string? verb, nint owner) => new()
    {
        cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
        // NOASYNC: the launch runs on a short-lived worker thread that exits straight after the call.
        fMask = NativeMethods.SEE_MASK_NOASYNC,
        hwnd = owner,
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
    /// Runs a blocking launch on its own STA thread (see <see cref="ShellThread"/>). When <paramref name="launch"/>
    /// succeeds, <paramref name="started"/> is posted back to the caller's synchronisation context (the dispatcher).
    /// </summary>
    private static void RunOnShellThread(string what, Func<bool> launch, Action? started = null) =>
        ShellThread.Run($"AppLauncher: could not launch '{what}'", launch, started);

    /// <summary>Activates a packaged app through IApplicationActivationManager, falling back to its AppsFolder item.</summary>
    private static void ActivatePackagedApp(string aumid, string? arguments) =>
        RunOnShellThread(aumid, () => TryActivate(aumid, arguments) || OpenAppsFolderItem(aumid, verb: null, owner: 0));

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
