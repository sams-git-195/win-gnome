using System.Collections.ObjectModel;
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

/// <summary>
/// The bar's notification area: mirrors the <see cref="TrayHost"/>'s icon list and delivers pointer events to the
/// icons' owners exactly as Explorer would. Turning it off stops hosting altogether (no thread, window or icons).
/// </summary>
internal sealed class TrayViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _onCloseRequested;
    private readonly DispatcherTimer _hoverTimer;
    private TrayHost? _host;
    private PixelRect _barBounds;
    private TrayIconViewModel? _hovered;
    private PixelRect _hoveredBounds;

    /// <param name="onCloseRequested">Quits WinGnome when the tray host receives a polite close request.</param>
    public TrayViewModel(Dispatcher dispatcher, Action onCloseRequested)
    {
        _dispatcher = dispatcher;
        _onCloseRequested = onCloseRequested;
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(SystemParameters.MouseHoverTime.TotalMilliseconds) };
        _hoverTimer.Tick += OnHoverTimer;
    }

    /// <summary>Icons in the order they were added, including hidden ones (the view collapses those).</summary>
    public ObservableCollection<TrayIconViewModel> Icons { get; } = [];

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
            host = new TrayHost(_dispatcher, (change, imageChanged, image) => OnHostChange(host, change, imageChanged, image), _barBounds, _onCloseRequested);
            _host = host;
        }
        else
        {
            StopHost();
        }

        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>The bar's strip in physical pixels (the host window claims it, as a taskbar's window would).</summary>
    public void SetBarBounds(PixelRect bounds)
    {
        _barBounds = bounds;
        _host?.SetBarBounds(bounds);
    }

    /// <summary>Delivers a pointer event on <paramref name="icon"/>, whose on-screen rectangle is <paramref name="bounds"/>.</summary>
    public void Send(TrayIconViewModel icon, TrayPointerAction action, PixelRect bounds)
    {
        if (_host is null)
        {
            return;
        }

        switch (action)
        {
            case TrayPointerAction.Enter:
                _hovered = icon;
                _hoveredBounds = bounds;
                _hoverTimer.Stop();
                _hoverTimer.Start();
                break;

            case TrayPointerAction.Leave:
                _hoverTimer.Stop();
                _hovered = null;
                if (!icon.PopupOpen)
                {
                    return;
                }

                icon.PopupOpen = false;
                break;

            default:
                _hoverTimer.Stop();
                break;
        }

        Deliver(icon, action, bounds);
    }

    /// <summary>Crash path (any thread, plain Win32 only).</summary>
    public void EmergencyRestore() => _host?.EmergencyRestore();

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hovered is { } icon && Icons.Contains(icon))
        {
            icon.PopupOpen = TrayCallback.Notifications(TrayPointerAction.Hover, icon.State).Count > 0;
            Deliver(icon, TrayPointerAction.Hover, _hoveredBounds);
        }
    }

    private void Deliver(TrayIconViewModel icon, TrayPointerAction action, PixelRect bounds)
    {
        var state = icon.State;
        var notifications = TrayCallback.Notifications(action, state);
        if (state.CallbackMessage == 0 || notifications.Count == 0 || _host is null)
        {
            return;
        }

        _host.SetIconBounds(state.Id, bounds);
        var owner = state.Id.Owner;
        if (TrayCallback.MayTakeForeground(action))
        {
            // We just received the click, so we may pass the right to take the foreground on, as Explorer does. Without
            // it the app's context menu opens behind other windows and never closes on an outside click.
            NativeMethods.AllowSetForegroundWindow((int)NativeMethods.GetProcessId(owner));
        }

        // Version 4 apps get an anchor in wParam: the icon's centre at the bar's bottom edge, so menus they place
        // there open just under the bar, like macOS menu bar extras.
        var anchorX = bounds.Left + (bounds.Width / 2);
        var anchorY = Math.Max(bounds.Bottom, _barBounds.Bottom);
        foreach (var notification in notifications)
        {
            var (wParam, lParam) = TrayCallback.Encode(state.Version, state.Id.Id, notification, anchorX, anchorY);

            // SendNotifyMessage: delivered like a sent message, but never waits for a busy or hung app.
            if (!NativeMethods.SendNotifyMessage(owner, state.CallbackMessage, wParam, lParam) && TrayCallback.MayTakeForeground(action))
            {
                Log.Warn($"Could not deliver a tray click to window 0x{owner:X} (error {Marshal.GetLastPInvokeError()})");
                return;
            }
        }
    }

    private void OnHostChange(TrayHost? host, TrayChange change, bool imageChanged, IconHandle? image)
    {
        if (host is null || !ReferenceEquals(host, _host) || change.Icon is null)
        {
            return;
        }

        switch (change.Kind)
        {
            case TrayChangeKind.Added:
                var added = new TrayIconViewModel(change.Icon);
                if (imageChanged)
                {
                    added.Image = ToImage(image);
                }

                Icons.Insert(Math.Min(change.Index, Icons.Count), added);
                break;

            case TrayChangeKind.Updated when change.Index < Icons.Count:
                var icon = Icons[change.Index];
                icon.State = change.Icon;
                if (imageChanged)
                {
                    icon.Image = ToImage(image);
                }

                break;

            case TrayChangeKind.Removed when change.Index < Icons.Count:
                if (ReferenceEquals(Icons[change.Index], _hovered))
                {
                    _hoverTimer.Stop();
                    _hovered = null;
                }

                Icons.RemoveAt(change.Index);
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
        Icons.Clear();
    }

    public void Dispose()
    {
        _hoverTimer.Tick -= OnHoverTimer;
        StopHost();
    }
}
