using System.Globalization;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>
/// The centred clock. Instead of a fixed-interval timer it re-arms a one-shot timer for the exact next
/// minute (or second) boundary, so the text flips on time without waking up needlessly.
/// </summary>
internal sealed class ClockViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private TopBarSettings _settings;
    private string _text = "";

    public ClockViewModel(Dispatcher dispatcher, TopBarSettings settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _timer.Tick += (_, _) => Update();
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Update();
    }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;
        Text = ClockFormatter.Format(now, _settings, CultureInfo.CurrentCulture);
        _timer.Stop();
        _timer.Interval = ClockFormatter.NextTickDelay(now, _settings.ShowSeconds);
        _timer.Start();
    }

    private void OnTimeChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        // WM_TIMECHANGE also covers time-zone changes, which DateTime.Now only sees once the cache is cleared.
        TimeZoneInfo.ClearCachedData();
        Update();
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Timers do not fire during sleep; the clock would otherwise show the suspend time until the next tick.
        if (e.Mode == PowerModes.Resume)
        {
            _dispatcher.BeginInvoke(Update);
        }
    }

    public void Dispose()
    {
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Stop();
    }
}
