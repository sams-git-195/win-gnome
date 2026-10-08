using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>
/// Blur / acrylic backgrounds for shell surfaces (top bar, dock) via SetWindowCompositionAttribute.
/// </summary>
/// <remarks>
/// The accent API is undocumented, but it has been stable since Windows 10 1803 and works with WPF
/// windows that use <c>AllowsTransparency="True"</c>. The blur fills the whole window rectangle, so
/// a surface with rounded corners or transparent headroom must clip the effect with
/// <see cref="ClipToRoundedRect"/>. Draw the WPF background with a transparent brush (or a very
/// faint one) when blur is on, because the tint is supplied through the accent colour.
/// </remarks>
internal static partial class WindowBlur
{
    private enum AccentState
    {
        Disabled = 0,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;

    [LibraryImport("user32.dll")]
    private static partial int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    /// <summary>
    /// Applies <paramref name="effect"/> to the window, tinted with <paramref name="tint"/> at
    /// <paramref name="opacity"/> (0..1). <see cref="BlurEffect.None"/> removes any accent.
    /// Returns false if the system refused (the caller should then fall back to a plain colour).
    /// </summary>
    public static bool Apply(Window window, BlurEffect effect, HexColor tint, double opacity)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return false;
        }

        var alpha = (uint)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var policy = new AccentPolicy
        {
            AccentState = effect switch
            {
                BlurEffect.Blur => AccentState.BlurBehind,
                BlurEffect.Acrylic => AccentState.AcrylicBlurBehind,
                _ => AccentState.Disabled,
            },
            // Acrylic misrenders with a fully transparent tint on some builds, so keep a minimum alpha.
            GradientColor = (Math.Max(alpha, effect == BlurEffect.Acrylic ? 1u : 0u) << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };
            if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
            {
                Log.Warn($"SetWindowCompositionAttribute({effect}) failed");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Restricts the window's visible and hit-testable area (and therefore the blur) to a rounded
    /// rectangle given in physical pixels relative to the window's top-left corner.
    /// Pass a radius of 0 for square corners.
    /// </summary>
    public static void ClipToRoundedRect(Window window, int left, int top, int right, int bottom, int radiusPx)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        // CreateRoundRectRgn takes the ellipse diameter; regions exclude the right/bottom edge, hence +1.
        var region = radiusPx > 0
            ? CreateRoundRectRgn(left, top, right + 1, bottom + 1, radiusPx * 2, radiusPx * 2)
            : CreateRectRgn(left, top, right, bottom);
        if (region == 0)
        {
            return;
        }

        // On success the system owns the region; only delete it if SetWindowRgn fails.
        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    /// <summary>Removes a clip region set by <see cref="ClipToRoundedRect"/>.</summary>
    public static void ClearClip(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != 0)
        {
            SetWindowRgn(hwnd, 0, true);
        }
    }
}
