using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Behaviour;

/// <summary>
/// GNOME window-management behaviours from Settings → General: centring new windows and
/// focus-follows-mouse. Focus-follows-mouse changes a system parameter, so it is skipped in safe mode
/// and restored on exit and on crash.
/// </summary>
[FeatureOrder(60)]
internal sealed class WindowBehaviourFeature : IFeature, IEmergencyRestore
{
    private readonly ShellContext _context;
    private readonly FocusFollowsMouse _focusFollowsMouse = new();
    private NewWindowCenterer? _centerer;

    public WindowBehaviourFeature(ShellContext context)
    {
        _context = context;
    }

    public string Name => "Window behaviour";

    public void Start(AppSettings settings)
    {
        if (_context.IsSafeMode && settings.General.FocusFollowsMouse)
        {
            Log.Info("Focus follows mouse is skipped in safe mode");
        }

        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        ApplyCentering(settings.General.CenterNewWindows);
        ApplyFocusFollowsMouse(settings.General.FocusFollowsMouse);
    }

    private void ApplyCentering(bool enabled)
    {
        if (enabled && _centerer is null)
        {
            _centerer = new NewWindowCenterer(_context.Windows, _context.Dispatcher);
            Log.Info("Centring new windows");
        }
        else if (!enabled && _centerer is not null)
        {
            _centerer.Dispose();
            _centerer = null;
        }
    }

    private void ApplyFocusFollowsMouse(bool enabled)
    {
        // Safe mode never touches system parameters.
        if (enabled && !_context.IsSafeMode)
        {
            _focusFollowsMouse.Enable();
        }
        else
        {
            _focusFollowsMouse.Disable();
        }
    }

    public void EmergencyRestore() => _focusFollowsMouse.EmergencyDisable();

    public void Dispose()
    {
        _centerer?.Dispose();
        _centerer = null;
        _focusFollowsMouse.Disable();
    }
}
