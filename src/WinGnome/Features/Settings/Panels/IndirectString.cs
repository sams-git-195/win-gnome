using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// Resolves the indirect strings Windows stores in the registry for display names ("@%SystemRoot%\system32\shell32.dll,
/// -123" or "@{PackageFullName?ms-resource://...}"). A plain string comes back unchanged.
/// </summary>
internal static class IndirectString
{
    // Display names are short; SHLoadIndirectString truncates anything longer rather than failing.
    private const int BufferLength = 1024;

    /// <summary>The resolved text, or null when <paramref name="source"/> is empty or can't be resolved (logged).</summary>
    public static string? Load(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var buffer = new char[BufferLength];
        var hr = NativeMethods.SHLoadIndirectString(source, buffer, buffer.Length, 0);
        if (hr < 0)
        {
            Log.Warn($"IndirectString: SHLoadIndirectString(\"{source}\") failed with 0x{hr:X8}");
            return null;
        }

        var length = Array.IndexOf(buffer, '\0');
        var text = new string(buffer, 0, length < 0 ? buffer.Length : length);
        return text.Length > 0 ? text : null;
    }
}
