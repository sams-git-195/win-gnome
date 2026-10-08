using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Overview;

/// <summary>
/// GNOME "Activities": the overview window and everything that opens it: <see cref="ShellCommands"/>
/// requests (top bar, dock), the hot corner, the global hotkey and the Super key.
/// </summary>
/// <remarks>
/// The low-level keyboard hook is only installed when a Super-key option needs it, and never in safe mode.
/// </remarks>
[FeatureOrder(50)]
internal sealed class OverviewFeature : IFeature
{
    private readonly ShellContext _context;
    private AppTileCatalog? _apps;
    private OverviewWindow? _window;
    private HotCornerWatcher? _hotCorner;
    private GlobalHotkey? _hotkey;
    private SuperKeyHook? _superKeys;
    private ActivitiesSettings _settings = new();

    public OverviewFeature(ShellContext context)
    {
        _context = context;
    }

    public string Name => "Activities overview";

    public void Start(AppSettings settings)
    {
        _apps = new AppTileCatalog(_context.Apps, _context.Icons, _context.Dispatcher);
        _window = new OverviewWindow(_context, _apps);

        _hotCorner = new HotCornerWatcher(_context.Dispatcher);
        _hotCorner.Triggered += (_, _) => Toggle();

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => Toggle();

        _context.Commands.OverviewRequested += OnOverviewRequested;
        _context.Commands.OverviewHideRequested += OnOverviewHideRequested;

        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Activities;
        _hotCorner?.Configure(_settings.HotCorner, _settings.HotCornerDelayMs);
        _hotkey?.Register(_settings.Hotkey);
        ApplySuperKeySettings();
    }

    private void ApplySuperKeySettings()
    {
        var wanted = _settings.SuperKeyOpensOverview || _settings.SuperNumberActivatesDock;
        if (_context.IsSafeMode || !wanted)
        {
            if (_superKeys is not null)
            {
                _superKeys.Dispose();
                _superKeys = null;
            }

            return;
        }

        if (_superKeys is null)
        {
            _superKeys = SuperKeyHook.TryStart(_context.Dispatcher);
            if (_superKeys is null)
            {
                return;
            }

            _superKeys.OverviewToggleRequested += (_, _) => Toggle();
            _superKeys.DockItemRequested += (_, index) => _context.Commands.ActivateDockItem(index);
            Log.Info("Super-key shortcuts enabled");
        }

        _superKeys.Configure(_settings.SuperKeyOpensOverview, _settings.SuperNumberActivatesDock);
    }

    /// <summary>Hot corner, hotkey and Super key close the overview whatever its mode, or open the windows view.</summary>
    private void Toggle()
    {
        if (_window?.IsOpen == true)
        {
            _window.Dismiss(restoreFocus: true);
        }
        else
        {
            _context.Commands.ShowOverview(OverviewMode.Windows);
        }
    }

    private void OnOverviewRequested(object? sender, OverviewRequest request)
    {
        if (_window is null)
        {
            return;
        }

        if (_window.IsShowing(request))
        {
            _window.Dismiss(restoreFocus: true);
        }
        else
        {
            _window.Open(request, _settings);
        }
    }

    private void OnOverviewHideRequested(object? sender, EventArgs e) => _window?.Dismiss(restoreFocus: true);

    public void Dispose()
    {
        _context.Commands.OverviewRequested -= OnOverviewRequested;
        _context.Commands.OverviewHideRequested -= OnOverviewHideRequested;
        _superKeys?.Dispose();
        _superKeys = null;
        _hotkey?.Dispose();
        _hotkey = null;
        _hotCorner?.Dispose();
        _hotCorner = null;
        _window?.Destroy();
        _window = null;
        _apps?.Dispose();
        _apps = null;
    }
}
