using System.Windows.Media;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Overview;

/// <summary>Something the overview can highlight with the keyboard and then open with Enter.</summary>
internal abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>An installed application shown in the app grid and in search results. The icon loads lazily.</summary>
internal sealed class AppTile(string name, string launchId) : SelectableItem
{
    private ImageSource? _icon;

    public string Name { get; } = name;

    public string LaunchId { get; } = launchId;

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>Pixel size <see cref="Icon"/> was loaded at, or 0 when it has not been loaded yet.</summary>
    public int IconSizePx { get; set; }
}

/// <summary>An open window listed in search results.</summary>
internal sealed class WindowResult(nint handle, string title, string appName, ImageSource? icon) : SelectableItem
{
    public nint Handle { get; } = handle;

    public string Title { get; } = title;

    public string AppName { get; } = appName;

    public ImageSource? Icon { get; } = icon;
}
