using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Mouse;

/// <summary>
/// Mouse &amp; Touchpad: primary button, pointer speed, acceleration ("Enhance pointer precision") and wheel scroll
/// lines, through SystemParametersInfo with the change persisted and broadcast, exactly as Windows' Mouse settings do.
/// Touchpad gestures live in Windows Settings (linked).
/// </summary>
internal sealed class MousePanelViewModel : SystemPanelViewModel
{
    private const string Left = "Left";
    private const string Right = "Right";

    private readonly SystemSettingWriter _writer;
    private readonly Debouncer _speedWrite;
    private readonly Debouncer _scrollWrite;
    private bool _leftHanded;
    private int _speed = 10;
    private bool _acceleration;
    private int _scrollLines = 3;
    private bool _pageScroll;

    public MousePanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Mouse)
    {
        _writer = context.CreateWriter();
        _speedWrite = new Debouncer(WriteSpeed);
        _scrollWrite = new Debouncer(WriteScrollLines);
        OpenTouchpadCommand = new RelayCommand(() => context.OpenLink("ms-settings:devices-touchpad"));
    }

    public ICommand OpenTouchpadCommand { get; }

    /// <summary>The choices of the primary button, as GNOME labels them.</summary>
    public IReadOnlyList<string> PrimaryButtons { get; } = [Left, Right];

    /// <summary>"Left" or "Right": which button clicks; "Right" swaps the buttons for left-handed use.</summary>
    public string PrimaryButton
    {
        get => _leftHanded ? Right : Left;
        set
        {
            var swap = value == Right;
            if (value is null || swap == _leftHanded)
            {
                return;
            }

            _leftHanded = swap;
            OnPropertyChanged();
            _writer.Run($"make the {value.ToLowerInvariant()} mouse button primary",
                () => Spi.Set(NativeMethods.SPI_SETMOUSEBUTTONSWAP, swap ? 1u : 0u, 0, "the primary mouse button"),
                () => ReportWriteFailure("the primary button"));
        }
    }

    /// <summary>Pointer speed 1..20 (Windows' slider; 10 is the default).</summary>
    public int Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, InputTuning.ClampMouseSpeed(value)))
            {
                _speedWrite.Trigger();
            }
        }
    }

    public bool Acceleration
    {
        get => _acceleration;
        set
        {
            if (SetProperty(ref _acceleration, value))
            {
                var parameters = InputTuning.AccelerationParameters(value);
                _writer.Run($"turn pointer acceleration {(value ? "on" : "off")}",
                    () => Spi.SetArray(NativeMethods.SPI_SETMOUSE, parameters, "pointer acceleration"),
                    () => ReportWriteFailure("pointer acceleration"));
            }
        }
    }

    /// <summary>Lines per wheel notch.</summary>
    public int ScrollLines
    {
        get => _scrollLines;
        set
        {
            if (SetProperty(ref _scrollLines, InputTuning.ClampScrollLines(value)))
            {
                OnPropertyChanged(nameof(ScrollLinesText));
                _pageScroll = false;
                _scrollWrite.Trigger();
            }
        }
    }

    /// <summary>The slider's top end: 30, or the current value when Windows was set higher, so the slider never clamps it.</summary>
    public int ScrollLinesMaximum => Math.Max(30, _scrollLines);

    public string ScrollLinesText => _pageScroll
        ? "One screen"
        : _scrollLines.ToString(CultureInfo.CurrentCulture) + (_scrollLines == 1 ? " line" : " lines");

    protected override void Open()
    {
        _leftHanded = NativeMethods.GetSystemMetrics(NativeMethods.SM_SWAPBUTTON) != 0;
        _speed = InputTuning.ClampMouseSpeed(Spi.Get(NativeMethods.SPI_GETMOUSESPEED, 10, "the mouse speed"));
        var lines = Spi.Get(NativeMethods.SPI_GETWHEELSCROLLLINES, 3, "the wheel scroll lines");
        _pageScroll = lines == NativeMethods.WHEEL_PAGESCROLL;
        _scrollLines = InputTuning.ClampScrollLines(lines);

        var mouse = new int[3];
        if (NativeMethods.SystemParametersInfoArray(NativeMethods.SPI_GETMOUSE, 0, mouse, 0))
        {
            _acceleration = InputTuning.IsAccelerationOn(mouse);
        }
        else
        {
            Log.Warn($"Could not read the pointer acceleration (error {Marshal.GetLastPInvokeError()})");
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>A change still waiting for its debounce is written now rather than lost.</summary>
    protected override void Close()
    {
        _speedWrite.Flush();
        _scrollWrite.Flush();
    }

    private void WriteSpeed()
    {
        var speed = _speed;
        _writer.Run($"set the mouse speed to {speed}",
            () => Spi.Set(NativeMethods.SPI_SETMOUSESPEED, 0, speed, "the mouse speed"),
            () => ReportWriteFailure("the pointer speed"));
    }

    private void WriteScrollLines()
    {
        var lines = (uint)_scrollLines;
        _writer.Run($"set the wheel to scroll {lines} lines",
            () => Spi.Set(NativeMethods.SPI_SETWHEELSCROLLLINES, lines, 0, "the wheel scroll lines"),
            () => ReportWriteFailure("the scroll speed"));
    }
}
