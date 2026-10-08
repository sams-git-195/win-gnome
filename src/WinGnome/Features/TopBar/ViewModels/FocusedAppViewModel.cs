using System.Windows.Media;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;
using WinGnome.Services.Apps;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>Name and icon of the application that owns the foreground window (GNOME's app menu label).</summary>
internal sealed class FocusedAppViewModel : ObservableObject, IDisposable
{
    private readonly WindowTracker _windows;
    private readonly IAppCatalog _apps;
    private readonly IIconProvider _icons;
    private nint _window;
    private string _name = "";
    private ImageSource? _icon;
    private int _iconSizePx = 16;

    public FocusedAppViewModel(WindowTracker windows, IAppCatalog apps, IIconProvider icons)
    {
        _windows = windows;
        _apps = apps;
        _icons = icons;
        _windows.ForegroundChanged += OnForegroundChanged;
        _apps.Changed += OnCatalogChanged;
        Update(_windows.Foreground);
    }

    public string Name
    {
        get => _name;
        private set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(HasApp));
            }
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public bool HasApp => Name.Length > 0;

    /// <summary>Icon size in physical pixels; changes with the bar's font size and the monitor DPI.</summary>
    public int IconSizePx
    {
        get => _iconSizePx;
        set
        {
            if (SetProperty(ref _iconSizePx, Math.Max(8, value)))
            {
                Update(_window);
            }
        }
    }

    private void OnForegroundChanged(object? sender, nint hwnd) => Update(hwnd);

    // The catalogue loads asynchronously after start-up and gives better names than file descriptions.
    private void OnCatalogChanged(object? sender, EventArgs e) => Update(_window);

    private void Update(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        // GNOME shows no app while the desktop is focused.
        if (NativeMethods.GetClassName(hwnd) is "Progman" or "WorkerW")
        {
            _window = 0;
            Name = "";
            Icon = null;
            return;
        }

        // WinGnome's own windows (Inspect returns null), the taskbar, Start, and an app's owned dialogs are not
        // task-switcher windows: keep showing the app that was focused before them.
        var info = _windows.Inspect(hwnd);
        if (info is null || !WindowFilter.IsTaskSwitcherWindow(info))
        {
            return;
        }

        _window = hwnd;
        Name = _apps.FindForWindow(info.AppUserModelId, info.ProcessPath)?.Name ?? _windows.GetAppName(info);
        Icon = GetIcon(info);
    }

    /// <summary>
    /// Executable and AppsFolder icons come from IconProvider's cache (keyed per app). A UWP frame whose app process
    /// was not found uses its app's AppsFolder icon rather than a fresh, uncached and possibly transient frame icon.
    /// Only windows with neither fall back to the window's own icon, which is per window and may change, so it is
    /// not cached.
    /// </summary>
    private ImageSource? GetIcon(WindowInfo info)
    {
        if (AppIdentity.HostedAppIconId(info.AppUserModelId, info.ProcessPath) is { } appId
            && _icons.GetAppIcon(appId, _iconSizePx) is { } appIcon)
        {
            return appIcon;
        }

        return _icons.GetWindowIcon(info.Handle, info.ProcessPath, _iconSizePx);
    }

    public void Dispose()
    {
        _windows.ForegroundChanged -= OnForegroundChanged;
        _apps.Changed -= OnCatalogChanged;
    }
}
