using WinGnome.Core.Geometry;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>
/// A live DWM thumbnail of one window, composited by DWM on top of a destination window's content.
/// Disposing unregisters it; an unregistered thumbnail costs nothing and leaves nothing behind.
/// </summary>
internal sealed class DwmThumbnail : IDisposable
{
    private nint _handle;

    private DwmThumbnail(nint handle, nint source)
    {
        _handle = handle;
        Source = source;
    }

    /// <summary>The window being mirrored.</summary>
    public nint Source { get; }

    /// <summary>Registers a thumbnail of <paramref name="source"/> drawn into <paramref name="destination"/>; null on failure.</summary>
    public static DwmThumbnail? TryRegister(nint destination, nint source)
    {
        var hr = NativeMethods.DwmRegisterThumbnail(destination, source, out var handle);
        if (hr < 0 || handle == 0)
        {
            // A window that closed after the window list was taken is expected, not worth a warning.
            if (NativeMethods.IsWindow(source))
            {
                Log.Warn($"DwmRegisterThumbnail failed for 0x{source:X} (hr=0x{hr:X8})");
            }

            return null;
        }

        return new DwmThumbnail(handle, source);
    }

    /// <summary>The source window's current size in physical pixels, or null when DWM has nothing to show.</summary>
    public (int Width, int Height)? QuerySourceSize()
    {
        if (_handle == 0 || NativeMethods.DwmQueryThumbnailSourceSize(_handle, out var size) < 0)
        {
            return null;
        }

        return size.cx > 0 && size.cy > 0 ? (size.cx, size.cy) : null;
    }

    /// <summary>
    /// Shows the whole source window (frame included) in <paramref name="destination"/>, which is in
    /// physical pixels relative to the destination window's client area.
    /// </summary>
    public void Show(PixelRect destination, byte opacity = 255)
    {
        Update(new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE
                | NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = RECT.From(destination),
            opacity = opacity,
            fVisible = 1,
            fSourceClientAreaOnly = 0,
        });
    }

    /// <summary>Stops drawing the thumbnail without unregistering it.</summary>
    public void Hide()
    {
        Update(new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_VISIBLE,
            fVisible = 0,
        });
    }

    private void Update(in DWM_THUMBNAIL_PROPERTIES properties)
    {
        if (_handle == 0)
        {
            return;
        }

        // Fails harmlessly (E_INVALIDARG) once the source window has been destroyed; the next
        // window-list refresh removes the thumbnail, so there is nothing to log here.
        _ = NativeMethods.DwmUpdateThumbnailProperties(_handle, properties);
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            // DWM drops the thumbnail itself when the source window is destroyed (E_INVALIDARG here), which
            // happens routinely when a window closes while the overview shows it: nothing to report then.
            var hr = NativeMethods.DwmUnregisterThumbnail(_handle);
            if (hr < 0 && NativeMethods.IsWindow(Source))
            {
                Log.Warn($"DwmUnregisterThumbnail failed for 0x{Source:X} (hr=0x{hr:X8})");
            }

            _handle = 0;
        }
    }
}
