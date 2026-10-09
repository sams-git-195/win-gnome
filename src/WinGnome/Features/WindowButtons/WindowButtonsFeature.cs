using System.Text.Json;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Theme;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Round macOS-style close/minimise/maximise buttons drawn over other apps' title bars, optionally with the
/// title bars unified to an Adwaita header colour. Runs in safe mode too: it changes nothing that outlives the
/// process except caption colours, which are restored on disable, shutdown and crash, and on the next start
/// if WinGnome was killed.
/// </summary>
[FeatureOrder(40)]
internal sealed class WindowButtonsFeature : IFeature, IEmergencyRestore
{
    private readonly ShellContext _context;
    /// <summary>The session-wide role of decorating windows (one WinGnome instance at a time).</summary>
    private const string RoleName = @"Local\WinGnome-WindowButtons";

    private readonly CaptionColorizer _colorizer;
    private SessionRole? _role;
    private CaptionOverlayManager? _manager;
    private WindowButtonSettings? _settings;
    private string? _styleSignature;
    private bool _disposed;

    public WindowButtonsFeature(ShellContext context)
    {
        _context = context;
        _colorizer = new CaptionColorizer(context.Settings.Directory);
    }

    public string Name => "Window buttons";

    public void Start(AppSettings settings)
    {
        var leftovers = _colorizer.RestoreAfterUncleanExit();
        if (leftovers > 0)
        {
            Log.Info($"Restored {leftovers} title bar colour(s) left by a WinGnome that did not exit cleanly");
        }

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
            ReleaseRole();
            return;
        }

        // Only one instance per session decorates (KI-015): two would draw over each other and, with unified
        // title bars, restore each other's colours. The role arrives later if another instance holds it.
        _role ??= new SessionRole(RoleName, "decorates windows", _context.Dispatcher, OnRoleAcquired);
        if (_role.IsHeld)
        {
            UpdateOverlays(_settings);
        }
    }

    private void OnRoleAcquired()
    {
        if (!_disposed && _settings is { Enabled: true } settings)
        {
            UpdateOverlays(settings);
        }
    }

    private void UpdateOverlays(WindowButtonSettings settings)
    {
        var style = CreateStyle(settings);
        var signature = Signature(style);
        if (_manager is null)
        {
            _manager = new CaptionOverlayManager(_context.Windows, _colorizer, _context.Dispatcher, style);
            _manager.Start();
            _styleSignature = signature;
            Log.Info($"Window buttons decorating {_manager.Count} window(s)");
        }
        else if (signature != _styleSignature)
        {
            // Every settings change (any page, every slider step) arrives here; only window-button or theme
            // changes are worth re-laying-out, re-colouring and re-sampling every decorated window.
            _styleSignature = signature;
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
        ReleaseRole();
    }

    /// <summary>Crash path: puts back every caption colour WinGnome changed (Win32 and file calls only, any thread).</summary>
    public void EmergencyRestore() => _colorizer.EmergencyRestore();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_manager is not null && _settings is not null)
        {
            var style = CreateStyle(_settings);
            _styleSignature = Signature(style);
            _manager.ApplyStyle(style);
        }
    }

    private DecorationStyle CreateStyle(WindowButtonSettings settings) =>
        DecorationStyle.Create(settings, _context.Theme.IsDark, appsAreDark: !ThemeManager.SystemUsesLightTheme());

    /// <summary>Everything a <see cref="DecorationStyle"/> is made of, as comparable text.</summary>
    private static string Signature(DecorationStyle style) =>
        $"{JsonSerializer.Serialize(style.Settings)}|{style.Colors}|{style.UnifiedCaption}|{style.UnifiedText}|{style.PlaceholderCaption}";

    private void ReleaseRole()
    {
        _role?.Dispose();
        _role = null;
    }

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
