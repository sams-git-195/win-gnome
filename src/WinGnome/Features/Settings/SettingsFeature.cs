using System.Globalization;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.Panels.Displays;
using WinGnome.Features.Settings.Panels.RegionLanguage;
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
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (_context.Options.SelfTest)
        {
            _context.Dispatcher.BeginInvoke(RunSelfTest, DispatcherPriority.Background);
        }
    }

    public void ApplySettings(AppSettings settings) => _startup.Sync(settings.General.StartWithWindows);

    public void Dispose()
    {
        _context.Commands.SettingsRequested -= OnSettingsRequested;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _window?.CloseForShutdown();

        // Even with no window, a save, revert or start-up recovery may still be running on the display queue.
        DisplayWork.WaitAtShutdown();
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

        // Queued on the shared display queue, off the UI thread: changing display modes waits on every top-level window.
        DisplayRevertFile.RecoverIfPending(_context.Settings.Directory);
    }

    // Region & Language (here or in Windows Settings) broadcasts WM_SETTINGCHANGE "intl". ClearCachedData drops .NET's
    // static caches, but the existing CurrentCulture instance keeps the user's formats it already read, so the clock and
    // calendar would show the old ones until WinGnome restarts. A new instance reads them again; DefaultThreadCurrentCulture
    // makes it the culture of every thread that has not set its own. The name is Windows' current format locale, not the
    // old culture's, so a format-locale change made in Windows Settings is followed too. The instance is read-only
    // because every thread shares it.
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.Locale)
        {
            return;
        }

        _context.Dispatcher.BeginInvoke(() =>
        {
            CultureInfo.CurrentCulture.ClearCachedData();
            var name = RegionService.ReadLocaleName();
            CultureInfo fresh;
            try
            {
                fresh = CultureInfo.ReadOnly(new CultureInfo(name.Length > 0 ? name : CultureInfo.CurrentCulture.Name, useUserOverride: true));
            }
            catch (CultureNotFoundException ex)
            {
                Log.Warn($"Settings: .NET has no culture for the format locale \"{name}\"; keeping {CultureInfo.CurrentCulture.Name}", ex);
                return;
            }

            CultureInfo.CurrentCulture = fresh;
            CultureInfo.DefaultThreadCurrentCulture = fresh;
        });
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
