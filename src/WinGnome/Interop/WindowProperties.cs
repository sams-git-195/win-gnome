using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Reads shell properties of top-level windows, such as the explicit AppUserModelID.</summary>
internal static partial class WindowProperties
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>PKEY_AppUserModel_ID.</summary>
    internal static readonly PROPERTYKEY AppUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        void Commit();
    }

    /// <summary>Just enough of PROPVARIANT to read VT_LPWSTR values.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public nint pointerValue;

        public const ushort VT_LPWSTR = 31;

        public readonly string? AsString() => vt == VT_LPWSTR && pointerValue != 0 ? Marshal.PtrToStringUni(pointerValue) : null;
    }

    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref PROPVARIANT value);

    private static readonly Guid IPropertyStoreGuid = typeof(IPropertyStore).GUID;

#pragma warning disable SYSLIB1054 // COM interface return marshalling requires DllImport.
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
#pragma warning restore SYSLIB1054

    /// <summary>Returns the window's explicit AppUserModelID, or null when none is set.</summary>
    public static string? GetAppUserModelId(nint hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = IPropertyStoreGuid;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store is null)
            {
                return null;
            }

            var key = AppUserModelIdKey;
            if (store.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            try
            {
                var id = value.AsString();
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }
}
