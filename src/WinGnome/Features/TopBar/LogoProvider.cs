using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;
using WinGnome.Features.TopBar.Controls;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar;

/// <summary>
/// Resolves the logo setting to a concrete mark and, for a custom image, decodes it once per path change (spec 0021).
/// Owned by <see cref="TopBarServices"/> and shared by every bar, so however many monitors there are the file is read
/// and masked once. The decode runs off the dispatcher under a generation counter (the AppearancePanelViewModel
/// wallpaper pattern): a newer selection or <see cref="Dispose"/> bumps the generation and discards any in-flight
/// decode, so no callback can publish a stale mask or touch a disposed provider. Until a mask lands the bar keeps the
/// Windows mark (first apply) or its previous logo (later changes) — never a blank box.
/// </summary>
internal sealed class LogoProvider : IDisposable
{
    private static readonly LogoTarget WindowsMark = new(LogoKind.WindowsMark, null, null);

    private readonly Dispatcher _dispatcher;
    private int _generation;
    private string? _decodedPath;
    private string? _warnedPath;
    private bool _disposed;

    public LogoProvider(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Target = WindowsMark;
    }

    /// <summary>The mark the bars should draw now. Kind is never <see cref="LogoKind.Image"/> unless <see cref="Mask"/> is ready.</summary>
    public LogoTarget Target { get; private set; }

    /// <summary>The frozen Gray8 mask for a custom image, or null for a built-in mark (or before the decode lands).</summary>
    public BitmapSource? Mask { get; private set; }

    /// <summary>Raised on the dispatcher when <see cref="Target"/> or <see cref="Mask"/> changed.</summary>
    public event EventHandler? Changed;

    public void ApplySettings(TopBarSettings settings)
    {
        var target = LogoSelection.Resolve(settings.Logo, settings.LogoImagePath, File.Exists);
        if (target.Kind != LogoKind.Image)
        {
            // A built-in mark needs no bitmap; cancel any in-flight decode and drop the cached mask.
            _generation++;
            _decodedPath = null;
            Mask = null;

            // A Custom selection whose file is missing (or whose path is set but unreadable) falls back here rather
            // than through the decode below, so warn once per path change — but not for a Custom with no path yet
            // (a pending choice Normalize keeps) or for a built-in mark the user picked directly.
            if (settings.Logo == TopBarLogo.Custom && !string.IsNullOrWhiteSpace(settings.LogoImagePath))
            {
                WarnFallbackOnce(settings.LogoImagePath);
            }
            else
            {
                _warnedPath = null;
            }

            if (Target != target)
            {
                Target = target;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var path = target.Path!;
        if (path == _decodedPath)
        {
            // Already decoded (or already failed) for this exact path: show the image if we have a mask, otherwise
            // keep the Windows-mark fallback set when the decode failed. Never re-decode or re-warn for the same path.
            if (Mask is not null && Target != target)
            {
                Target = target;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // A new path: decode off the dispatcher. Keep the current logo until the mask lands (no flicker to blank).
        var generation = ++_generation;
        Task.Run(() =>
        {
            var decoded = LogoMask.TryDecode(path, out var mask) ? mask : null;
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || generation != _generation)
                {
                    return;
                }

                _decodedPath = path;
                if (decoded is not null)
                {
                    Mask = decoded;
                    Target = new LogoTarget(LogoKind.Image, null, path);
                    _warnedPath = null;
                    Log.Info($"LogoProvider: decoded {path} → {decoded.PixelWidth}×{decoded.PixelHeight} Gray8");
                }
                else
                {
                    // The file exists but couldn't be decoded (oversized, corrupt, not an image): fall back once.
                    Mask = null;
                    Target = WindowsMark;
                    WarnFallbackOnce(path);
                }

                Changed?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    /// <summary>Logs the custom-logo fallback once per distinct path (never a dialog, never per bar, never per frame).</summary>
    private void WarnFallbackOnce(string path)
    {
        if (_warnedPath == path)
        {
            return;
        }

        _warnedPath = path;
        Log.Warn($"LogoProvider: could not load the custom logo '{path}'; showing the Windows mark");
    }

    public void Dispose()
    {
        // Bump the generation so an in-flight decode's callback finds it stale and returns without touching anything.
        _disposed = true;
        _generation++;
    }
}
