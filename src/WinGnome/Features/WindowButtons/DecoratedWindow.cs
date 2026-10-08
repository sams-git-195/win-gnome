using System.ComponentModel;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// The traffic lights of one target window: keeps the button surface (and, for left-side buttons, a mask over
/// the native buttons) positioned over the target's title bar, coloured to match it, stacked directly above it
/// and hidden whenever the target is minimised, cloaked or has no caption buttons.
/// </summary>
internal sealed class DecoratedWindow : IDisposable
{
    private readonly CaptionColorizer _colorizer;
    private readonly TrafficLightButtonsView _view = new();

    // Created on the first supported layout, not up front: many tracked windows never get one (dialogs with only
    // a close button, DPI-virtualised apps), and each surface is a full WPF window kept for the target's lifetime.
    // A surface that is never placed keeps WPF's default window size and its render buffers: measured about 9 MB,
    // 4 GDI and 2 USER objects for every such window.
    private CaptionSurface? _buttons;
    private CaptionSurface? _mask;
    private DecorationStyle _style;

    private CaptionOverlayLayout? _layout;
    private CaptionMetrics _anchor;
    private LayoutKey _layoutKey;
    private bool _drawsOwnCaption;
    private bool _isUnified;
    private HexColor? _sampledCaption;

    // Set for windows that draw their own title bar: where probing found their buttons, instead of DWM.
    private ProbedCaption? _probe;
    private bool _reprobeRequested;

    private bool _cloaked;
    private bool _minimized;
    private bool _shown;
    private bool _disposed;

    /// <summary>
    /// Tracks <paramref name="target"/>. Subscribe to the events, then call <see cref="UpdatePlacement"/>, which
    /// creates and shows the surfaces once the target has a layout they can cover.
    /// </summary>
    /// <param name="probe">For a window that draws its own title bar, where its buttons were probed; else null.</param>
    public DecoratedWindow(nint target, DecorationStyle style, CaptionColorizer colorizer, bool isActive, ProbedCaption? probe = null)
    {
        Target = target;
        _probe = probe;
        _style = style;
        _colorizer = colorizer;
        _view.IsWindowActive = isActive;
        _view.ButtonClicked += OnButtonClicked;
        _view.SetAppearance(style.Colors, style.Settings);
    }

    /// <summary>The decorated window.</summary>
    public nint Target { get; }

    /// <summary>
    /// Asks the owner to drop this decoration. The flag is true when the window must never be decorated again
    /// (UIPI blocks our commands), false when the overlay merely broke and may be recreated.
    /// </summary>
    public event Action<DecoratedWindow, bool>? RemovalRequested;

    /// <summary>Asks the owner to sample the title bar colour shortly (after the target has repainted).</summary>
    public event Action<DecoratedWindow>? SampleRequested;

    /// <summary>
    /// Asks the owner to probe a custom title bar again: the window's size or DPI changed, so the buttons may
    /// have moved. The overlay stays hidden until <see cref="SetProbe"/> is called.
    /// </summary>
    public event Action<DecoratedWindow>? ReprobeRequested;

    /// <summary>True when the buttons were found by probing rather than reported by DWM.</summary>
    public bool IsProbed => _probe is not null;

    /// <summary>Lets the next measurement ask for a probe again (the last one could not be used).</summary>
    public void AllowReprobe() => _reprobeRequested = false;

    /// <summary>Replaces the probed button position after a re-probe and shows the overlay again.</summary>
    public void SetProbe(ProbedCaption probe)
    {
        if (_disposed)
        {
            return;
        }

        _probe = probe;
        _reprobeRequested = false;
        _layoutKey = default;
        UpdatePlacement();
    }

    /// <summary>Applies new settings or theme colours, recomputing the layout and colours.</summary>
    public void ApplyStyle(DecorationStyle style)
    {
        if (_disposed)
        {
            return;
        }

        _style = style;
        _view.SetAppearance(style.Colors, style.Settings);
        UpdateMask();
        if (!style.Settings.UnifyTitleBarColor)
        {
            // Restore now: the rebuild below does not run while the window is minimised or on another desktop.
            _isUnified = false;
            _colorizer.Restore(Target);
        }

        // Force a full rebuild: side, order, size and colour mode may all have changed.
        _layoutKey = default;
        UpdatePlacement();
    }

    /// <summary>Updates the focus state (inactive windows are dimmed; active and inactive title bars differ).</summary>
    public void SetActive(bool active)
    {
        if (_disposed || _view.IsWindowActive == active)
        {
            return;
        }

        _view.IsWindowActive = active;
        RequestSample();
    }

    /// <summary>Tracks EVENT_SYSTEM_MINIMIZESTART/END.</summary>
    public void SetMinimized(bool minimized)
    {
        _minimized = minimized;
        UpdatePlacement();
    }

    /// <summary>Tracks EVENT_OBJECT_CLOAKED/UNCLOAKED (other virtual desktops, suspended UWP apps).</summary>
    public void SetCloaked(bool cloaked)
    {
        _cloaked = cloaked;
        UpdatePlacement();
    }

    /// <summary>
    /// Follows the target's current geometry. Called for every location change while the target is dragged,
    /// so the common case (same size, new position) only translates the cached layout and allocates nothing.
    /// </summary>
    public void UpdatePlacement()
    {
        if (_disposed)
        {
            return;
        }

        if (_cloaked || _minimized || NativeMethods.IsIconic(Target) || !TryMeasure(out var metrics))
        {
            Conceal();
            return;
        }

        var key = new LayoutKey(metrics.Buttons.Width, metrics.Buttons.Height, metrics.Frame.Width, metrics.Dpi, metrics.IsMaximized);
        if (key != _layoutKey)
        {
            _layoutKey = key;
            Rebuild(metrics, key.IsMaximized);
        }

        if (_layout is null)
        {
            Conceal();
            return;
        }

        // Same size as when the layout was built, so the overlay just moves with the frame. A layout implies that
        // the surfaces exist (see Rebuild).
        _buttons!.Place(_layout.Bounds.Offset(metrics.Frame.Left - _anchor.Frame.Left, metrics.Buttons.Top - _anchor.Buttons.Top));
        _mask?.Place(metrics.Buttons);
        if (!_shown)
        {
            // Stack the hidden surfaces first so they never flash above windows that cover the target, then once
            // more after showing in case showing moved them.
            _shown = true;
            Restack();
            _buttons.Reveal();
            _mask?.Reveal();
            Restack();
            RequestSample();
        }
    }

    /// <summary>Puts the surfaces back directly above the target after it may have moved in the z-order.</summary>
    public void Restack()
    {
        if (_disposed || !_shown || _buttons is null)
        {
            return;
        }

        var topmost = (NativeMethods.GetExStyle(Target) & NativeMethods.WS_EX_TOPMOST) != 0;
        _buttons.StackAbove(Target, topmost);
        _mask?.StackAbove(_buttons.Handle, topmost);
    }

    /// <summary>Samples the title bar colour if this window's surface is not painted in the unified colour.</summary>
    public void SampleTitleBar()
    {
        if (_disposed || !_shown || _isUnified || !TryMeasure(out var metrics))
        {
            return;
        }

        if (TitleBarSampler.TrySample(Target, metrics.Buttons, metrics.Scale, out var color))
        {
            _sampledCaption = color;
            SetFill(color);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.ButtonClicked -= OnButtonClicked;
        if (_buttons is not null)
        {
            _buttons.Closed -= OnSurfaceClosed;
            _buttons.Destroy();
        }

        _mask?.Destroy();
        _colorizer.Restore(Target);
    }

    /// <summary>
    /// Reads where the target's native buttons are: from DWM, or for a probed custom title bar from the probe,
    /// asking (once) for a new probe when the window's size or DPI has changed since.
    /// </summary>
    private bool TryMeasure(out CaptionMetrics metrics)
    {
        if (_probe is not { } probe)
        {
            return CaptionMetrics.TryRead(Target, out metrics);
        }

        if (CaptionMetrics.TryReadProbed(Target, probe, out metrics, out var stale))
        {
            return true;
        }

        if (stale && !_reprobeRequested)
        {
            _reprobeRequested = true;
            ReprobeRequested?.Invoke(this);
        }

        return false;
    }

    private void Rebuild(CaptionMetrics metrics, bool isMaximized)
    {
        var style = NativeMethods.GetStyle(Target);
        var canMinimize = (style & NativeMethods.WS_MINIMIZEBOX) != 0;
        var canMaximize = (style & NativeMethods.WS_MAXIMIZEBOX) != 0;
        _layout = CaptionDecorationRules.HasButtonTrio(canMinimize, canMaximize) && !IsDpiVirtualized(metrics.Dpi)
            ? CaptionButtonLayout.Compute(metrics.Buttons, metrics.Frame, metrics.Scale, _style.Settings)
            : null;
        if (_layout is null || !EnsureSurfaces())
        {
            // Hidden until the window is supported again; it should not keep a recoloured title bar meanwhile.
            _layout = null;
            _isUnified = false;
            _colorizer.Restore(Target);
            return;
        }

        _anchor = metrics;
        _view.SetLayout(_layout, metrics.Scale, canMinimize, canMaximize);
        _view.SetSurfaceScale(_buttons!.SurfaceScale);

        // Only surfaces touching the window's top-right corner need its rounding; maximised windows are square.
        // The surfaces stop inside the border (see CaptionMetrics.Buttons), so they follow its inner edge.
        var cornerRadius = isMaximized
            ? 0
            : CaptionButtonGeometry.InnerCornerRadius(CaptionButtonGeometry.WindowCornerRadiusPixels(metrics.Scale), metrics.Border);
        _buttons.SetTopRightCornerRadius(_style.Settings.Side == ButtonSide.Right ? cornerRadius : 0);
        _mask?.SetTopRightCornerRadius(cornerRadius);

        var clientOrigin = default(POINT);
        _drawsOwnCaption = NativeMethods.ClientToScreen(Target, ref clientOrigin)
            && CaptionButtonGeometry.ClientCoversCaption(clientOrigin.Y, metrics.Buttons);
        UpdateCaptionColour();
    }

    /// <summary>
    /// Unifies the title bar through DWM when enabled and visible to DWM; otherwise restores the system colour
    /// and falls back to sampling. Apps that paint their own title bar (tabs, Mica) are never recoloured: DWM's
    /// caption colour does not show behind their client area.
    /// </summary>
    private void UpdateCaptionColour()
    {
        _isUnified = _style.Settings.UnifyTitleBarColor && !_drawsOwnCaption
            && _colorizer.Apply(Target, _style.UnifiedCaption, _style.UnifiedText);
        if (_isUnified)
        {
            SetFill(_style.UnifiedCaption);
            return;
        }

        _colorizer.Restore(Target);
        SetFill(_sampledCaption ?? _style.PlaceholderCaption);
        RequestSample();
    }

    private void SetFill(HexColor color)
    {
        _buttons?.SetFill(color);
        _mask?.SetFill(color);
    }

    /// <summary>Creates the surfaces on first use; false (after asking to be dropped) when the system refused.</summary>
    private bool EnsureSurfaces()
    {
        if (_buttons is not null)
        {
            return true;
        }

        try
        {
            var buttons = new CaptionSurface(_view);
            buttons.Closed += OnSurfaceClosed;
            buttons.DpiChanged += (_, _) => _view.SetSurfaceScale(buttons.SurfaceScale);
            _buttons = buttons;
            UpdateMask();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ThrottledLog.Warn("decorate", $"Could not create window buttons for 0x{Target:X}: {ex.Message}");
            RemovalRequested?.Invoke(this, false);
            return false;
        }
    }

    /// <summary>Left-side circles also need a surface that masks the native buttons (once the buttons exist).</summary>
    private void UpdateMask()
    {
        var needed = _buttons is not null && _style.Settings.Side == ButtonSide.Left;
        if (needed && _mask is null)
        {
            _mask = new CaptionSurface(content: null);
            _mask.Closed += OnSurfaceClosed;
            if (_shown)
            {
                // Show it on the next placement together with the buttons.
                Conceal();
            }
        }
        else if (!needed && _mask is not null)
        {
            _mask.Closed -= OnSurfaceClosed;
            _mask.Destroy();
            _mask = null;
        }
    }

    private void Conceal()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        _buttons?.Conceal();
        _mask?.Conceal();
    }

    private void RequestSample()
    {
        if (_shown && !_isUnified)
        {
            SampleRequested?.Invoke(this);
        }
    }

    private bool IsDpiVirtualized(uint windowDpi)
    {
        var monitor = NativeMethods.MonitorFromWindow(Target, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out var monitorDpi, out _) == 0
            && CaptionDecorationRules.IsDpiVirtualized(windowDpi, monitorDpi);
    }

    private void OnButtonClicked(CaptionButtonKind kind)
    {
        // A disabled window (one showing a modal dialog) ignores its native caption buttons. DefWindowProc would
        // still act on a posted WM_SYSCOMMAND, closing or minimising the owner out from under its dialog.
        if (_disposed || !NativeMethods.IsWindowEnabled(Target))
        {
            return;
        }

        if (CaptionCommands.Invoke(Target, kind) == CaptionCommandResult.AccessDenied)
        {
            RemovalRequested?.Invoke(this, true);
        }
    }

    private void OnSurfaceClosed(object? sender, EventArgs e)
    {
        // Only reached when a surface's HWND is destroyed behind our back (Dispose unsubscribes first).
        if (!_disposed)
        {
            RemovalRequested?.Invoke(this, false);
        }
    }

    /// <summary>The target geometry a layout depends on; a change means the layout must be recomputed.</summary>
    private readonly record struct LayoutKey(int ButtonsWidth, int ButtonsHeight, int FrameWidth, uint Dpi, bool IsMaximized);
}
