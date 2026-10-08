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
    private int _writeCount;
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
        var writesAtStart = _writeCount;
        try
        {
            var panel = await Task.Run(WmiBrightnessPanel.Open);
            ApplyPanel(panel, writesAtStart);
        }
        catch (Exception ex)
        {
            // A feature boundary: WMI can fail in many COM-specific ways and none may escape an async void.
            Log.Warn("Could not read the screen brightness", ex);

            // A failed read says nothing about a panel the user has just written to.
            if (!_disposed && _writeCount == writesAtStart)
            {
                Clear();
            }
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

        var level = BrightnessScale.Resolve(percent, Level, _levels);
        if (level == Level)
        {
            return;
        }

        Level = level;
        _writeCount++;
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
            Log.Warn($"Could not set the screen brightness to {current} %; the brightness slider is hidden", ex);

            // Only hide the row for the panel that failed, never for a newer one.
            if (!_disposed && ReferenceEquals(_panel, panel))
            {
                Clear();
            }
        }
    }

    private void ApplyPanel(WmiBrightnessPanel? panel, int writesAtStart)
    {
        // While a write is running the current panel is in use and the write's level is newer than this reading.
        if (_disposed || _writes.IsBusy)
        {
            DisposeInBackground(panel);
            return;
        }

        if (panel is null)
        {
            Log.Info("No display reports brightness control; the brightness slider is hidden");
            _unsupported = true;
            Clear();
            return;
        }

        DisposeInBackground(_panel);
        _panel = panel;
        _levels = panel.Levels;

        // The user changed the level while this read was running: their value is newer than the one read.
        Level = _writeCount == writesAtStart ? panel.Level : BrightnessScale.Snap(Level, _levels);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        _writes = new WriteCoalescer();
        DisposeInBackground(_panel);
        _panel = null;
        _levels = [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Disposing waits for a WMI call in flight, so it never happens on the UI thread.</summary>
    private static void DisposeInBackground(WmiBrightnessPanel? panel)
    {
        if (panel is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                panel.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not release the WMI brightness connection", ex);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeInBackground(_panel);
        _panel = null;
    }
}
