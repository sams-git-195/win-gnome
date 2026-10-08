using System.Runtime.InteropServices;

namespace WinGnome.Interop;

// Classic [ComImport] shell interfaces. Method order mirrors the vtable in shobjidl_core.h exactly;
// do not reorder. Interfaces may stop after the last method we call (trailing slots are never touched).

/// <summary>SIGDN: the form of name returned by <see cref="IShellItem.GetDisplayName"/>.</summary>
internal enum SIGDN : uint
{
    NormalDisplay = 0x00000000,
    ParentRelativeParsing = 0x80018001,
}

/// <summary>SIIGBF flags for <see cref="IShellItemImageFactory.GetImage"/>.</summary>
[Flags]
internal enum SIIGBF
{
    /// <summary>Allow a larger image than requested instead of shrinking it.</summary>
    BiggerSizeOk = 0x01,

    /// <summary>Return the icon only, never a content thumbnail.</summary>
    IconOnly = 0x04,
}

/// <summary>ACTIVATEOPTIONS for <see cref="IApplicationActivationManager.ActivateApplication"/>.</summary>
[Flags]
internal enum ActivateOptions
{
    None = 0x0,

    /// <summary>Do not show an error dialog if activation fails (the caller has a fallback).</summary>
    NoErrorUI = 0x2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;

    public SIZE(int width, int height)
    {
        cx = width;
        cy = height;
    }
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint bindContext, ref Guid handlerId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? handler);

    [PreserveSig]
    int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? parent);

    [PreserveSig]
    int GetDisplayName(SIGDN form, [MarshalAs(UnmanagedType.LPWStr)] out string? name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
}

/// <summary>
/// IShellItem2. Classic COM interop does not lay out inherited interface methods, so the five
/// IShellItem methods are re-declared first to keep the vtable slots aligned.
/// </summary>
[ComImport]
[Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    // ---- IShellItem ----
    [PreserveSig]
    int BindToHandler(nint bindContext, ref Guid handlerId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? handler);

    [PreserveSig]
    int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? parent);

    [PreserveSig]
    int GetDisplayName(SIGDN form, [MarshalAs(UnmanagedType.LPWStr)] out string? name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);

    // ---- IShellItem2 ----
    [PreserveSig]
    int GetPropertyStore(int flags, ref Guid riid, out nint store);

    [PreserveSig]
    int GetPropertyStoreWithCreateObject(int flags, nint createObject, ref Guid riid, out nint store);

    [PreserveSig]
    int GetPropertyStoreForKeys(nint keys, uint keyCount, int flags, ref Guid riid, out nint store);

    [PreserveSig]
    int GetPropertyDescriptionList(ref WindowProperties.PROPERTYKEY keyType, ref Guid riid, out nint list);

    [PreserveSig]
    int Update(nint bindContext);

    [PreserveSig]
    int GetProperty(ref WindowProperties.PROPERTYKEY key, out WindowProperties.PROPVARIANT value);

    [PreserveSig]
    int GetCLSID(ref WindowProperties.PROPERTYKEY key, out Guid clsid);

    [PreserveSig]
    int GetFileTime(ref WindowProperties.PROPERTYKEY key, out long fileTime);

    [PreserveSig]
    int GetInt32(ref WindowProperties.PROPERTYKEY key, out int value);

    [PreserveSig]
    int GetString(ref WindowProperties.PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string? value);

    // GetUInt32, GetUInt64 and GetBool follow; not declared because they are not used.
}

[ComImport]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    /// <summary>Call with <paramref name="count"/> = 1; returns S_OK while items remain, S_FALSE at the end.</summary>
    [PreserveSig]
    int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems? clone);
}

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    /// <summary>Returns a 32bpp DIB section the caller owns and must DeleteObject.</summary>
    [PreserveSig]
    int GetImage(SIZE size, SIIGBF flags, out nint bitmap);
}

[ComImport]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
        ActivateOptions options,
        out uint processId);

    [PreserveSig]
    int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, [MarshalAs(UnmanagedType.LPWStr)] string? verb, out uint processId);

    [PreserveSig]
    int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, out uint processId);
}

/// <summary>Well-known shell GUIDs used with the interfaces above.</summary>
internal static class ShellGuids
{
    /// <summary>FOLDERID_AppsFolder: the virtual "All apps" folder (shell:AppsFolder).</summary>
    public static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    /// <summary>BHID_EnumItems: binds a folder item to an IEnumShellItems over its children.</summary>
    public static readonly Guid BindEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    /// <summary>CLSID_ApplicationActivationManager.</summary>
    public static readonly Guid ApplicationActivationManager = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
}
