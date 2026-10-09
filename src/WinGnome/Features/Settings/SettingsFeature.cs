using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.Panels.Displays;
using WinGnome.Features.Settings.Startup;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Features.Settings.Views;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings;

/// <summary>
/// Owns the settings window (one instance, opened by <see cref="ShellCommands.SettingsRequested"/>), keeps the
/// "Start with Windows" entry in step with the setting, and hosts the streamline tweak service.
/// </summary>
[FeatureOrder(90)]
internal sealed class SettingsFeature : IFeature
{
    private readonly ShellContext _context;
    private readonly StartupRegistration _startup;
    private readonly TweakService _tweaks;
    private SettingsWindow? _window;

    public SettingsFeature(ShellContext context)
    {
        _context = context;
        _startup = new StartupRegistration(simulate: !context.ManagesStartupEntry);
        _tweaks = new TweakService(context.Settings, simulate: context.IsSafeMode);
    }

    public string Name => "Settings";

    public void Start(AppSettings settings)
    {
        _startup.Sync(settings.General.StartWithWindows);
        _tweaks.SyncEnabledSetting();
        RecoverUnconfirmedDisplayChange();
        _context.Commands.SettingsRequested += OnSettingsRequested;

        if (_context.Options.SelfTest)
        {
            _context.Dispatcher.BeginInvoke(RunSelfTest, DispatcherPriority.Background);
        }
    }

    public void ApplySettings(AppSettings settings) => _startup.Sync(settings.General.StartWithWindows);

    public void Dispose()
    {
        _context.Commands.SettingsRequested -= OnSettingsRequested;
        _window?.Close();
    }

    /// <summary>
    /// Reverts a display change that was applied but never confirmed because WinGnome stopped during the countdown.
    /// Safe mode changes no system state, so the record waits for a normal start.
    /// </summary>
    private void RecoverUnconfirmedDisplayChange()
    {
        if (_context.IsSafeMode)
        {
            return;
        }

        DisplayRevertFile.RecoverIfPending(_context.Settings.Directory);
    }

    private void OnSettingsRequested(object? sender, string? panelId)
    {
        if (_window is null)
        {
            _window = new SettingsWindow(_context, _tweaks);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }

        if (panelId is not null)
        {
            _window.ShowPanel(panelId);
        }

        _window.BringToFront();
    }

    /// <summary>Opens the window, visits every page so each view is built, then closes it. Failures reach the application's failure count.</summary>
    private void RunSelfTest()
    {
        var window = new SettingsWindow(_context, _tweaks);
        try
        {
            window.Show();
            window.VisitAllPages();
            Log.Info("Settings self-test OK");
        }
        finally
        {
            window.Close();
        }
    }
}
