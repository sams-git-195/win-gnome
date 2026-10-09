using WinGnome.Core.Shell;

namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Decides how a desktop app is uninstalled, and refuses anything that could run a program other than the one the app
/// registered: the result is always a fully qualified executable, never a name Windows would look up on the search
/// path. When there is no safe command the panel hands the app over to Windows Settings instead.
/// </summary>
public static class UninstallPlan
{
    // System programs installers name without a path; they are rooted at System32 instead of trusting the search path.
    private static readonly string[] SystemPrograms = ["msiexec", "rundll32"];

    /// <summary>
    /// The command that uninstalls <paramref name="record"/>, or null when it can't be run safely:
    /// <list type="number">
    /// <item><c>NoRemove</c> set: null.</item>
    /// <item>A Windows Installer key whose name is a product code: <c>&lt;System32&gt;\msiexec.exe /X {GUID}</c>.</item>
    /// <item>Otherwise the parsed <c>UninstallString</c>, with a bare <c>msiexec</c> or <c>rundll32</c> rooted at System32
    /// (rundll32 only when the DLL it loads is fully qualified).</item>
    /// <item>Any other executable must be a fully qualified <c>.exe</c>; a bare name, relative path, other file type or
    /// unexpanded <c>%VAR%</c> gives null.</item>
    /// </list>
    /// </summary>
    /// <param name="systemDirectory">The fully qualified System32 folder, for example <c>C:\Windows\System32</c>.</param>
    public static PlannedCommand? For(InstalledAppRecord record, string systemDirectory)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!WindowsPath.IsFullyQualified(systemDirectory))
        {
            throw new ArgumentException($"\"{systemDirectory}\" is not a fully qualified folder.", nameof(systemDirectory));
        }

        if (record.NoRemove)
        {
            return null;
        }

        if (record.WindowsInstaller && Guid.TryParseExact(record.KeyName, "B", out var productCode))
        {
            return new PlannedCommand(InSystem(systemDirectory, "msiexec.exe"), "/X " + productCode.ToString("B").ToUpperInvariant());
        }

        if (string.IsNullOrWhiteSpace(record.UninstallString)
            || !StartupRunner.TryParseCommand(record.UninstallString, wholeLineIsPath: false, out var command)
            || command.NeedsEnvironmentExpansion)
        {
            return null;
        }

        var executable = command.Executable.Trim();
        if (SystemProgram(executable) is { } program)
        {
            // rundll32 loads the DLL it is given through the DLL search path unless the path is fully qualified.
            if (program == "rundll32" && !WindowsPath.IsFullyQualified(Rundll32Library(command.Arguments)))
            {
                return null;
            }

            return new PlannedCommand(InSystem(systemDirectory, program + ".exe"), command.Arguments);
        }

        // Only an .exe: an unquoted "C:\Program Files\App\uninst /S" splits at the first space, and ShellExecute could
        // then run a planted C:\Program.exe.
        return WindowsPath.IsFullyQualified(executable) && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? new PlannedCommand(executable, command.Arguments)
            : null;
    }

    /// <summary>"msiexec" for "msiexec" or "MsiExec.exe" (bare names only), otherwise null.</summary>
    private static string? SystemProgram(string executable)
    {
        foreach (var program in SystemPrograms)
        {
            if (string.Equals(executable, program, StringComparison.OrdinalIgnoreCase)
                || string.Equals(executable, program + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return program;
            }
        }

        return null;
    }

    /// <summary>The DLL in rundll32's "<c>dll,Entry args</c>" (quoted or not), or an empty string.</summary>
    private static string Rundll32Library(string arguments)
    {
        var text = arguments.Trim();
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : "";
        }

        var end = text.IndexOfAny([',', ' ', '\t']);
        return end < 0 ? text : text[..end];
    }

    private static string InSystem(string systemDirectory, string fileName) => systemDirectory.TrimEnd('\\', '/') + "\\" + fileName;
}
