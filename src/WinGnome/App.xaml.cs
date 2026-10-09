using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Services;
using WinGnome.Services.Apps;
using WinGnome.Theme;

namespace WinGnome;

/// <summary>
/// Composition root: parses the command line, enforces a single instance, builds the shared services,
/// starts every <see cref="IFeature"/>, and guarantees system state is restored on exit or crash.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Application lifetime is the process lifetime; everything is disposed in StopShell from OnExit.")]
public partial class App : Application
{
    private static readonly TimeSpan SelfTestDuration = TimeSpan.FromSeconds(5);

    private readonly List<IFeature> _features = [];
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private ThemeManager? _theme;
    private WindowTracker? _tracker;
    private AppCatalog? _catalog;
    private ShellContext? _context;
    private ControlWindow? _controlWindow;
    private string _settingsDirectory = SettingsStore.DefaultDirectory;
    private int _failures;
    private int _recentDispatcherErrors;
    private DateTime _dispatcherErrorWindowStart = DateTime.UtcNow;
    private bool _stopped;

    /// <summary>
    /// True once this process holds the single-instance mutex. Only the owner may restore the taskbar: a second
    /// launch (the documented way to open settings) must not undo the running instance's taskbar state.
    /// </summary>
    private volatile bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Software rendering for every window (before any is created). WinGnome runs all day and is idle almost
        // all the time; its windows are small or static and its expensive visuals (blur, acrylic, live window
        // thumbnails) are drawn by DWM either way. Hardware rendering costs a Direct3D device with the GPU
        // driver's heaps and threads plus a swap chain per window, even while nothing animates. Measured on a
        // 2560x1600 Intel iGPU: idle private bytes ~120 -> ~80 MB, threads 43 -> 24, and 166 -> 107 MB after the
        // overview has been used; startup is ~100 ms faster and the overview glide keeps its frame rate.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var options = CommandLineOptions.Parse(e.Args);
        _settingsDirectory = FullPathOrSelf(options.SettingsDirectory ?? SettingsStore.DefaultDirectory);
        Log.Initialize(_settingsDirectory);
        Log.Info($"WinGnome {typeof(App).Assembly.GetName().Version} starting. Args: {string.Join(' ', e.Args)}");
        InstallCrashHandlers();

        if (options.RestoreTaskbar)
        {
            TaskbarController.RestoreFromMarker(_settingsDirectory);
            Shutdown(0);
            return;
        }

        if (!AcquireSingleInstance(options))
        {
            // A self-test that could not run must not report success.
            Shutdown(options.SelfTest ? 1 : 0);
            return;
        }

        // A previous run that crashed (or was killed) may have left the taskbar hidden.
        TaskbarController.RestoreFromMarker(_settingsDirectory);

        try
        {
            StartShell(options);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error during startup", ex);
            StopShell();
            if (!options.SelfTest)
            {
                // A modal dialog would hang an unattended self-test run.
                MessageBox.Show($"WinGnome could not start:\n\n{ex.Message}", "WinGnome", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Shutdown(1);
            return;
        }

        if (options.SelfTest)
        {
            RunSelfTest();
        }
        else if (options.OpenSettings)
        {
            _context!.Commands.ShowSettings(options.SettingsPanel);
        }
    }

    private void StartShell(CommandLineOptions options)
    {
        var settings = new SettingsService(new SettingsStore(_settingsDirectory));
        _theme = new ThemeManager();
        _theme.Apply(settings.Current.General.Theme);

        _tracker = new WindowTracker(Dispatcher);
        _catalog = new AppCatalog(Dispatcher);
        var icons = new IconProvider();
        var launcher = new AppLauncher(_catalog.ResolvePath);
        var commands = new ShellCommands();
        commands.QuitRequested += (_, _) => Shutdown(0);
        _controlWindow = new ControlWindow(() => Dispatcher.BeginInvoke(() => Shutdown(0)));

        _context = new ShellContext(Dispatcher, options, settings, _theme, _tracker, _catalog, icons, launcher, commands);

        _tracker.Start();
        _ = _catalog.RefreshAsync();

        foreach (var type in FeatureDiscovery.FindFeatureTypes())
        {
            try
            {
                var feature = FeatureDiscovery.Create(type, _context);
                _features.Add(feature);
                feature.Start(settings.Current);
                Log.Info($"Started feature {feature.Name}");
            }
            catch (Exception ex)
            {
                _failures++;
                Log.Error($"Feature {type.Name} failed to start", ex);
            }
        }

        settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _theme?.Apply(settings.General.Theme);
        foreach (var feature in _features)
        {
            try
            {
                feature.ApplySettings(settings);
            }
            catch (Exception ex)
            {
                _failures++;
                Log.Error($"Feature {feature.Name} failed to apply settings", ex);
            }
        }
    }

    private void RunSelfTest()
    {
        var timer = new DispatcherTimer { Interval = SelfTestDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // Exercise a settings round-trip so every feature's ApplySettings runs at least once.
            _context!.Settings.Update(_ => { });
            var featureNames = string.Join(", ", _features.Select(f => f.Name));
            Log.Info($"Self-test finished. Features: [{featureNames}]. Failures: {_failures}.");
            Shutdown(_failures == 0 ? 0 : 1);
        };
        timer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopShell();
        Log.Info($"WinGnome exited with code {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void StopShell()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingsChanged;
        }

        // Dispose in reverse start order so dependants go first.
        for (var i = _features.Count - 1; i >= 0; i--)
        {
            try
            {
                _features[i].Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"Feature {_features[i].Name} failed to dispose", ex);
            }
        }

        _features.Clear();
        _controlWindow?.Dispose();
        _catalog?.Dispose();
        _tracker?.Dispose();
        _theme?.Dispose();
        if (_ownsInstance)
        {
            TaskbarController.RestoreFromMarker(_settingsDirectory);
        }

        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            _ownsInstance = false;
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
    }

    private static string FullPathOrSelf(string directory)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return directory;
        }
    }

    /// <summary>
    /// One WinGnome per settings directory. Launching a second copy asks the running one to open
    /// its settings window (WinGnome has no tray icon, so this is how users get back to settings).
    /// </summary>
    private bool AcquireSingleInstance(CommandLineOptions options)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_settingsDirectory.ToUpperInvariant())))[..16];
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\WinGnome-{key}", out var createdNew);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\WinGnome-Activate-{key}");

        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            if (!options.SelfTest)
            {
                _activationEvent.Set();
            }

            Log.Info("Another instance is running; asked it to show settings");
            return false;
        }

        _ownsInstance = true;
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _context?.Commands.ShowSettings()),
            null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception; restoring system state", args.ExceptionObject as Exception);
            EmergencyRestore();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        SessionEnding += (_, _) => StopShell();
    }

    /// <summary>
    /// UI-thread exceptions are logged and swallowed so one faulty feature cannot take down the
    /// shell. A burst of errors means something is badly wrong, so we restore and exit.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _failures++;
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;

        var now = DateTime.UtcNow;
        if (now - _dispatcherErrorWindowStart > TimeSpan.FromMinutes(1))
        {
            _dispatcherErrorWindowStart = now;
            _recentDispatcherErrors = 0;
        }

        if (++_recentDispatcherErrors >= 20)
        {
            Log.Error("Too many UI errors in one minute; shutting down");
            Shutdown(2);
        }
    }

    private void EmergencyRestore()
    {
        if (!_ownsInstance)
        {
            return;
        }

        foreach (var feature in _features.OfType<IEmergencyRestore>())
        {
            try
            {
                feature.EmergencyRestore();
            }
            catch (Exception ex)
            {
                Log.Error("Emergency restore failed", ex);
            }
        }

        TaskbarController.RestoreFromMarker(_settingsDirectory);
    }
}
