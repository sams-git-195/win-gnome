using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;

namespace WinGnome.Theme;

/// <summary>
/// Swaps the Light/Dark palette dictionary in application resources, with Windows' accent colour written into it.
/// Surfaces reference palette brushes through DynamicResource, so a swap restyles every open window immediately.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private static readonly Uri DarkUri = new("pack://application:,,,/WinGnome;component/Theme/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/WinGnome;component/Theme/Light.xaml");

    private ThemeMode _mode = ThemeMode.Dark;
    private ResourceDictionary? _palette;
    private HexColor _accent;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>True when the effective theme is dark.</summary>
    public bool IsDark { get; private set; } = true;

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeMode mode)
    {
        _mode = mode;
        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => !SystemUsesLightTheme(),
        };
        var accent = AccentPalette.FromWindows(ReadWindowsAccent());

        if (_palette is not null && dark == IsDark && accent == _accent)
        {
            return;
        }

        IsDark = dark;
        _accent = accent;
        // By convention (see App.xaml) the palette is always the first merged dictionary.
        // Later dictionaries win lookups, so it must be replaced in place rather than inserted.
        var resources = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        SetAccent(palette, accent);
        if (resources.Count > 0)
        {
            resources[0] = palette;
        }
        else
        {
            resources.Add(palette);
        }

        _palette = palette;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the Windows "apps use light theme" preference.</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read system theme", ex);
            return false;
        }
    }

    /// <summary>Windows' current accent (fixed or from the wallpaper) as DWM records it, or null when absent.</summary>
    private static int? ReadWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            return key?.GetValue("AccentColor") is int abgr ? abgr : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read the accent colour", ex);
            return null;
        }
    }

    // The palette's AccentBrush binds AccentColor with StaticResource when the XAML loads, so both are replaced.
    private static void SetAccent(ResourceDictionary palette, HexColor accent)
    {
        var color = Color.FromRgb(accent.R, accent.G, accent.B);
        var foreground = AccentPalette.ForegroundOn(accent);
        palette["AccentColor"] = color;
        palette["AccentBrush"] = Frozen(color);
        palette["AccentForegroundBrush"] = Frozen(Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Accent changes arrive as WM_SETTINGCHANGE "ImmersiveColorSet" (category General) in every theme mode,
    // so this always re-checks; Apply returns early when neither the scheme nor the accent moved.
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(_mode));
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
