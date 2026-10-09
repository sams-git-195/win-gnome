using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// A switch whose shown value is only ever what Windows reported after the last write (verified-set): turning it asks
/// for a write, the switch is busy until that write has finished, and the value then shown is the one read back.
/// </summary>
internal sealed class VerifiedSwitch(Action<VerifiedSwitch, bool> requestChange) : ObservableObject
{
    private bool _isOn = true;
    private bool _canChange = true;
    private int _pending;

    /// <summary>The value Windows reported. Setting it asks for a change; the shown value follows the read-back.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (value != _isOn && _pending == 0 && _canChange)
            {
                requestChange(this, value);
            }
        }
    }

    /// <summary>False while a write is in flight.</summary>
    public bool IsIdle => _pending == 0;

    /// <summary>False when Windows doesn't let this switch change (for example a device-wide setting is off). Shown disabled.</summary>
    public bool CanChange
    {
        get => _canChange;
        set
        {
            if (SetProperty(ref _canChange, value))
            {
                OnPropertyChanged(nameof(IsInteractive));
            }
        }
    }

    /// <summary>True when the switch can be used: no write in flight and changing it is allowed.</summary>
    public bool IsInteractive => _pending == 0 && _canChange;

    public void Begin()
    {
        _pending++;
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsInteractive));
    }

    public void End()
    {
        _pending = Math.Max(0, _pending - 1);
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsInteractive));
    }

    /// <summary>Shows the value Windows reported. Always raises a change, so a click Windows undid snaps back.</summary>
    public void Confirm(bool on)
    {
        _isOn = on;
        OnPropertyChanged(nameof(IsOn));
    }
}
