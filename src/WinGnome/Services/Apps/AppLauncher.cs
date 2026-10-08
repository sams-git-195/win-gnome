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
/// desktop AUMIDs or known-folder AppsFolder parsing names.
/// </summary>
internal sealed class AppLauncher : IAppLauncher
{
    private static readonly string ExplorerPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private readonly Func<string, string> _resolvePath;

    /// <param name="resolvePath">Expands "{KNOWNFOLDERID}\..." launch ids to file paths (wired to <see cref="IAppCatalog.ResolvePath"/>).</param>
    public AppLauncher(Func<string, string> resolvePath)
    {
        ArgumentNullException.ThrowIfNull(resolvePath);
        _resolvePath = resolvePath;
    }

    public bool Launch(string launchId, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            Log.Warn("AppLauncher: empty launch id");
            return false;
        }

        var id = launchId.Trim();

        // We are (usually) the foreground process right after a click; let the new app take focus.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        try
        {
            if (IsUri(id))
            {
                return ShellExecute(id, arguments: null, workingDirectory: null);
            }

            if (Path.IsPathRooted(id) && File.Exists(id))
            {
                return StartFile(id, arguments);
            }

            if (KnownFolderPath.TryParse(id, out _, out _) && _resolvePath(id) is { } resolved
                && Path.IsPathRooted(resolved) && File.Exists(resolved)
                && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return StartFile(resolved, arguments);
            }

            if (id.Contains('!'))
            {
                ActivatePackagedApp(id, arguments);
                return true;
            }

            return StartViaAppsFolder(id);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or COMException or InvalidOperationException)
        {
            Log.Warn($"AppLauncher: could not launch '{id}'", ex);
            return false;
        }
    }

    /// <summary>
    /// True for "scheme:rest" strings such as "ms-settings:", "shell:RecycleBinFolder" or "https://...".
    /// A scheme is at least two characters, so drive-letter paths ("C:\...") never qualify.
    /// </summary>
    private static bool IsUri(string id)
    {
        var colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || !char.IsAsciiLetter(id[0]))
        {
            return false;
        }

        foreach (var c in id.AsSpan(0, colon))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Opens an existing file. Executables start in their own folder; shortcuts keep their "Start in".</summary>
    private static bool StartFile(string path, string? arguments)
    {
        var workingDirectory = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : null;
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

    /// <summary>
    /// Asks Explorer to open the AppsFolder item, exactly like clicking it in Start. Explorer has no way to
    /// pass arguments to such items, so any arguments are ignored.
    /// </summary>
    private static bool StartViaAppsFolder(string id)
    {
        var info = new ProcessStartInfo(ExplorerPath) { UseShellExecute = false };
        info.ArgumentList.Add("shell:AppsFolder\\" + id);
        using var process = Process.Start(info);
        return true;
    }

    /// <summary>
    /// Activates a packaged app through IApplicationActivationManager, falling back to Explorer.
    /// ActivateApplication blocks until the app has started (seconds on a cold start), so it runs on a
    /// worker thread to keep the dock responsive; failures are logged there.
    /// </summary>
    private static void ActivatePackagedApp(string aumid, string? arguments)
    {
        _ = Task.Run(() =>
        {
            if (TryActivate(aumid, arguments))
            {
                return;
            }

            try
            {
                StartViaAppsFolder(aumid);
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
            {
                Log.Warn($"AppLauncher: could not launch '{aumid}' through Explorer", ex);
            }
        });
    }

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
