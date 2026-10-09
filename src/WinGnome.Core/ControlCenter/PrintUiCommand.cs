namespace WinGnome.Core.ControlCenter;

/// <summary>What a printer's rundll32 <c>PrintUIEntry</c> window should show.</summary>
public enum PrintUiAction
{
    /// <summary>The print queue (<c>/o</c>).</summary>
    Queue,

    /// <summary>Printer properties (<c>/p</c>).</summary>
    Properties,

    /// <summary>Printing preferences (<c>/e</c>).</summary>
    Preferences,
}

/// <summary>Builds the arguments for <c>rundll32.exe printui.dll,PrintUIEntry</c> (documented, same as Windows' own printer links).</summary>
public static class PrintUiCommand
{
    /// <summary>
    /// The rundll32 arguments for <paramref name="action"/> on the printer <paramref name="printerName"/>, with the name
    /// quoted; or null when the name is empty or contains a double quote (PrintUIEntry has no way to escape one inside
    /// the quoted name, so such a printer is never passed on).
    /// </summary>
    public static string? Build(PrintUiAction action, string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName) || printerName.Contains('"'))
        {
            return null;
        }

        var flag = action switch
        {
            PrintUiAction.Queue => "/o",
            PrintUiAction.Properties => "/p",
            PrintUiAction.Preferences => "/e",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

        return $"printui.dll,PrintUIEntry {flag} /n \"{printerName}\"";
    }
}
