using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>
/// Blur / acrylic material for a window via SetWindowCompositionAttribute (the accent API).
/// </summary>
/// <remarks>
/// The accent API is undocumented, but it has been stable since Windows 10 1803. The material fills the whole
/// window rectangle and DWM ignores window regions for it, so shell surfaces do not apply it to themselves:
/// they use a <see cref="BlurBackdrop"/>, which is the only caller.
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
        public nint SizeOfData; // SIZE_T
    }

    private const int WCA_ACCENT_POLICY = 19;

    [LibraryImport("user32.dll")]
    private static partial int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Applies an untinted <paramref name="effect"/> to the window (the surface above draws the tint);
    /// <see cref="BlurEffect.None"/> removes any accent. Returns false if the window has no handle yet or
    /// the system refused (the caller should then fall back to a plain colour).
    /// </summary>
    public static bool Apply(Window window, BlurEffect effect)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return false;
        }

        var policy = new AccentPolicy
        {
            AccentState = effect switch
            {
                BlurEffect.Blur => AccentState.BlurBehind,
                BlurEffect.Acrylic => AccentState.AcrylicBlurBehind,
                _ => AccentState.Disabled,
            },

            // Acrylic misrenders with a fully transparent tint on some builds, so keep a minimum alpha (1/255).
            GradientColor = effect == BlurEffect.Acrylic ? 1u << 24 : 0,
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
}
