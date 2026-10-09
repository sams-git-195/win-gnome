using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// A switch whose shown value is only ever what Windows reported after the last write (verified-set): turning it asks
/// for a write, the switch is busy until that write has finished, and the value then shown is the one read back.
/// </summary>
internal sealed class VerifiedSwitch(Action<VerifiedSwitch, bool> requestChange) : ObservableObject
{
    private bool _isOn = true;
    private int _pending;

    /// <summary>The value Windows reported. Setting it asks for a change; the shown value follows the read-back.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (value != _isOn && _pending == 0)
            {
                requestChange(this, value);
            }
        }
    }

    /// <summary>False while a write is in flight.</summary>
    public bool IsIdle => _pending == 0;

    public void Begin()
    {
        _pending++;
        OnPropertyChanged(nameof(IsIdle));
    }

    public void End()
    {
        _pending = Math.Max(0, _pending - 1);
        OnPropertyChanged(nameof(IsIdle));
    }

    /// <summary>Shows the value Windows reported. Always raises a change, so a click Windows undid snaps back.</summary>
    public void Confirm(bool on)
    {
        _isOn = on;
        OnPropertyChanged(nameof(IsOn));
    }
}
