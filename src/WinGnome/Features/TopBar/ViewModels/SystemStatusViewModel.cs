using System.Globalization;
using System.Windows.Threading;
using WinGnome.Core.TopBar;
using WinGnome.Features.TopBar.Services;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>Network, volume and battery state for the status pill and the quick-settings menu.</summary>
internal sealed class SystemStatusViewModel : ObservableObject, IDisposable
{
    private readonly NetworkMonitor _network;
    private readonly AudioVolumeController _audio;
    private readonly BrightnessController _brightness;
    private readonly BatteryMonitor _battery;
    private bool _showBatteryPercentageSetting;

    public SystemStatusViewModel(Dispatcher dispatcher, bool showBatteryPercentage)
    {
        _showBatteryPercentageSetting = showBatteryPercentage;
        _network = new NetworkMonitor(dispatcher);
        _audio = new AudioVolumeController(dispatcher);
        _brightness = new BrightnessController();
        _battery = new BatteryMonitor(dispatcher);
        _network.Changed += (_, _) => RaiseAll(nameof(NetworkGlyph), nameof(IsWifiConnected), nameof(NetworkText), nameof(NetworkSummary), nameof(ToolTip));
        _audio.Changed += (_, _) => RaiseAll(nameof(IsVolumeAvailable), nameof(VolumeGlyph), nameof(VolumePercent),
            nameof(VolumeText), nameof(IsMuted), nameof(ToolTip));
        _brightness.Changed += (_, _) => RaiseAll(nameof(IsBrightnessAvailable), nameof(BrightnessPercent), nameof(BrightnessText));
        _battery.Changed += (_, _) => RaiseAll(nameof(HasBattery), nameof(BatteryGlyph), nameof(BatteryPercentText),
            nameof(BatterySummary), nameof(ShowBatteryPercentage), nameof(ToolTip));
    }

    // ---- Network ----------------------------------------------------------------------------
    public string NetworkGlyph => Glyphs.ForNetwork(_network.Connection);

    public bool IsWifiConnected => _network.Connection == NetworkConnection.Wireless;

    public string NetworkText => _network.Connection switch
    {
        NetworkConnection.Wireless => "Connected",
        NetworkConnection.Wired => "Using wired network",
        _ => "Not connected",
    };

    /// <summary>Network state for the status tooltip.</summary>
    public string NetworkSummary => _network.Connection switch
    {
        NetworkConnection.Wireless => "Wi-Fi",
        NetworkConnection.Wired => "Wired",
        _ => "Offline",
    };

    // ---- Volume -----------------------------------------------------------------------------
    public bool IsVolumeAvailable => _audio.IsAvailable;

    public bool IsMuted => _audio.IsMuted;

    public string VolumeGlyph => Glyphs.ForVolume(VolumeLevel.IconFor(_audio.Level, _audio.IsMuted));

    /// <summary>0..100, bound two-way to the quick-settings slider.</summary>
    public double VolumePercent
    {
        get => _audio.Level * 100;
        set => _audio.SetLevel(value / 100);
    }

    public string VolumeText => VolumeLevel.ToPercent(_audio.Level).ToString(CultureInfo.InvariantCulture) + "%";

    public void ToggleMute() => _audio.SetMuted(!_audio.IsMuted);

    public void NudgeVolume(int wheelDelta) => _audio.Nudge(wheelDelta);

    // ---- Brightness -------------------------------------------------------------------------
    /// <summary>True when the laptop panel reports brightness control; the quick-settings row is hidden otherwise.</summary>
    public bool IsBrightnessAvailable => _brightness.IsAvailable;

    /// <summary>0..100, bound two-way to the quick-settings slider.</summary>
    public double BrightnessPercent
    {
        get => _brightness.Level;
        set => _brightness.SetLevel(value);
    }

    public string BrightnessText => _brightness.Level.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>Re-reads the panel's brightness; called when the quick-settings card opens.</summary>
    public void RefreshBrightness() => _brightness.Refresh();

    public void NudgeBrightness(int wheelDelta) => _brightness.Nudge(wheelDelta);

    // ---- Battery ----------------------------------------------------------------------------
    public bool HasBattery => _battery.Status.HasBattery;

    public string BatteryGlyph => Glyphs.ForBattery(_battery.Status);

    public string BatteryPercentText => _battery.Status.PercentText;

    public string BatterySummary => _battery.Status.Summary;

    public bool ShowBatteryPercentage => _showBatteryPercentageSetting && HasBattery && BatteryPercentText.Length > 0;

    public void ApplySettings(bool showBatteryPercentage)
    {
        _showBatteryPercentageSetting = showBatteryPercentage;
        OnPropertyChanged(nameof(ShowBatteryPercentage));
    }

    // ---- Summary ----------------------------------------------------------------------------
    /// <summary>Hover text for the status pill.</summary>
    public string ToolTip
    {
        get
        {
            var lines = new List<string> { "Network: " + NetworkSummary };
            if (IsVolumeAvailable)
            {
                lines.Add("Volume: " + (IsMuted ? "muted" : VolumeText));
            }

            if (HasBattery)
            {
                lines.Add("Battery: " + BatterySummary);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    private void RaiseAll(params string[] names)
    {
        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }
    }

    public void Dispose()
    {
        _network.Dispose();
        _audio.Dispose();
        _brightness.Dispose();
        _battery.Dispose();
    }
}
