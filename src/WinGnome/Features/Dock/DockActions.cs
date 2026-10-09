using System.Windows.Input;
using WinGnome.Core.Dock;
using WinGnome.Core.Settings;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>What dock clicks and menu commands do: launch, focus, minimise, close, pin and unpin.</summary>
internal sealed class DockActions
{
    private readonly ShellContext _context;
    private readonly ExternalForeground _foreground;
    private readonly Action<DockEntry> _launchFeedback;
    private readonly Func<nint> _dockWindow;
    private nint _lastActivated;

    /// <param name="context">Shell services.</param>
    /// <param name="foreground">The user's (non-WinGnome) foreground window.</param>
    /// <param name="launchFeedback">Plays the "launching" animation on an entry.</param>
    /// <param name="dockWindow">The dock's HWND, which owns UAC prompts for elevated launches.</param>
    public DockActions(ShellContext context, ExternalForeground foreground, Action<DockEntry> launchFeedback, Func<nint> dockWindow)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _launchFeedback = launchFeedback ?? throw new ArgumentNullException(nameof(launchFeedback));
        _dockWindow = dockWindow ?? throw new ArgumentNullException(nameof(dockWindow));
    }

    /// <summary>Left click (or Super+N) and middle click on any entry.</summary>
    public void Invoke(DockEntry entry, MouseButton button)
    {
        switch (entry)
        {
            case DockAppEntry app when button == MouseButton.Middle:
                LaunchNew(app);
                break;

            case DockAppEntry app:
                Click(app);
                break;

            case DockActionEntry { Kind: DockActionKind.ShowApplications }:
                _context.Commands.ShowOverview(OverviewMode.Applications);
                break;

            case DockActionEntry { Kind: DockActionKind.RecycleBin } bin:
                OpenRecycleBin(bin);
                break;
        }
    }

    /// <summary>Focuses, minimises, cycles, previews or launches, as <see cref="DockClickPlanner"/> decides.</summary>
    public void Click(DockAppEntry entry)
    {
        var app = entry.App;
        var plan = DockClickPlanner.Plan(app, _foreground.Handle, _context.Settings.Current.Dock.ClickAction, _lastActivated);
        switch (plan.Kind)
        {
            case DockClickKind.Launch:
                LaunchNew(entry);
                break;

            case DockClickKind.Activate:
                ActivateWindow(plan.Window);
                break;

            case DockClickKind.Minimize:
                WindowActivator.Minimize(plan.Window);
                break;

            case DockClickKind.ShowPreviews:
                _context.Commands.ShowOverview(new OverviewRequest(OverviewMode.Windows, app.Windows));
                break;
        }
    }

    /// <summary>
    /// Starts a new instance (pinned launcher, the app's catalogue entry, its AUMID, or its executable), elevated when
    /// the pin says "Always run as administrator".
    /// </summary>
    public void LaunchNew(DockAppEntry entry) =>
        Launch(entry, target => LaunchPlanner.Plan(target, LaunchModifiers.None, PinOf(entry.App), HostOf(target)));

    /// <summary>"Run as administrator": starts a new instance elevated (UAC prompt).</summary>
    public void RunAsAdministrator(DockAppEntry entry) =>
        Launch(entry, target => LaunchPlanner.PlanElevated(target, PinOf(entry.App), HostOf(target)));

    public bool CanLaunch(DockApp app) => LaunchTarget(app) is not null;

    /// <summary>True when the app can be started elevated (desktop and full-trust packaged apps; never UWP apps or URIs).</summary>
    public bool CanRunAsAdministrator(DockApp app) =>
        LaunchTarget(app) is { } target && LaunchPlanner.CanElevate(target, HostOf(target));

    /// <summary>The pin's "Always run as administrator" flag (false for unpinned apps).</summary>
    public bool IsAlwaysRunAsAdministrator(DockApp app) => PinOf(app) is { RunAsAdministrator: true };

    public void SetAlwaysRunAsAdministrator(DockApp app, bool value)
    {
        if (app.LaunchId is { } launchId)
        {
            _context.Settings.Update(s => s.Dock.PinnedApps = DockPins.SetRunAsAdministrator(s.Dock.PinnedApps, launchId, value));
        }
    }

    public void ActivateWindow(nint hwnd)
    {
        WindowActivator.Activate(hwnd);
        _lastActivated = hwnd;
    }

    /// <summary>Politely closes every window of the app (like clicking each close button).</summary>
    public static void Quit(DockApp app)
    {
        foreach (var hwnd in app.Windows)
        {
            WindowActivator.Close(hwnd);
        }
    }

    /// <summary>The launch id and name a running app would be pinned with, or null when it cannot be launched again.</summary>
    public PinnedApp? PinFor(DockApp app)
    {
        if (app.LaunchId is not null)
        {
            return new PinnedApp { Name = app.Name, LaunchId = app.LaunchId };
        }

        var catalogued = _context.Apps.FindForWindow(app.AppUserModelId, app.ProcessPath);
        if (catalogued is not null)
        {
            return new PinnedApp { Name = catalogued.Name, LaunchId = catalogued.LaunchId };
        }

        return string.IsNullOrWhiteSpace(app.ProcessPath) ? null : new PinnedApp { Name = app.Name, LaunchId = app.ProcessPath };
    }

    public void Pin(DockApp app)
    {
        if (PinFor(app) is { } pin)
        {
            _context.Settings.Update(s => s.Dock.PinnedApps = DockPins.Insert(s.Dock.PinnedApps, [pin], s.Dock.PinnedApps.Count));
        }
    }

    public void Unpin(DockApp app)
    {
        if (app.LaunchId is { } launchId)
        {
            _context.Settings.Update(s => s.Dock.PinnedApps = DockPins.Remove(s.Dock.PinnedApps, launchId));
        }
    }

    /// <summary>Pins dropped .exe/.lnk files at <paramref name="index"/> in the pinned list.</summary>
    public void PinFiles(IReadOnlyList<string> paths, int index)
    {
        var pins = paths.Where(DockPins.IsPinnableFile).Select(DockPins.FromFile).ToList();
        if (pins.Count > 0)
        {
            _context.Settings.Update(s => s.Dock.PinnedApps = DockPins.Insert(s.Dock.PinnedApps, pins, index));
        }
    }

    /// <summary>Saves the pinned order left by a drag in the dock.</summary>
    public void ReorderPins(IReadOnlyList<string> launchIds) =>
        _context.Settings.Update(s => s.Dock.PinnedApps = DockPins.Reorder(s.Dock.PinnedApps, launchIds));

    public void OpenRecycleBin(DockActionEntry entry)
    {
        if (_context.Launcher.Launch(DockViewModel.RecycleBinLaunchId))
        {
            _launchFeedback(entry);
        }
    }

    private void Launch(DockAppEntry entry, Func<string, LaunchRequest> plan)
    {
        var app = entry.App;
        var target = LaunchTarget(app);
        if (target is null)
        {
            Log.Warn($"Dock: no way to launch '{app.Name}'");
            return;
        }

        // The feedback runs once the app has started: for an elevated launch, only after UAC is accepted.
        _context.Launcher.Launch(plan(target), () => _launchFeedback(entry), _dockWindow());
    }

    /// <summary>Whether a packaged app is full trust (elevatable) or UWP, from the app catalogue.</summary>
    private AppHost HostOf(string launchId) => _context.Apps.FindByLaunchId(launchId)?.Host ?? AppHost.Unknown;

    /// <summary>The app's pin (arguments, "Always run as administrator"), or null when it is not pinned.</summary>
    private PinnedApp? PinOf(DockApp app) =>
        app.LaunchId is { } launchId ? DockPins.Find(_context.Settings.Current.Dock.PinnedApps, launchId) : null;

    private string? LaunchTarget(DockApp app)
    {
        if (app.LaunchId is not null)
        {
            return app.LaunchId;
        }

        if (_context.Apps.FindForWindow(app.AppUserModelId, app.ProcessPath) is { } catalogued)
        {
            return catalogued.LaunchId;
        }

        // Packaged apps ("Family_hash!App") launch by AUMID; other explicit AUMIDs are not launchable on their own.
        if (app.AppUserModelId is { } aumid && aumid.Contains('!', StringComparison.Ordinal))
        {
            return aumid;
        }

        return string.IsNullOrWhiteSpace(app.ProcessPath) ? null : app.ProcessPath;
    }
}
