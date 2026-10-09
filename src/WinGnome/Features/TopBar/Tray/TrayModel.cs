using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Tray;

namespace WinGnome.Features.TopBar.Tray;

/// <summary>A change to the order of the icon list (state changes travel on the entries themselves).</summary>
internal readonly record struct TrayStructureChange(TrayChangeKind Kind, int Index, TrayIconEntry? Entry);

/// <summary>
/// The notification area shared by every bar: owns the one <see cref="TrayHost"/>, the ordered icon entries, the
/// hover state and delivery of pointer events to the icons' owners exactly as Explorer would. Each bar mirrors the
/// entries through its own <see cref="TrayBarIcons"/>. Turning it off stops hosting altogether (no thread, window
/// or icons).
/// </summary>
internal sealed class TrayModel : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _onCloseRequested;
    private readonly DispatcherTimer _hoverTimer;
    private readonly List<TrayIconEntry> _entries = [];
    private TrayHost? _host;
    private PixelRect _hostBounds;
    private TrayIconEntry? _hovered;
    private PixelRect _hoveredBounds;
    private PixelRect _hoveredBarBounds;

    /// <param name="onCloseRequested">Quits WinGnome when the tray host receives a polite close request.</param>
    public TrayModel(Dispatcher dispatcher, Action onCloseRequested)
    {
        _dispatcher = dispatcher;
        _onCloseRequested = onCloseRequested;
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(SystemParameters.MouseHoverTime.TotalMilliseconds) };
        _hoverTimer.Tick += OnHoverTimer;
    }

    /// <summary>Raised on the UI thread when an icon is inserted or removed (applied in order, the list mirrors exactly).</summary>
    public event EventHandler<TrayStructureChange>? Changed;

    /// <summary>Raised when hosting starts or stops.</summary>
    public event EventHandler? EnabledChanged;

    /// <summary>Icons in the order they were added, including hidden ones (the views collapse those).</summary>
    public IReadOnlyList<TrayIconEntry> Entries => _entries;

    public bool IsEnabled => _host is not null;

    /// <summary>Starts or stops hosting tray icons (TopBarSettings.ShowTrayIcons).</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
        {
            return;
        }

        if (enabled)
        {
            if (NativeMethods.IsProcessElevated(NativeMethods.GetCurrentProcessId()))
            {
                // An elevated host would sit between every normal app and Explorer, and UIPI would block the private
                // messages those apps send to the taskbar. Leave the tray to Explorer.
                Log.Info("WinGnome runs as administrator; tray icons stay in the Windows taskbar");
                return;
            }

            // Changes are queued on the dispatcher, so a stopped host's last ones can arrive after a new host started:
            // each callback names its host and only the current one's are applied.
            TrayHost? host = null;
            host = new TrayHost(_dispatcher, (change, imageChanged, image) => OnHostChange(host, change, imageChanged, image), _hostBounds, _onCloseRequested);
            _host = host;
        }
        else
        {
            StopHost();
        }

        EnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The primary bar's strip in physical pixels: the hidden host window claims it, as a taskbar's window would.
    /// Bars on other monitors pass their own strip with each pointer event instead.
    /// </summary>
    public void SetHostBounds(PixelRect bounds)
    {
        _hostBounds = bounds;
        _host?.SetBarBounds(bounds);
    }

    /// <summary>
    /// Delivers a pointer event on <paramref name="entry"/>, whose on-screen rectangle is <paramref name="iconBounds"/>,
    /// in the bar whose strip is <paramref name="barBounds"/>.
    /// </summary>
    public void Send(TrayIconEntry entry, TrayPointerAction action, PixelRect iconBounds, PixelRect barBounds)
    {
        if (_host is null)
        {
            if (TrayCallback.MayTakeForeground(action))
            {
                // Callers are the bars' pointer handlers and the hover timer, all on the UI thread, so ThrottledLog is safe.
                ThrottledLog.Info("tray-nohost", $"Tray {action} did nothing: the tray host is not running");
            }

            return;
        }

        switch (action)
        {
            case TrayPointerAction.Enter:
                // Also when the pointer came from another bar: the timer restarts for this icon, as between two icons.
                _hovered = entry;
                _hoveredBounds = iconBounds;
                _hoveredBarBounds = barBounds;
                _hoverTimer.Stop();
                _hoverTimer.Start();
                break;

            case TrayPointerAction.Leave:
                _hoverTimer.Stop();
                _hovered = null;
                if (!entry.PopupOpen)
                {
                    return;
                }

                entry.PopupOpen = false;
                break;

            default:
                _hoverTimer.Stop();
                break;
        }

        Deliver(entry, action, iconBounds, barBounds);
    }

    /// <summary>Crash path (any thread, plain Win32 only).</summary>
    public void EmergencyRestore() => _host?.EmergencyRestore();

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hovered is { } entry && _entries.Contains(entry))
        {
            entry.PopupOpen = TrayCallback.Notifications(TrayPointerAction.Hover, entry.State).Count > 0;
            Deliver(entry, TrayPointerAction.Hover, _hoveredBounds, _hoveredBarBounds);
        }
    }

    private void Deliver(TrayIconEntry entry, TrayPointerAction action, PixelRect iconBounds, PixelRect barBounds)
    {
        var state = entry.State;
        var notifications = TrayCallback.Notifications(action, state);

        // Only clicks are logged: Enter/Hover/Leave fire on every pointer move across the tray and must stay silent.
        var click = TrayCallback.MayTakeForeground(action);
        if (state.CallbackMessage == 0)
        {
            if (click)
            {
                Log.Info($"Tray {action} on {Tip(state)} (owner 0x{state.Id.Owner:X}, id {state.Id.Id}) did nothing: the icon registered no callback message");
            }

            return;
        }

        if (notifications.Count == 0)
        {
            if (click)
            {
                Log.Info($"Tray {action} on {Tip(state)} (owner 0x{state.Id.Owner:X}, id {state.Id.Id}) produced no notifications; nothing sent");
            }

            return;
        }

        if (_host is null)
        {
            return;
        }

        // Shell_NotifyIconGetRect answers with the icon the user actually used, on that bar's monitor.
        _host.SetIconBounds(state.Id, iconBounds);
        var owner = state.Id.Owner;
        if (click)
        {
            // We just received the click, so we may pass the right to take the foreground on, as Explorer does. Without
            // it the app's context menu opens behind other windows and never closes on an outside click.
            NativeMethods.AllowSetForegroundWindow((int)NativeMethods.GetProcessId(owner));
        }

        // Version 4 apps get an anchor in wParam: menus they place there open just under the clicked bar.
        var (anchorX, anchorY) = TrayAnchor.For(iconBounds, barBounds);
        if (click)
        {
            // The recorded version is the diagnosis for icons whose NIM_SETVERSION the host missed: a version-4 icon
            // logged as version 0 gets legacy-encoded callbacks and ignores them.
            var guid = state.Id.ItemGuid == Guid.Empty ? "none" : state.Id.ItemGuid.ToString();
            var sends = string.Join(", ", notifications.Select(notification => $"0x{notification:X}"));
            Log.Info($"Tray {action} on {Tip(state)} (owner 0x{owner:X}, id {state.Id.Id}, guid {guid}, version {state.Version}, callback 0x{state.CallbackMessage:X}): sending [{sends}]; anchor {anchorX},{anchorY}, icon {Rect(iconBounds)}, bar {Rect(barBounds)}");
        }

        foreach (var notification in notifications)
        {
            var (wParam, lParam) = TrayCallback.Encode(state.Version, state.Id.Id, notification, anchorX, anchorY);

            // SendNotifyMessage: delivered like a sent message, but never waits for a busy or hung app.
            if (!NativeMethods.SendNotifyMessage(owner, state.CallbackMessage, wParam, lParam) && click)
            {
                Log.Warn($"Could not deliver a tray click to window 0x{owner:X} (error {Marshal.GetLastPInvokeError()})");
                return;
            }
        }
    }

    /// <summary>The icon's quoted tooltip, capped: it is arbitrary app-supplied text (potential PII) and must not fill the log.</summary>
    private static string Tip(TrayIconState state) =>
        state.Tip.Length == 0 ? "\"(no tooltip)\""
        : state.Tip.Length <= 64 ? $"\"{state.Tip}\""
        : $"\"{state.Tip[..64]}…\"";

    private static string Rect(PixelRect rect) => $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}";

    private void OnHostChange(TrayHost? host, TrayChange change, bool imageChanged, IconHandle? image)
    {
        if (host is null || !ReferenceEquals(host, _host) || change.Icon is null)
        {
            return;
        }

        switch (change.Kind)
        {
            case TrayChangeKind.Added:
                var added = new TrayIconEntry(change.Icon);
                if (imageChanged)
                {
                    added.Image = ToImage(image);
                }

                var index = Math.Min(change.Index, _entries.Count);
                _entries.Insert(index, added);
                Changed?.Invoke(this, new TrayStructureChange(TrayChangeKind.Added, index, added));
                break;

            case TrayChangeKind.Updated when change.Index < _entries.Count:
                var entry = _entries[change.Index];
                entry.State = change.Icon;
                if (imageChanged)
                {
                    entry.Image = ToImage(image);
                }

                break;

            case TrayChangeKind.Removed when change.Index < _entries.Count:
                if (ReferenceEquals(_entries[change.Index], _hovered))
                {
                    _hoverTimer.Stop();
                    _hovered = null;
                }

                var removed = _entries[change.Index];
                _entries.RemoveAt(change.Index);
                Changed?.Invoke(this, new TrayStructureChange(TrayChangeKind.Removed, change.Index, removed));
                break;
        }
    }

    private static BitmapSource? ToImage(IconHandle? icon)
    {
        if (icon is null)
        {
            return null;
        }

        try
        {
            // Empty size options keep the icon's own pixel size at 96 DPI; TrayIconPlacement decides how it is drawn.
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.DangerousGetHandle(), Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            Log.Warn("Could not convert a tray icon", ex);
            return null;
        }
    }

    private void StopHost()
    {
        _hoverTimer.Stop();
        _hovered = null;
        var host = _host;
        _host = null;
        host?.Dispose();

        // Last to first, so each removal's index is valid for the bars mirroring the list.
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var removed = _entries[i];
            _entries.RemoveAt(i);
            Changed?.Invoke(this, new TrayStructureChange(TrayChangeKind.Removed, i, removed));
        }
    }

    public void Dispose()
    {
        _hoverTimer.Tick -= OnHoverTimer;
        StopHost();
    }
}
