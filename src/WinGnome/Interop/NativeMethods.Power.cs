using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Power plan settings for the Power panel (powrprof.dll).</summary>
internal static partial class NativeMethods
{
    public static readonly Guid GUID_VIDEO_SUBGROUP = new("7516B95F-F776-4464-8C53-06167F40CC99");
    public static readonly Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");
    public static readonly Guid GUID_SLEEP_SUBGROUP = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    public static readonly Guid GUID_STANDBY_TIMEOUT = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");

    /// <summary>Returns the active power plan's GUID through a LocalAlloc'd pointer the caller frees with LocalFree.</summary>
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSetActiveScheme(nint userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadACValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, out uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDCValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, out uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerWriteACValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerWriteDCValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, uint value);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint memory);

    // ---- Power mode (UNDOCUMENTED, see KI-061) ----------------------------------------------
    // PowerGetEffectiveOverlayScheme / PowerSetActiveOverlayScheme are what Windows Settings uses for the power mode.
    // They are resolved at run time so a Windows without them degrades instead of failing to bind.

    private static readonly Lazy<nint[]?> OverlayExports = new(LoadOverlayExports);

    /// <summary>The effective power mode overlay; returns a Win32 error code, or null when the export is missing.</summary>
    public static unsafe uint? PowerGetEffectiveOverlayScheme(out Guid overlay)
    {
        overlay = Guid.Empty;
        if (OverlayExports.Value is not { } exports)
        {
            return null;
        }

        Guid value;
        var error = ((delegate* unmanaged[Stdcall]<Guid*, uint>)exports[0])(&value);
        overlay = value;
        return error;
    }

    /// <summary>Sets the power mode overlay; returns a Win32 error code, or null when the export is missing.</summary>
    public static unsafe uint? PowerSetActiveOverlayScheme(Guid overlay) =>
        OverlayExports.Value is { } exports ? ((delegate* unmanaged[Stdcall]<Guid, uint>)exports[1])(overlay) : null;

    private static nint[]? LoadOverlayExports() =>
        NativeLibrary.TryLoad("powrprof.dll", typeof(NativeMethods).Assembly, DllImportSearchPath.System32, out var library)
        && NativeLibrary.TryGetExport(library, "PowerGetEffectiveOverlayScheme", out var get)
        && NativeLibrary.TryGetExport(library, "PowerSetActiveOverlayScheme", out var set)
            ? [get, set]
            : null;
}
