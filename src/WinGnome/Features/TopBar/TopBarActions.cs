using WinGnome.Features.TopBar.Popups;
using WinGnome.Features.TopBar.Services;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar;

/// <summary>Everything a top-bar popup can ask for.</summary>
internal enum TopBarAction
{
    Notifications,
    DateTimeSettings,
    WifiSettings,
    BluetoothSettings,
    WindowsQuickSettings,
    ShowSystemTray,
    Screenshot,
    WindowsSettings,
    WinGnomeSettings,
    Lock,
    Suspend,
    Restart,
    ShutDown,
    SignOut,
    Quit,
}

/// <summary>Carries out <see cref="TopBarAction"/>s. Restart, shut down and sign out always ask first.</summary>
internal sealed class TopBarActions(ShellContext context)
{
    /// <summary>True for the actions that show a confirmation dialog before running.</summary>
    public static bool AsksForConfirmation(TopBarAction action) =>
        action is TopBarAction.Restart or TopBarAction.ShutDown or TopBarAction.SignOut;

    public void Execute(TopBarAction action)
    {
        switch (action)
        {
            case TopBarAction.Notifications:
                ShellShortcuts.OpenNotificationCenter();
                break;
            case TopBarAction.DateTimeSettings:
                context.Launcher.Launch("ms-settings:dateandtime");
                break;
            case TopBarAction.WifiSettings:
                context.Launcher.Launch("ms-settings:network-wifi");
                break;
            case TopBarAction.BluetoothSettings:
                context.Launcher.Launch("ms-settings:bluetooth");
                break;
            case TopBarAction.WindowsQuickSettings:
                ShellShortcuts.OpenWindowsQuickSettings();
                break;
            case TopBarAction.ShowSystemTray:
                // Tray icons cannot be re-hosted; they live in the (hidden) native taskbar.
                context.Commands.PeekTaskbar();
                break;
            case TopBarAction.Screenshot:
                ShellShortcuts.StartScreenshot();
                break;
            case TopBarAction.WindowsSettings:
                context.Launcher.Launch("ms-settings:");
                break;
            case TopBarAction.WinGnomeSettings:
                context.Commands.ShowSettings();
                break;
            case TopBarAction.Quit:
                context.Commands.Quit();
                break;
            default:
                ExecutePowerAction(action);
                break;
        }
    }

    private void ExecutePowerAction(TopBarAction action)
    {
        // Belt and braces: power actions are only reachable by clicking, but an unattended run must never trigger one.
        if (context.Options.SelfTest)
        {
            Log.Info($"Self-test: skipped power action {action}");
            return;
        }

        switch (action)
        {
            case TopBarAction.Lock:
                PowerActions.Lock();
                break;
            case TopBarAction.Suspend:
                PowerActions.Suspend();
                break;
            case TopBarAction.Restart when ConfirmDialog.Ask("Restart", "Restart the computer now? Unsaved work in open apps may be lost.", "Restart"):
                PowerActions.Restart();
                break;
            case TopBarAction.ShutDown when ConfirmDialog.Ask("Power off", "Shut down the computer now? Unsaved work in open apps may be lost.", "Power Off"):
                PowerActions.ShutDown();
                break;
            case TopBarAction.SignOut when ConfirmDialog.Ask("Log out", "Sign out of Windows now? Unsaved work in open apps may be lost.", "Log Out"):
                PowerActions.SignOut();
                break;
        }
    }
}
