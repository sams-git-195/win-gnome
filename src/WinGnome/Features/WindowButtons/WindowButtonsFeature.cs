using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Theme;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Round macOS-style close/minimise/maximise buttons drawn over other apps' title bars, optionally with the
/// title bars unified to an Adwaita header colour. Runs in safe mode too: it changes nothing that outlives the
/// process except caption colours, which are restored on disable, shutdown and crash.
/// </summary>
[FeatureOrder(40)]
internal sealed class WindowButtonsFeature : IFeature, IEmergencyRestore
{
    private readonly ShellContext _context;
    private readonly CaptionColorizer _colorizer = new();
    private CaptionOverlayManager? _manager;
    private WindowButtonSettings? _settings;
    private bool _disposed;

    public WindowButtonsFeature(ShellContext context)
    {
        _context = context;
    }

    public string Name => "Window buttons";

    public void Start(AppSettings settings)
    {
        _context.Theme.ThemeChanged += OnThemeChanged;
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_disposed)
        {
            return;
        }

        _settings = settings.WindowButtons;
        if (!_settings.Enabled)
        {
            StopOverlays();
            return;
        }

        var style = CreateStyle(_settings);
        if (_manager is null)
        {
            _manager = new CaptionOverlayManager(_context.Windows, _colorizer, _context.Dispatcher, style);
            _manager.Start();
            Log.Info($"Window buttons decorating {_manager.Count} window(s)");
        }
        else
        {
            _manager.ApplyStyle(style);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.Theme.ThemeChanged -= OnThemeChanged;
        StopOverlays();
    }

    /// <summary>Crash path: puts back every caption colour WinGnome changed (Win32 only, any thread).</summary>
    public void EmergencyRestore() => _colorizer.RestoreAll();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_manager is not null && _settings is not null)
        {
            _manager.ApplyStyle(CreateStyle(_settings));
        }
    }

    private DecorationStyle CreateStyle(WindowButtonSettings settings) =>
        DecorationStyle.Create(settings, _context.Theme.IsDark, appsAreDark: !ThemeManager.SystemUsesLightTheme());

    private void StopOverlays()
    {
        if (_manager is null)
        {
            return;
        }

        _manager.Dispose();
        _manager = null;

        // Every overlay restores its own window; this catches any window whose overlay was lost.
        var restored = _colorizer.RestoreAll();
        Log.Info($"Window buttons stopped{(restored > 0 ? $"; restored {restored} leftover caption colour(s)" : string.Empty)}");
    }
}
