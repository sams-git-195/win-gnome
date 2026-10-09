using System.Runtime.InteropServices;

namespace WinGnome.Interop;

// The Windows Update Agent (WUA) objects are IDispatch-based and documented, so the panel calls them late-bound
// (dynamic) by ProgID; only the search callback needs a declared interface, because BeginSearch takes an object that
// implements it. See Panels/WindowsUpdate/UpdateStatusService.cs.

/// <summary>The ProgIDs of the WUA objects the Windows Update panel reads.</summary>
internal static class WindowsUpdateProgIds
{
    public const string AutoUpdate = "Microsoft.Update.AutoUpdate";
    public const string Session = "Microsoft.Update.Session";
    public const string SystemInfo = "Microsoft.Update.SystemInfo";
}

/// <summary>WUA's <c>ISearchCompletedCallback</c>: called when an asynchronous search finishes, is cancelled or fails.</summary>
[ComImport]
[Guid("88AEE058-D4B0-4725-A2F1-814A67AE964C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCompletedCallback
{
    void Invoke([MarshalAs(UnmanagedType.Interface)] object searchJob, [MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}
