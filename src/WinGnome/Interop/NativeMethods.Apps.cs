using System.Runtime.InteropServices;

namespace WinGnome.Interop;

// Shell COM interfaces for the Apps panel's startup list (spec 0020): creating a Startup-folder shortcut and moving one
// to the Recycle Bin. Method order mirrors the vtables in shobjidl_core.h exactly; do not reorder. Each interface stops
// after the last method called. IPersistFile is System.Runtime.InteropServices.ComTypes.IPersistFile.

/// <summary>IShellLinkW, up to <see cref="SetIDList"/>.</summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig]
    int GetPath(nint file, int fileLength, nint findData, uint flags);

    [PreserveSig]
    int GetIDList(out nint idList);

    /// <summary>Points the shortcut at an item by ID list (an AppsFolder item works for desktop and packaged apps alike).</summary>
    [PreserveSig]
    int SetIDList(nint idList);
}

/// <summary>IFileOperation, up to <see cref="PerformOperations"/> and <see cref="GetAnyOperationsAborted"/>.</summary>
[ComImport]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    [PreserveSig]
    int Advise(nint sink, out uint cookie);

    [PreserveSig]
    int Unadvise(uint cookie);

    [PreserveSig]
    int SetOperationFlags(uint flags);

    [PreserveSig]
    int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

    [PreserveSig]
    int SetProgressDialog(nint dialog);

    [PreserveSig]
    int SetProperties(nint properties);

    [PreserveSig]
    int SetOwnerWindow(nint owner);

    [PreserveSig]
    int ApplyPropertiesToItem(nint item);

    [PreserveSig]
    int ApplyPropertiesToItems(nint items);

    [PreserveSig]
    int RenameItem(nint item, nint newName, nint sink);

    [PreserveSig]
    int RenameItems(nint items, nint newName);

    [PreserveSig]
    int MoveItem(nint item, nint destination, nint newName, nint sink);

    [PreserveSig]
    int MoveItems(nint items, nint destination);

    [PreserveSig]
    int CopyItem(nint item, nint destination, nint copyName, nint sink);

    [PreserveSig]
    int CopyItems(nint items, nint destination);

    [PreserveSig]
    int DeleteItem([MarshalAs(UnmanagedType.Interface)] IShellItem item, nint sink);

    [PreserveSig]
    int DeleteItems(nint items);

    [PreserveSig]
    int NewItem(nint destination, uint attributes, nint name, nint template, nint sink);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

internal static partial class NativeMethods
{
    public static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid CLSID_FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");

    // IFileOperation::SetOperationFlags (FOF_* from shellapi.h, FOFX_* from shobjidl_core.h).
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_ALLOWUNDO = 0x0040;

    /// <summary>Warns before an item is destroyed instead of recycled (overrides <see cref="FOF_NOCONFIRMATION"/> for that case).</summary>
    public const uint FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>Deletes to the Recycle Bin when possible.</summary>
    public const uint FOFX_RECYCLEONDELETE = 0x00080000;
}
