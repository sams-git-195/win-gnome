using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Accessibility;

/// <summary>
/// Accessibility: sticky, slow and bounce keys, pointer size, text cursor thickness, reduce animation, high contrast,
/// and launchers for the on-screen keyboard, Magnifier and Narrator. Every change is verified-set: written on the
/// settings writer thread, read back from Windows, and the page shows what Windows reports. While a write is in flight
/// the switches are disabled. Pointer colour, text size and the text cursor indicator open Windows Settings.
/// </summary>
internal sealed class AccessibilityPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private readonly Debouncer _cursorWrite;
    private readonly Debouncer _caretWrite;
    private AccessibilityService? _service;
    private AccessibilityState? _state;
    private int _cursorSize = CursorSizeScale.MinSize;
    private int _caretWidth = Core.ControlCenter.CaretWidth.Min;
    private int _writesInFlight;

    // Bumped by every Open and Close, so a re-read that finishes after the page closed is dropped.
    private int _openCount;

    public AccessibilityPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Accessibility)
    {
        _writer = context.CreateWriter();
        _cursorWrite = new Debouncer(WriteCursorSize);
        _caretWrite = new Debouncer(WriteCaretWidth);
        OnScreenKeyboardCommand = new RelayCommand(() => ShellLaunch.SystemTool("osk.exe"));
        MagnifierCommand = new RelayCommand(() => ShellLaunch.SystemTool("magnify.exe"));
        NarratorCommand = new RelayCommand(() => ShellLaunch.SystemTool("narrator.exe"));
        PointerSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:easeofaccess-mousepointer"));
        TextCursorSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:easeofaccess-cursor"));
        TextSizeSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:easeofaccess-display"));
    }

    /// <summary>True once Windows has been read, while nothing is being written, and not in safe mode.</summary>
    public bool CanChange => CanEdit && _state is not null && _writesInFlight == 0;

    /// <summary>A write is in flight; the page shows the value Windows reports once it returns.</summary>
    public bool IsBusy => _writesInFlight > 0;

    public bool StickyKeys
    {
        get => _state is { } state && StickyKeysFlags.IsOn(state.StickyKeysFlags);
        set => Change(value, StickyKeys, $"turn sticky keys {OnOff(value)}", "sticky keys", _ => AccessibilityService.SetStickyKeys(value));
    }

    public bool SlowKeys
    {
        get => _state?.FilterKeys.IsSlowKeysOn ?? false;
        set => Change(value, SlowKeys, $"turn slow keys {OnOff(value)}", "slow keys", _ => AccessibilityService.SetSlowKeys(value));
    }

    public bool BounceKeys
    {
        get => _state?.FilterKeys.IsBounceKeysOn ?? false;
        set => Change(value, BounceKeys, $"turn bounce keys {OnOff(value)}", "bounce keys", _ => AccessibilityService.SetBounceKeys(value));
    }

    /// <summary>Pointer size step, 1..15. Written when the slider settles.</summary>
    public int CursorSize
    {
        get => _cursorSize;
        set
        {
            if (CanChangeCursorSize && SetProperty(ref _cursorSize, Math.Clamp(value, CursorSizeScale.MinSize, CursorSizeScale.MaxSize)))
            {
                OnPropertyChanged(nameof(CursorSizeText));
                _cursorWrite.Trigger();
            }
        }
    }

    public string CursorSizeText => CursorSizeScale.ToPixels(_cursorSize).ToString(CultureInfo.CurrentCulture) + " px";

    /// <summary>
    /// The slider follows the editable state rather than <see cref="CanChange"/>, so a write in flight doesn't cut
    /// a drag short; the value Windows reports is shown once the drag settles.
    /// </summary>
    public bool CanChangeCursorSize => CanEdit && _state is { CanChangeCursorSize: true };

    public string CursorSizeNote => _state is { CanChangeCursorSize: false }
        ? "Black, inverted, coloured and custom pointers are resized in Windows Settings."
        : "Make the mouse pointer bigger.";

    /// <summary>Text cursor thickness in pixels, 1..20. Written when the slider settles.</summary>
    public int CaretWidth
    {
        get => _caretWidth;
        set
        {
            if (CanEdit && _state is not null && SetProperty(ref _caretWidth, Core.ControlCenter.CaretWidth.Clamp(value)))
            {
                OnPropertyChanged(nameof(CaretWidthText));
                _caretWrite.Trigger();
            }
        }
    }

    public string CaretWidthText => _caretWidth.ToString(CultureInfo.CurrentCulture) + " px";

    public bool CanChangeCaretWidth => CanEdit && _state is not null;

    /// <summary>Windows' "Animation effects" off (SPI_SETCLIENTAREAANIMATION); WinGnome's overview follows it too.</summary>
    public bool ReduceAnimation
    {
        get => _state is { ClientAreaAnimation: false };
        set => Change(value, ReduceAnimation, $"turn animation effects {OnOff(!value)}", "animation effects", _ => AccessibilityService.SetReduceAnimation(value));
    }

    public IReadOnlyList<string> ContrastChoices => Contrast().Choices;

    /// <summary>None or a contrast theme. Windows shows its own "Please wait" while it switches.</summary>
    public string HighContrast
    {
        get => Contrast().Selected;
        set
        {
            if (value is null || value == HighContrast)
            {
                return;
            }

            var theme = ContrastThemes.FromDisplayName(value);
            if (!CanChange || (theme is null && value != ContrastThemes.None))
            {
                // Not now, or a custom scheme listed only so the row shows what Windows has: put the row back.
                OnPropertyChanged();
                return;
            }

            Write(theme is null ? "turn high contrast off" : $"turn high contrast on with {theme.DisplayName}",
                "high contrast", _ => AccessibilityService.SetHighContrast(theme));
        }
    }

    public string TextSizeText => (_state?.TextScalePercent ?? 100).ToString(CultureInfo.CurrentCulture) + "%";

    public ICommand OnScreenKeyboardCommand { get; }

    public ICommand MagnifierCommand { get; }

    public ICommand NarratorCommand { get; }

    public ICommand PointerSettingsCommand { get; }

    public ICommand TextCursorSettingsCommand { get; }

    public ICommand TextSizeSettingsCommand { get; }

    protected override void Open()
    {
        _openCount++;
        _writesInFlight = 0;
        _state = null;
        _service = new AccessibilityService();
        OnPropertyChanged(string.Empty);
        var service = _service;
        LoadAsync(service.Read, Show);
    }

    /// <summary>A slider change still waiting for its debounce is written now rather than lost.</summary>
    protected override void Close()
    {
        _cursorWrite.Flush();
        _caretWrite.Flush();
        _openCount++;
        _service = null;
    }

    private (IReadOnlyList<string> Choices, string Selected) Contrast() =>
        ContrastThemes.ChoicesFor(_state?.HighContrastOn ?? false, _state?.HighContrastScheme);

    private static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>Shows what Windows reports. A slider whose own write is still waiting keeps the user's value.</summary>
    private void Show(AccessibilityState state)
    {
        _state = state;
        if (!_cursorWrite.IsPending)
        {
            _cursorSize = state.CursorSize;
        }

        if (!_caretWrite.IsPending)
        {
            _caretWidth = Core.ControlCenter.CaretWidth.Clamp(state.CaretWidth);
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Starts a verified-set write when <paramref name="requested"/> differs from what is shown.</summary>
    private void Change(bool requested, bool shown, string what, string failure, Func<AccessibilityService, bool> write,
        [CallerMemberName] string? property = null)
    {
        if (requested == shown)
        {
            return;
        }

        if (!CanChange)
        {
            // The switch flipped itself; put it back to what Windows has.
            OnPropertyChanged(property);
            return;
        }

        Write(what, failure, write);
    }

    private void WriteCursorSize()
    {
        var size = _cursorSize;
        Write($"set the pointer size to {size}", "the pointer size", service => service.SetCursorSize(size));
    }

    private void WriteCaretWidth()
    {
        var width = _caretWidth;
        Write($"set the text cursor thickness to {width} px", "the text cursor thickness", _ => AccessibilityService.SetCaretWidth(width));
    }

    /// <summary>
    /// Queues <paramref name="write"/> on the writer thread, then reads Windows back on that thread and shows the result
    /// here, whether the write succeeded or not (verified-set: the page never shows a value Windows doesn't hold).
    /// </summary>
    private void Write(string what, string failure, Func<AccessibilityService, bool> write)
    {
        if (_service is not { } service || !CanEdit)
        {
            return;
        }

        var openCount = _openCount;
        _writesInFlight++;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanChange));
        _writer.Run(what, () =>
        {
            try
            {
                return write(service);
            }
            finally
            {
                ShowReadBack(service, openCount);
            }
        }, () => ReportWriteFailure(failure));
    }

    /// <summary>Runs on the writer thread after a write: reads Windows and posts the result to the page if it is still open.</summary>
    private void ShowReadBack(AccessibilityService service, int openCount)
    {
        AccessibilityState? state = null;
        try
        {
            state = service.Read();
        }
        catch (Exception ex)
        {
            // Writer thread boundary: logged and shown, never thrown.
            Log.Warn("Accessibility: could not read the settings back after a change", ex);
        }

        Context.Dispatcher.BeginInvoke(() =>
        {
            if (openCount != _openCount)
            {
                return;
            }

            _writesInFlight--;
            if (state is null)
            {
                Problem = "Couldn't read these settings back from Windows. You can check them in Windows Settings.";
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanChange));
                return;
            }

            Show(state);
        });
    }
}
