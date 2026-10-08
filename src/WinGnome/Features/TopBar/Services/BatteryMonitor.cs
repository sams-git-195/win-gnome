using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Polls <c>GetSystemPowerStatus</c>. Windows broadcasts power-status changes (PowerModeChanged) but not
/// percentage changes, so a slow timer keeps the charge level current. Without a battery the timer stops; the
/// broadcast sent when a battery appears (or after resume) reads the status again and restarts it.
/// </summary>
internal sealed class BatteryMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private bool _warned;
    private bool _disposed;

    public BatteryMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = PollInterval };
        _timer.Tick += OnTick;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Refresh();
    }

    public BatteryStatus Status { get; private set; } = BatteryStatus.None;

    /// <summary>Raised on the UI thread when <see cref="Status"/> changes.</summary>
    public event EventHandler? Changed;

    public void Refresh()
    {
        // A power broadcast queued before Dispose must not restart the timer.
        if (_disposed)
        {
            return;
        }

        if (!NativeMethods.GetSystemPowerStatus(out var raw))
        {
            // Keep trying: a failed read says nothing about whether there is a battery.
            _timer.Start();

            // Logged once: this would otherwise repeat every poll.
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"GetSystemPowerStatus failed (error {Marshal.GetLastPInvokeError()})");
            }

            return;
        }

        var status = BatteryStatus.FromPowerStatus(raw.ACLineStatus, raw.BatteryFlag, raw.BatteryLifePercent, raw.BatteryLifeTime);
        _timer.IsEnabled = status.NeedsPolling;
        if (status != Status)
        {
            Status = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => _dispatcher.BeginInvoke(Refresh);

    public void Dispose()
    {
        _disposed = true;
        // SystemEvents is static: an un-removed handler would keep this object (and the bar) alive forever.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
