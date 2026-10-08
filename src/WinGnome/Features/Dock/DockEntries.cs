using System.Windows.Media;
using WinGnome.Core.Dock;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Dock;

/// <summary>One slot in the dock: an app, the separator, or a built-in action.</summary>
internal abstract class DockEntry(DockAppearance appearance) : ObservableObject
{
    public DockAppearance Appearance { get; } = appearance;

    /// <summary>False for entries that keep their size under the pointer (the separator).</summary>
    public virtual bool Magnifies => true;
}

/// <summary>The thin line between pinned apps and other running apps.</summary>
internal sealed class DockSeparatorEntry(DockAppearance appearance) : DockEntry(appearance)
{
    public override bool Magnifies => false;
}

internal enum DockActionKind
{
    /// <summary>GNOME's "Show Applications" grid button.</summary>
    ShowApplications,
    RecycleBin,
}

/// <summary>A built-in dock button that is not an app.</summary>
internal sealed class DockActionEntry(DockAppearance appearance, DockActionKind kind, string name) : DockEntry(appearance)
{
    private ImageSource? _icon;

    public DockActionKind Kind { get; } = kind;

    public string Name { get; } = name;

    public bool IsShowApplications => Kind == DockActionKind.ShowApplications;

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>Cache key of <see cref="Icon"/> (it depends on the pixel size).</summary>
    public string? IconKey { get; set; }
}

/// <summary>A pinned launcher and/or a running app.</summary>
internal sealed class DockAppEntry(DockAppearance appearance, string key) : DockEntry(appearance)
{
    private const int MaxIndicators = 4;

    private DockApp? _app;
    private string _name = "";
    private ImageSource? _icon;
    private bool _isFocused;
    private bool _isRunning;
    private int[] _indicators = [];

    /// <summary>Stable key across refreshes ("pin:" + launch id, or "run:" + identity), so the view keeps its container.</summary>
    public string Key { get; } = key;

    /// <summary>The latest model. Set by <see cref="Update"/> before the entry is first shown.</summary>
    public DockApp App => _app ?? throw new InvalidOperationException("Dock entry used before its first update.");

    public string Name
    {
        get => _name;
        private set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Initial));
            }
        }
    }

    /// <summary>First letter of the name, shown on a coloured tile when the app has no icon.</summary>
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>What <see cref="Icon"/> was loaded for; it is only reloaded when this changes.</summary>
    public string? IconKey { get; set; }

    public bool IsFocused
    {
        get => _isFocused;
        private set => SetProperty(ref _isFocused, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool IsPinned => App.IsPinned;

    /// <summary>One element per running-indicator dot (one per window, at most four).</summary>
    public int[] Indicators
    {
        get => _indicators;
        private set => SetProperty(ref _indicators, value);
    }

    public void Update(DockApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Name = app.Name;
        IsFocused = app.IsFocused;
        IsRunning = app.IsRunning;

        var dots = Math.Min(app.Windows.Count, MaxIndicators);
        if (dots != _indicators.Length)
        {
            Indicators = Enumerable.Range(0, dots).ToArray();
        }
    }
}
