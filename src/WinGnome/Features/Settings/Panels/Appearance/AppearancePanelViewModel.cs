using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Appearance;

/// <summary>An accent colour swatch; <see cref="Color"/> is null for "Automatic" (Windows picks it from the wallpaper).</summary>
internal sealed record AccentChoice(string Name, HexColor? Color)
{
    public Brush Brush { get; } = Color is { } c
        ? Frozen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)))
        : Frozen(new LinearGradientBrush(Colors.SteelBlue, Colors.Orange, 45));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Appearance: GNOME's Default (light) or Dark style, accent colour and wallpaper, applied to Windows. When the matching
/// Streamline tweak is on it owns the value (and its backup), so the panel shows it read-only and points there.
/// </summary>
internal sealed class AppearancePanelViewModel : SystemPanelViewModel
{
    private const string DarkModeTweak = "dark-mode";
    private const string AccentTweak = "gnome-accent";
    private const string DefaultStyle = "Default";
    private const string DarkStyle = "Dark";

    private readonly SystemSettingWriter _writer;
    private readonly RegistryStore _registry = new();
    private bool _isDark;
    private AccentChoice? _accent;
    private string? _wallpaperPath;
    private BitmapSource? _wallpaperPreview;
    private int _generation;

    public AppearancePanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Appearance)
    {
        _writer = context.CreateWriter();

        ChooseWallpaperCommand = new RelayCommand(p => SetWallpaper((p as WallpaperChoice)?.Path));
        AddPictureCommand = new RelayCommand(AddPicture);
    }

    /// <summary>GNOME's accent colours, then Windows' automatic accent.</summary>
    public IReadOnlyList<AccentChoice> Accents { get; } =
    [
        new("Blue", HexColor.Parse("#3584E4")),
        new("Teal", HexColor.Parse("#2190A4")),
        new("Green", HexColor.Parse("#3A944A")),
        new("Yellow", HexColor.Parse("#C88800")),
        new("Orange", HexColor.Parse("#ED5B00")),
        new("Red", HexColor.Parse("#E62D42")),
        new("Pink", HexColor.Parse("#D56199")),
        new("Purple", HexColor.Parse("#9141AC")),
        new("Slate", HexColor.Parse("#6F8396")),
        new("Automatic (from wallpaper)", null),
    ];

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (!CanChangeStyle || !SetProperty(ref _isDark, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedStyle));
            _writer.Run($"switch Windows to the {(value ? "dark" : "light")} style",
                () => AppearanceService.WriteIsDark(_registry, value),
                () => ReportWriteFailure("the style"));
        }
    }

    /// <summary>GNOME's two styles.</summary>
    public IReadOnlyList<string> Styles { get; } = [DefaultStyle, DarkStyle];

    public string SelectedStyle
    {
        get => _isDark ? DarkStyle : DefaultStyle;
        set
        {
            if (value is not null)
            {
                IsDark = value == DarkStyle;
            }
        }
    }

    /// <summary>The selected accent swatch; null when Windows uses a colour that isn't one of GNOME's.</summary>
    public AccentChoice? Accent
    {
        get => _accent;
        set
        {
            if (value is null || !CanChangeAccent || !SetProperty(ref _accent, value))
            {
                return;
            }

            _writer.Run($"set the accent colour to {value.Name}",
                () => AppearanceService.WriteAccent(_registry, value.Color),
                () => ReportWriteFailure("the accent colour"));
        }
    }

    public bool CanChangeStyle => CanEdit && !TweakIsOn(DarkModeTweak);

    public bool CanChangeAccent => CanEdit && !TweakIsOn(AccentTweak);

    /// <summary>Why the style is locked, or null.</summary>
    public string? StyleNote => TweakIsOn(DarkModeTweak) ? "The Dark mode tweak on the Streamline page sets this; turn it off there to choose here." : null;

    /// <summary>Why the accent is locked, or null.</summary>
    public string? AccentNote => TweakIsOn(AccentTweak) ? "The Adwaita blue accent tweak on the Streamline page sets this; turn it off there to choose here." : null;

    public ObservableCollection<WallpaperChoice> Wallpapers { get; } = [];

    public string? WallpaperPath
    {
        get => _wallpaperPath;
        private set
        {
            if (SetProperty(ref _wallpaperPath, value))
            {
                OnPropertyChanged(nameof(WallpaperName));
            }
        }
    }

    public string WallpaperName => _wallpaperPath is null ? "None (solid colour or slideshow)" : Path.GetFileName(_wallpaperPath);

    public BitmapSource? WallpaperPreview
    {
        get => _wallpaperPreview;
        private set => SetProperty(ref _wallpaperPreview, value);
    }


    public ICommand ChooseWallpaperCommand { get; }

    public ICommand AddPictureCommand { get; }

    protected override void Open()
    {
        try
        {
            _isDark = AppearanceService.ReadIsDark(_registry);
            var accent = AppearanceService.ReadAccent(_registry);
            _accent = Accents.FirstOrDefault(a => a.Color == accent);
        }
        catch (RegistryAccessException ex)
        {
            Log.Warn("Could not read the Windows colours", ex);
            Problem = "WinGnome couldn't read the Windows colour settings.";
        }

        OnPropertyChanged(string.Empty);
        LoadWallpapers();
    }

    protected override void Close()
    {
        // Drop the decoded previews; a slow load that finishes later is ignored.
        _generation++;
        Wallpapers.Clear();
        WallpaperPreview = null;
    }


    private bool TweakIsOn(string id) => Settings.Current.EnabledTweaks.Contains(id, StringComparer.OrdinalIgnoreCase);

    private void LoadWallpapers()
    {
        var generation = ++_generation;
        Task.Run(() =>
        {
            var current = AppearanceService.ReadWallpaper();
            var preview = current is not null && File.Exists(current) ? AppearanceService.Thumbnail(current) : null;
            var bundled = AppearanceService.ListBundledWallpapers();
            Context.Dispatcher.BeginInvoke(() =>
            {
                if (generation != _generation)
                {
                    return;
                }

                WallpaperPath = string.IsNullOrEmpty(current) ? null : current;
                WallpaperPreview = preview;
                Wallpapers.Clear();
                foreach (var choice in bundled)
                {
                    Wallpapers.Add(choice);
                }
            });
        });
    }

    private void AddPicture()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a wallpaper",
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() == true)
        {
            SetWallpaper(dialog.FileName);
        }
    }

    private void SetWallpaper(string? path)
    {
        if (path is null || !CanEdit)
        {
            return;
        }

        WallpaperPath = path;
        var generation = _generation;
        _writer.Run($"set the wallpaper to {path}", () =>
        {
            var applied = AppearanceService.WriteWallpaper(path);
            var preview = AppearanceService.Thumbnail(path);
            Context.Dispatcher.BeginInvoke(() =>
            {
                if (generation == _generation)
                {
                    WallpaperPreview = preview;
                }
            });
            return applied;
        }, () => ReportWriteFailure("the wallpaper"));
    }
}
