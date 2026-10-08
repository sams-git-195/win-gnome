using System.Windows.Threading;
using WinGnome.Features.TopBar.Services;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>The workspace dots: one per virtual desktop, the current one drawn as a wider pill.</summary>
internal sealed class WorkspacesViewModel : ObservableObject, IDisposable
{
    // Touchpads and free-spinning wheels send many wheel events per gesture; one switch per gesture is plenty.
    private static readonly TimeSpan ScrollCooldown = TimeSpan.FromMilliseconds(300);

    private readonly VirtualDesktopMonitor _monitor;
    private DateTime _lastScroll = DateTime.MinValue;

    public WorkspacesViewModel(Dispatcher dispatcher)
    {
        _monitor = new VirtualDesktopMonitor(dispatcher);
        _monitor.Changed += OnMonitorChanged;
    }

    public int Count => _monitor.State.Count;

    public int CurrentIndex => _monitor.State.CurrentIndex;

    public void SwitchTo(int index) => _monitor.SwitchTo(index);

    /// <summary>Wheel up goes to the previous desktop, wheel down to the next, as in GNOME.</summary>
    public void Scroll(int wheelDelta)
    {
        var now = DateTime.UtcNow;
        if (wheelDelta == 0 || now - _lastScroll < ScrollCooldown)
        {
            return;
        }

        _lastScroll = now;
        var target = CurrentIndex + (wheelDelta > 0 ? -1 : 1);
        if (target >= 0 && target < Count)
        {
            SwitchTo(target);
        }
    }

    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CurrentIndex));
    }

    public void Dispose()
    {
        _monitor.Changed -= OnMonitorChanged;
        _monitor.Dispose();
    }
}
