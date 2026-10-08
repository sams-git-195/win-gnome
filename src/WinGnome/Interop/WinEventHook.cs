using System.Runtime.InteropServices;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>
/// Out-of-context SetWinEventHook wrapper. Callbacks arrive on the thread that created the hook
/// (the WPF UI thread), filtered to top-level window objects (OBJID_WINDOW, CHILDID_SELF).
/// </summary>
internal sealed partial class WinEventHook : IDisposable
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    // Delegates are not supported by LibraryImport.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    // Held in a field so the GC never collects the delegate while native code references it.
    private readonly WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private readonly Action<uint, nint> _handler;

    /// <summary>Installs hooks for each inclusive range in <paramref name="ranges"/>.</summary>
    public WinEventHook(Action<uint, nint> handler, bool skipOwnProcess, params (uint Min, uint Max)[] ranges)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _callback = OnEvent;
        var flags = WINEVENT_OUTOFCONTEXT | (skipOwnProcess ? WINEVENT_SKIPOWNPROCESS : 0);
        foreach (var (min, max) in ranges)
        {
            var hook = SetWinEventHook(min, max, 0, _callback, 0, 0, flags);
            if (hook == 0)
            {
                Log.Warn($"SetWinEventHook failed for 0x{min:X}-0x{max:X}");
                continue;
            }

            _hooks.Add(hook);
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Events already queued when Dispose ran can still be delivered; owners have torn down by then.
        if (_hooks.Count == 0 || hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        try
        {
            _handler(eventType, hwnd);
        }
        catch (Exception ex)
        {
            // An exception escaping into user32 would tear down the process.
            Log.Error($"WinEvent handler failed for event 0x{eventType:X}", ex);
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }
}
