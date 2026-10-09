using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace WinGnome.Core.Shell;

/// <summary>Where a start-up item comes from, in the order Windows runs them.</summary>
public enum StartupSource
{
    RunOnceMachine,
    RunOnceUser,
    RunMachine,

    /// <summary>32-bit Run key (WOW6432Node); its approval flags live under <c>StartupApproved\Run32</c>.</summary>
    RunMachine32,
    RunUser,
    FolderCommon,
    FolderUser,
}

/// <summary>
/// A start-up item. For registry sources, <paramref name="Name"/> is the value name and
/// <paramref name="CommandLine"/> its data; for folder sources they are the file name and its full path.
/// </summary>
public sealed record StartupEntry(string Name, string CommandLine, StartupSource Source);

public enum StartupApproval
{
    /// <summary>No StartupApproved data, or a flag byte we don't know: runs.</summary>
    Enabled,
    Disabled,
}

public enum StartupSkipReason
{
    Disabled,
    OwnEntry,
    EmptyCommand,
    MalformedCommand,
    SafeMode,
    FolderMetadata,
}

/// <summary>An executable and its arguments split from a command line.</summary>
/// <param name="NeedsEnvironmentExpansion">Contains <c>%VAR%</c> references; the caller expands them (left as-is here).</param>
public sealed record ParsedCommand(string Executable, string Arguments, bool NeedsEnvironmentExpansion);

/// <param name="Entry">The entry as given (the value name keeps any ! or * prefix, so it can be deleted).</param>
/// <param name="Command">The parsed command line.</param>
/// <param name="DeleteBeforeRun">RunOnce value to delete before launching (the default for RunOnce).</param>
/// <param name="DeleteAfterSuccess">RunOnce value with the "!" prefix: delete only once the command succeeded.</param>
public sealed record StartupStep(StartupEntry Entry, ParsedCommand Command, bool DeleteBeforeRun, bool DeleteAfterSuccess);

public sealed record SkippedStartup(StartupEntry Entry, StartupSkipReason Reason);

public sealed record StartupPlan(IReadOnlyList<StartupStep> Steps, IReadOnlyList<SkippedStartup> Skipped);

/// <summary>StartupApproved flags: <c>StartupApproved\Run</c>, <c>\Run32</c> and <c>\StartupFolder</c>, per hive. Keyed by source, so each key stays separate.</summary>
public sealed class StartupApprovedSet
{
    private readonly Dictionary<(StartupSource, string), byte[]> _flags = [];

    /// <summary>Sets the raw binary value for an entry's name under its source's StartupApproved key.</summary>
    public StartupApprovedSet Add(StartupSource source, string name, byte[] data)
    {
        _flags[(source, name.ToUpperInvariant())] = data;
        return this;
    }

    public StartupApproval Get(StartupSource source, string name) =>
        _flags.TryGetValue((source, name.ToUpperInvariant()), out var data) ? Parse(data) : StartupApproval.Enabled;

    /// <summary>0x02/0x06 enabled, 0x03/0x07 disabled. Null, empty and unknown first bytes count as enabled.</summary>
    public static StartupApproval Parse(byte[]? data) =>
        data is { Length: > 0 } && data[0] is 0x03 or 0x07 ? StartupApproval.Disabled : StartupApproval.Enabled;

    /// <summary>
    /// The 12-byte value Windows (Task Manager, Windows Settings) writes: enabled is <c>02 00 00 00</c> and eight zero
    /// bytes; disabled is <c>03 00 00 00</c> and the time of the change as a little-endian UTC FILETIME.
    /// </summary>
    /// <param name="fileTimeUtc">When the item was disabled (<c>DateTime.ToFileTimeUtc</c>); ignored for enabled.</param>
    public static byte[] Encode(StartupApproval approval, long fileTimeUtc)
    {
        var data = new byte[12];
        if (approval == StartupApproval.Disabled)
        {
            data[0] = 0x03;
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(4), fileTimeUtc);
        }
        else
        {
            data[0] = 0x02;
        }

        return data;
    }
}

/// <summary>
/// Turns what the registry and Startup folders hold into the ordered list of things the shell launches
/// at sign-in: RunOnce (machine, then user), Run (machine, then user), then the Startup folders
/// (common, then user). Pure; the app reads the OS and performs the plan.
/// </summary>
public static partial class StartupRunner
{
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat", ".cmd", ".msc", ".lnk", ".scr"];

    public static StartupPlan BuildPlan(
        IEnumerable<StartupEntry> entries,
        StartupApprovedSet approved,
        string ownEntryName,
        bool safeMode)
    {
        var steps = new List<StartupStep>();
        var skipped = new List<SkippedStartup>();

        foreach (var entry in entries.OrderBy(e => (int)e.Source))
        {
            var isFolder = entry.Source is StartupSource.FolderCommon or StartupSource.FolderUser;
            var isRunOnce = entry.Source is StartupSource.RunOnceMachine or StartupSource.RunOnceUser;
            var (name, afterSuccess, runInSafeMode) = isRunOnce ? StripRunOnceFlags(entry.Name) : (entry.Name, false, false);

            if (isFolder && IsFolderMetadata(name))
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.FolderMetadata));
            }
            else if (string.Equals(name, ownEntryName, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.OwnEntry));
            }
            else if (!isRunOnce && approved.Get(entry.Source, entry.Name) == StartupApproval.Disabled)
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.Disabled));
            }
            else if (safeMode && !runInSafeMode)
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.SafeMode));
            }
            else if (string.IsNullOrWhiteSpace(entry.CommandLine))
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.EmptyCommand));
            }
            else if (!TryParseCommand(entry.CommandLine, isFolder, out var command))
            {
                skipped.Add(new SkippedStartup(entry, StartupSkipReason.MalformedCommand));
            }
            else
            {
                steps.Add(new StartupStep(entry, command, DeleteBeforeRun: isRunOnce && !afterSuccess, DeleteAfterSuccess: isRunOnce && afterSuccess));
            }
        }

        return new StartupPlan(steps, skipped);
    }

    /// <summary>
    /// Splits a command line. Quoted: the quoted text is the executable. Unquoted: the executable ends
    /// at the first known extension followed by whitespace or the end (so unquoted paths with spaces
    /// work), else at the first whitespace. A folder item's whole path is the executable.
    /// </summary>
    public static bool TryParseCommand(string commandLine, bool wholeLineIsPath, out ParsedCommand command)
    {
        command = new ParsedCommand("", "", false);
        var text = commandLine.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        string exe;
        string args;
        if (wholeLineIsPath)
        {
            exe = text.Trim('"');
            args = "";
        }
        else if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close <= 1)
            {
                return false;
            }

            exe = text[1..close];
            args = text[(close + 1)..].Trim();
        }
        else
        {
            var end = FindUnquotedExecutableEnd(text);
            exe = text[..end];
            args = text[end..].Trim();
        }

        command = new ParsedCommand(exe, args, EnvironmentReference().IsMatch(text));
        return true;
    }

    private static int FindUnquotedExecutableEnd(string text)
    {
        var best = -1;
        foreach (var extension in ExecutableExtensions)
        {
            var from = 0;
            while (from < text.Length)
            {
                var at = text.IndexOf(extension, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    break;
                }

                var end = at + extension.Length;
                if (end == text.Length || char.IsWhiteSpace(text[end]))
                {
                    if (best < 0 || end < best)
                    {
                        best = end;
                    }

                    break;
                }

                from = at + 1;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        var space = text.IndexOfAny([' ', '\t']);
        return space < 0 ? text.Length : space;
    }

    private static (string Name, bool AfterSuccess, bool RunInSafeMode) StripRunOnceFlags(string name)
    {
        var afterSuccess = false;
        var safe = false;
        var i = 0;
        while (i < name.Length && name[i] is '!' or '*')
        {
            afterSuccess |= name[i] == '!';
            safe |= name[i] == '*';
            i++;
        }

        return (name[i..], afterSuccess, safe);
    }

    private static bool IsFolderMetadata(string fileName) =>
        string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("%[^%\\s]+%")]
    private static partial Regex EnvironmentReference();
}
