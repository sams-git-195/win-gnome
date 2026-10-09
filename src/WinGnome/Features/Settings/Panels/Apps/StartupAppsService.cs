using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using StartupEntry = WinGnome.Core.Shell.StartupEntry;

namespace WinGnome.Features.Settings.Panels.Apps;

/// <summary>
/// Reads and changes the start-up items: the Run keys, the Startup folders and Windows' StartupApproved values. Changes
/// are limited to the current user: StartupApproved under HKCU (the same value Task Manager writes), shortcuts added to
/// the user's Startup folder, and those shortcuts moved to the Recycle Bin. No Run value is ever written or deleted, and
/// nothing under HKLM is written.
/// </summary>
internal static class StartupAppsService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The user's Startup folder (where added shortcuts go and the only place one can be removed from).</summary>
    public static string UserStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    /// <summary>Every listed start-up item with its approval. Registry and folder reads only; cheap, but done off the UI thread.</summary>
    public static IReadOnlyList<StartupAppRow> Read()
    {
        var entries = new List<StartupEntry>();
        ReadRun(Registry.CurrentUser, RunKey, StartupSource.RunUser, entries);
        using (var machine64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var machine32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        {
            ReadRun(machine64, RunKey, StartupSource.RunMachine, entries);
            ReadRun(machine32, RunKey, StartupSource.RunMachine32, entries);
        }

        ReadFolder(UserStartupFolder, StartupSource.FolderUser, entries);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.FolderCommon, entries);

        var approvals = new StartupApprovedSet();
        foreach (var source in new[] { StartupSource.RunUser, StartupSource.FolderUser, StartupSource.RunMachine, StartupSource.RunMachine32, StartupSource.FolderCommon })
        {
            ReadApprovals(source, approvals);
        }

        return StartupAppList.Build(entries, approvals, WinGnome.Core.Settings.StartupEntry.ValueName);
    }

    /// <summary>
    /// Turns a current-user item on or off by writing its HKCU StartupApproved value in Windows' own format, then reads
    /// the value back. Returns false (logged) when the item isn't the user's or the value read back differs.
    /// </summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (StartupAppList.ApprovalLocation(entry.Source) is not { Machine: false } location)
        {
            Log.Warn($"Apps: refused to change the machine-wide start-up item \"{entry.Name}\"");
            return false;
        }

        var wanted = enabled ? StartupApproval.Enabled : StartupApproval.Disabled;
        var path = StartupAppList.ApprovedKeyPath + "\\" + location.KeyName;
        using (var key = Registry.CurrentUser.CreateSubKey(path, writable: true))
        {
            key.SetValue(entry.Name, StartupApprovedSet.Encode(wanted, DateTime.UtcNow.ToFileTimeUtc()), RegistryValueKind.Binary);
        }

        using var check = Registry.CurrentUser.OpenSubKey(path);
        var stored = StartupApprovedSet.Parse(check?.GetValue(entry.Name) as byte[]);
        if (stored != wanted)
        {
            Log.Warn($"Apps: start-up item \"{entry.Name}\" reads back as {stored} after writing {wanted}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a shortcut to an AppsFolder item (desktop or packaged app) in the user's Startup folder, named after the
    /// app. Runs shell COM: call it on a <c>ShellThread</c>. Returns the new file's path, or null (logged) on failure.
    /// </summary>
    public static string? AddShortcut(string appName, string parsingName)
    {
        var folder = UserStartupFolder;
        Directory.CreateDirectory(folder);
        var existing = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>();
        var path = Path.Combine(folder, StartupShortcutName.For(appName, existing));

        var hr = NativeMethods.SHParseDisplayName(@"shell:AppsFolder\" + parsingName, 0, out var idList, 0, out _);
        if (hr < 0 || idList == 0)
        {
            Log.Warn($"Apps: SHParseDisplayName for start-up shortcut \"{parsingName}\" failed with 0x{hr:X8}");
            return null;
        }

        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(NativeMethods.CLSID_ShellLink, throwOnError: true)!);
            hr = ((IShellLinkW)link!).SetIDList(idList);
            if (hr < 0)
            {
                Log.Warn($"Apps: IShellLink.SetIDList for \"{parsingName}\" failed with 0x{hr:X8}");
                return null;
            }

            ((IPersistFile)link).Save(path, fRemember: true);
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }

            Marshal.FreeCoTaskMem(idList);
        }

        if (!File.Exists(path))
        {
            Log.Warn($"Apps: start-up shortcut {path} wasn't created");
            return null;
        }

        Log.Info($"Apps: added start-up shortcut {path} for \"{parsingName}\"");
        return path;
    }

    /// <summary>
    /// Moves a shortcut in the user's Startup folder to the Recycle Bin (the shell asks before it would have to delete it
    /// outright). Anything outside that folder is refused. Runs shell COM: call it on a <c>ShellThread</c>. Returns true
    /// when the file is gone.
    /// </summary>
    public static bool Recycle(string path)
    {
        var folder = Path.GetFullPath(UserStartupFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), folder, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Apps: refused to remove {path}: not in the user's Startup folder");
            return false;
        }

        object? itemObject = null;
        object? operation = null;
        try
        {
            var itemId = typeof(IShellItem).GUID;
            var hr = NativeMethods.SHCreateItemFromParsingName(path, 0, ref itemId, out itemObject);
            if (hr < 0 || itemObject is not IShellItem item)
            {
                Log.Warn($"Apps: SHCreateItemFromParsingName({path}) failed with 0x{hr:X8}");
                return false;
            }

            operation = Activator.CreateInstance(Type.GetTypeFromCLSID(NativeMethods.CLSID_FileOperation, throwOnError: true)!);
            var files = (IFileOperation)operation!;
            hr = files.SetOperationFlags(NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION
                | NativeMethods.FOF_WANTNUKEWARNING | NativeMethods.FOFX_RECYCLEONDELETE);
            if (hr >= 0)
            {
                hr = files.DeleteItem(item, 0);
            }

            if (hr >= 0)
            {
                hr = files.PerformOperations();
            }

            if (hr < 0)
            {
                // COPYENGINE_E_USER_CANCELLED and ERROR_CANCELLED land here too: the user said no to the shell's prompt.
                Log.Warn($"Apps: moving {path} to the Recycle Bin failed with 0x{hr:X8}");
            }
        }
        finally
        {
            if (operation is not null)
            {
                Marshal.ReleaseComObject(operation);
            }

            if (itemObject is not null)
            {
                Marshal.ReleaseComObject(itemObject);
            }
        }

        var gone = !File.Exists(path);
        if (gone)
        {
            Log.Info($"Apps: moved start-up shortcut {path} to the Recycle Bin");
        }

        return gone;
    }

    private static void ReadRun(RegistryKey hive, string path, StartupSource source, List<StartupEntry> into)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                // The default value isn't a start-up item.
                if (name.Length > 0 && key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                {
                    into.Add(new StartupEntry(name, command, source));
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Apps: could not read the {source} start-up items", ex);
        }
    }

    private static void ReadFolder(string folder, StartupSource source, List<StartupEntry> into)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                into.Add(new StartupEntry(Path.GetFileName(file), file, source));
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Apps: could not read the Startup folder {folder}", ex);
        }
    }

    private static void ReadApprovals(StartupSource source, StartupApprovedSet into)
    {
        var location = StartupAppList.ApprovalLocation(source)!;
        try
        {
            using var hive = location.Machine
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.OpenSubKey(StartupAppList.ApprovedKeyPath + "\\" + location.KeyName);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] data)
                {
                    into.Add(source, name, data);
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Apps: could not read the {source} StartupApproved values", ex);
        }
    }
}
