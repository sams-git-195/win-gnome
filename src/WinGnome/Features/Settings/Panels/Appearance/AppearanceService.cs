using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WinGnome.Core.Theming;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Appearance;

/// <summary>A wallpaper the panel offers, with a small decoded preview.</summary>
internal sealed record WallpaperChoice(string Path, BitmapSource Thumbnail);

/// <summary>
/// Reads and writes Windows' colour scheme, accent colour and wallpaper. These are the user's own choices made on
/// purpose, written the way Windows Settings writes them (HKCU values plus a WM_SETTINGCHANGE broadcast, and
/// IDesktopWallpaper), so they are not tweaks and are not backed up or reverted.
/// </summary>
internal static class AppearanceService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const int ThumbnailWidth = 176;
    private const int MaxWallpapers = 24;

    private static readonly string[] PictureExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>True when apps use the dark scheme. Windows' default (no value) is light.</summary>
    public static bool ReadIsDark(IRegistryStore registry) =>
        registry.GetValue(PersonalizeKey, "AppsUseLightTheme") is { Kind: RegistryValueKind.DWord, Data: 0 };

    /// <summary>The fixed accent colour, or null when Windows picks it from the wallpaper (or none is recorded).</summary>
    public static HexColor? ReadAccent(IRegistryStore registry)
    {
        if (registry.GetValue(DesktopKey, "AutoColorization") is { Kind: RegistryValueKind.DWord, Data: 1 })
        {
            return null;
        }

        return registry.GetValue(DwmKey, "AccentColor") is { Kind: RegistryValueKind.DWord, Data: int abgr }
            ? AccentColorChanges.FromAbgr(abgr)
            : null;
    }

    /// <summary>Switches apps and the shell to the dark or light scheme and tells running apps.</summary>
    public static bool WriteIsDark(IRegistryStore registry, bool dark)
    {
        var light = RegistryValue.DWord(dark ? 0 : 1);
        registry.SetValue(PersonalizeKey, "AppsUseLightTheme", light);
        registry.SetValue(PersonalizeKey, "SystemUsesLightTheme", light);
        _ = SystemBroadcast.ThemeChangedAsync();
        return true;
    }

    /// <summary>Sets a fixed accent colour, or with null hands the choice back to Windows (from the wallpaper).</summary>
    public static bool WriteAccent(IRegistryStore registry, HexColor? accent)
    {
        foreach (var change in accent is { } color ? AccentColorChanges.For(color) : AccentColorChanges.Automatic())
        {
            registry.SetValue(change.SubKey, change.ValueName, change.Value);
        }

        _ = SystemBroadcast.ThemeChangedAsync();
        return true;
    }

    /// <summary>The wallpaper of the first monitor, or null when there is none or it can't be read.</summary>
    public static string? ReadWallpaper() => WithWallpaper(wallpaper =>
        wallpaper.GetMonitorDevicePathCount() == 0 ? null : wallpaper.GetWallpaper(wallpaper.GetMonitorDevicePathAt(0)));

    /// <summary>Sets <paramref name="path"/> as the wallpaper of every monitor.</summary>
    public static bool WriteWallpaper(string path) => WithWallpaper(wallpaper =>
    {
        wallpaper.SetWallpaper(null, path);
        return true;
    });

    /// <summary>Windows' bundled wallpapers, previewed. Runs on a worker thread: decoding takes a while.</summary>
    public static IReadOnlyList<WallpaperChoice> ListBundledWallpapers()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper");
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => PictureExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(MaxWallpapers)
                .Select(f => Thumbnail(f) is { } thumbnail ? new WallpaperChoice(f, thumbnail) : null)
                .OfType<WallpaperChoice>()
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not list the wallpapers in {folder}", ex);
            return [];
        }
    }

    /// <summary>A small frozen preview of a picture (decoded at thumbnail size), or null when it can't be read.</summary>
    public static BitmapSource? Thumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = ThumbnailWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or UriFormatException or ArgumentException)
        {
            Log.Warn($"Could not preview {path}", ex);
            return null;
        }
    }

    private static T? WithWallpaper<T>(Func<IDesktopWallpaper, T> action)
    {
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperComObject();
            return action(wallpaper);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("IDesktopWallpaper failed", ex);
            return default;
        }
        finally
        {
            if (wallpaper is not null)
            {
                Marshal.ReleaseComObject(wallpaper);
            }
        }
    }
}
