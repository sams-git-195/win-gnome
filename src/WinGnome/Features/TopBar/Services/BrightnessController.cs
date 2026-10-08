using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Brightness of the internal laptop panel through WMI (<c>WmiMonitorBrightness</c> to read,
/// <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c> to write). Nothing runs until <see cref="Refresh"/>
/// is called, which the quick-settings card does each time it opens; there is no polling.
/// </summary>
/// <remarks>
/// The WMI details live in <see cref="WmiBrightnessPanel"/>. Every WMI call runs on a thread-pool thread; the
/// members of this class are called on the dispatcher thread, whose synchronisation context brings each
/// result back there. Writes are coalesced by a
/// <see cref="WriteCoalescer"/> so dragging the slider issues one call at a time, always ending on the latest
/// value. Any WMI failure is logged and hides the row until the next refresh.
/// </remarks>
internal sealed class BrightnessController : IDisposable
{
    private WriteCoalescer _writes = new();
    private IReadOnlyList<int> _levels = [];
    private WmiBrightnessPanel? _panel;
    private bool _refreshing;
    private bool _unsupported;
    private bool _disposed;

    /// <summary>True once a panel with brightness control has been read.</summary>
    public bool IsAvailable => _levels.Count > 0;

    /// <summary>Current brightness in percent (0..100).</summary>
    public int Level { get; private set; }

    /// <summary>Raised on the UI thread when availability or the level changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the panel's levels and current brightness in the background.</summary>
    public async void Refresh()
    {
        // A drag in progress owns the level; a desktop without a controllable panel is not asked again.
        if (_disposed || _unsupported || _refreshing || _writes.IsBusy)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var panel = await Task.Run(WmiBrightnessPanel.Open);
            ApplyPanel(panel);
        }
        catch (Exception ex)
        {
            // A feature boundary: WMI can fail in many COM-specific ways and none may escape an async void.
            Fail("read the screen brightness", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Sets the brightness (snapped to a supported level) and writes it to the panel in the background.</summary>
    public void SetLevel(double percent)
    {
        if (!IsAvailable)
        {
            return;
        }

        var level = BrightnessScale.Snap(percent, _levels);
        if (level == Level)
        {
            return;
        }

        Level = level;
        Changed?.Invoke(this, EventArgs.Empty);
        if (_writes.Post(level) is { } toWrite)
        {
            WriteLevels(toWrite);
        }
    }

    /// <summary>Applies a mouse-wheel delta (5 % per notch).</summary>
    public void Nudge(int wheelDelta)
    {
        if (IsAvailable)
        {
            SetLevel(BrightnessScale.Nudge(Level, wheelDelta, _levels));
        }
    }

    private async void WriteLevels(int first)
    {
        var panel = _panel!;
        var writes = _writes;
        int? next = first;
        var current = first;
        try
        {
            while (next is { } level)
            {
                current = level;
                await Task.Run(() => panel.SetBrightness(level));
                next = writes.Complete();
            }
        }
        catch (Exception ex)
        {
            // A feature boundary, as in Refresh.
            Fail($"set the screen brightness to {current} %", ex);
        }
    }

    private void ApplyPanel(WmiBrightnessPanel? panel)
    {
        // A write started while we were reading: its level is newer than this reading.
        if (_disposed || _writes.IsBusy)
        {
            panel?.Dispose();
            return;
        }

        if (panel is null)
        {
            Log.Info("No display reports brightness control; the brightness slider is hidden");
            _unsupported = true;
            Clear();
            return;
        }

        _panel?.Dispose();
        _panel = panel;
        _levels = panel.Levels;
        Level = panel.Level;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string what, Exception ex)
    {
        // After Dispose the failure is just the COM object having gone away under an in-flight call.
        if (_disposed)
        {
            return;
        }

        Log.Warn($"Could not {what}; the brightness slider is hidden", ex);
        Clear();
    }

    private void Clear()
    {
        _writes = new WriteCoalescer();
        _panel?.Dispose();
        _panel = null;
        _levels = [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _panel?.Dispose();
        _panel = null;
    }
}
