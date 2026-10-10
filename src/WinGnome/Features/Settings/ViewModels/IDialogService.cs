using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>The modal prompts view models need, implemented by the settings window.</summary>
internal interface IDialogService
{
    /// <summary>Asks the user to confirm an action. Returns true when they chose <paramref name="confirmLabel"/>.</summary>
    /// <param name="title">Dialog heading.</param>
    /// <param name="message">What will happen.</param>
    /// <param name="confirmLabel">Text of the confirming button, for example "Reset".</param>
    /// <param name="isDestructive">Paints the confirming button as a destructive action.</param>
    bool Confirm(string title, string message, string confirmLabel, bool isDestructive = false);

    /// <summary>Lets the user pick an installed app, hiding the ones in <paramref name="hiddenLaunchIds"/>. Returns null when cancelled.</summary>
    AppEntry? PickApp(IReadOnlyCollection<string> hiddenLaunchIds);

    /// <summary>
    /// Asks for the password of the Wi-Fi network <paramref name="networkName"/>. Returns the typed characters, which the
    /// caller owns and must clear, or null when cancelled. <paramref name="note"/> explains why it is asked again.
    /// </summary>
    char[]? PromptWifiPassword(string networkName, string? note);
}
