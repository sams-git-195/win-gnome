using System.Windows.Media;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Services;
using WinGnome.Services.Apps;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>
/// Name and icon of the focused application of one bar's monitor (GNOME's app menu label). The top bar coordinator
/// decides which window that is (see <c>MonitorFocusTracker</c>) and calls <see cref="Show"/>.
/// </summary>
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
        _apps.Changed += OnCatalogChanged;
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

    /// <summary>Shows the app of task-switcher window <paramref name="hwnd"/>, or nothing for 0 (as on the desktop).</summary>
    public void Show(nint hwnd)
    {
        if (hwnd != _window || hwnd == 0)
        {
            Update(hwnd);
        }
    }

    // The catalogue loads asynchronously after start-up and gives better names than file descriptions.
    private void OnCatalogChanged(object? sender, EventArgs e) => Update(_window);

    private void Update(nint hwnd)
    {
        var info = hwnd == 0 ? null : _windows.Inspect(hwnd);
        if (info is null)
        {
            _window = 0;
            Name = "";
            Icon = null;
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
        _apps.Changed -= OnCatalogChanged;
    }
}
