using System.Windows;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Theme;

/// <summary>
/// Swaps the Light/Dark palette dictionary in application resources. Surfaces reference palette
/// brushes through DynamicResource, so a swap restyles every open window immediately.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private static readonly Uri DarkUri = new("pack://application:,,,/WinGnome;component/Theme/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/WinGnome;component/Theme/Light.xaml");

    private ThemeMode _mode = ThemeMode.Dark;
    private ResourceDictionary? _palette;

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

        if (_palette is not null && dark == IsDark)
        {
            return;
        }

        IsDark = dark;
        // By convention (see App.xaml) the palette is always the first merged dictionary.
        // Later dictionaries win lookups, so it must be replaced in place rather than inserted.
        var resources = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
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

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode == ThemeMode.System && e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(_mode));
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
