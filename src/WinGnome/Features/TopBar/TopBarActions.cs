using System.IO;
using WinGnome.Core.ControlCenter;
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
    SoundSettings,
    DisplaySettings,
    PowerSettings,
    WindowsQuickSettings,
    ShowSystemTray,
    Screenshot,
    Settings,
    WinGnomeSettings,
    WindowsSettings,
    ControlPanel,
    AboutThisPc,
    MicrosoftStore,
    StartMenu,
    TaskManager,
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
                OpenSettingsPanel(PanelIds.DateTime);
                break;
            case TopBarAction.WifiSettings:
                OpenSettingsPanel(PanelIds.Wifi);
                break;
            case TopBarAction.BluetoothSettings:
                OpenSettingsPanel(PanelIds.Bluetooth);
                break;
            case TopBarAction.SoundSettings:
                OpenSettingsPanel(PanelIds.Sound);
                break;
            case TopBarAction.DisplaySettings:
                OpenSettingsPanel(PanelIds.Displays);
                break;
            case TopBarAction.PowerSettings:
                OpenSettingsPanel(PanelIds.Power);
                break;
            case TopBarAction.WindowsQuickSettings:
                ShellShortcuts.OpenWindowsQuickSettings();
                break;
            case TopBarAction.ShowSystemTray:
                // Explorer's own tray (its overflow and system icons) lives in the hidden native taskbar.
                context.Commands.PeekTaskbar();
                break;
            case TopBarAction.Screenshot:
                ShellShortcuts.StartScreenshot();
                break;
            case TopBarAction.Settings:
                // The settings window as the user left it, or its first page.
                context.Commands.ShowSettings();
                break;
            case TopBarAction.WinGnomeSettings:
                OpenSettingsPanel(PanelIds.General);
                break;
            case TopBarAction.AboutThisPc:
                OpenSettingsPanel(PanelIds.About);
                break;
            case TopBarAction.MicrosoftStore:
                context.Launcher.Launch("ms-windows-store:");
                break;
            case TopBarAction.WindowsSettings:
                // The top-level ms-settings: URI (Win+I); LaunchPlanner.IsUri routes it to the scheme's handler.
                context.Launcher.Launch("ms-settings:");
                break;
            case TopBarAction.StartMenu:
                ShellShortcuts.OpenStartMenu();
                break;
            case TopBarAction.TaskManager:
                context.Launcher.Launch(Path.Combine(Environment.SystemDirectory, "taskmgr.exe"));
                break;
            case TopBarAction.ControlPanel:
                // The classic Control Panel, launched like Task Manager: a rooted System32 path, never elevated.
                context.Launcher.Launch(Path.Combine(Environment.SystemDirectory, "control.exe"));
                break;
            case TopBarAction.Quit:
                context.Commands.Quit();
                break;
            default:
                ExecutePowerAction(action);
                break;
        }
    }

    /// <summary>
    /// Shows a panel of the settings app through <see cref="ShellCommands"/>. Link panels (Wi-Fi, Bluetooth) open their
    /// Windows Settings page straight away, so the row doesn't also bring up the settings window.
    /// </summary>
    private void OpenSettingsPanel(string panelId)
    {
        if (SettingsPanelCatalog.DirectLinkFor(panelId) is { } link)
        {
            context.Launcher.Launch(link);
        }
        else
        {
            context.Commands.ShowSettings(panelId);
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
