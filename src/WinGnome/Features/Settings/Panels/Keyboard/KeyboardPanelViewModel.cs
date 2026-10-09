using System.Globalization;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Keyboard;

/// <summary>One installed input source (keyboard layout / input language).</summary>
internal sealed record InputSource(string Name, string Detail, bool IsCurrent);

/// <summary>
/// Keyboard: key repeat delay and rate through SystemParametersInfo, and the installed input sources (listed; adding,
/// removing and the switch shortcut are in Windows Settings, linked). Switching sources is Win+Space, as in GNOME's
/// Super+Space.
/// </summary>
internal sealed class KeyboardPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private readonly Debouncer _delayWrite;
    private readonly Debouncer _speedWrite;
    private int _delay;
    private int _speed;
    private IReadOnlyList<InputSource> _sources = [];

    public KeyboardPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Keyboard)
    {
        _writer = context.CreateWriter();
        _delayWrite = new Debouncer(WriteDelay);
        _speedWrite = new Debouncer(WriteSpeed);
        AddSourceCommand = new RelayCommand(() => context.OpenLink("ms-settings:regionlanguage"));
        ShortcutsCommand = new RelayCommand(() => context.OpenLink("ms-settings:typing"));
    }

    /// <summary>Repeat delay step 0..3 (250 ms to 1 s).</summary>
    public int Delay
    {
        get => _delay;
        set
        {
            if (SetProperty(ref _delay, Math.Clamp(value, 0, InputTuning.MaxRepeatDelayIndex)))
            {
                OnPropertyChanged(nameof(DelayText));
                _delayWrite.Trigger();
            }
        }
    }

    public string DelayText => InputTuning.RepeatDelayMs(_delay).ToString(CultureInfo.CurrentCulture) + " ms";

    /// <summary>Repeat rate step 0..31 (about 2.5 to 30 repeats a second).</summary>
    public int Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, Math.Clamp(value, 0, InputTuning.MaxRepeatSpeed)))
            {
                OnPropertyChanged(nameof(SpeedText));
                _speedWrite.Trigger();
            }
        }
    }

    public string SpeedText => InputTuning.RepeatsPerSecond(_speed).ToString("0.#", CultureInfo.CurrentCulture) + " per second";

    public IReadOnlyList<InputSource> Sources
    {
        get => _sources;
        private set => SetProperty(ref _sources, value);
    }

    public ICommand AddSourceCommand { get; }

    public ICommand ShortcutsCommand { get; }

    protected override void Open()
    {
        _delay = Math.Clamp(Spi.Get(NativeMethods.SPI_GETKEYBOARDDELAY, 1, "the key repeat delay"), 0, InputTuning.MaxRepeatDelayIndex);
        _speed = Math.Clamp(Spi.Get(NativeMethods.SPI_GETKEYBOARDSPEED, 31, "the key repeat rate"), 0, InputTuning.MaxRepeatSpeed);
        OnPropertyChanged(string.Empty);
        Sources = ReadSources();
    }

    protected override void Close()
    {
        _delayWrite.Flush();
        _speedWrite.Flush();
    }

    /// <summary>Lists the session's input locales; the low word of an HKL is its language, the high word its layout.</summary>
    private static List<InputSource> ReadSources()
    {
        var count = NativeMethods.GetKeyboardLayoutList(0, null);
        var layouts = new nint[Math.Max(count, 0)];
        count = NativeMethods.GetKeyboardLayoutList(layouts.Length, layouts);
        var current = NativeMethods.GetKeyboardLayout(0);
        var sources = new List<InputSource>(count);
        foreach (var hkl in layouts.Take(count))
        {
            var language = (int)(hkl & 0xFFFF);
            var layout = (int)((hkl >> 16) & 0xFFFF);
            var name = LanguageName(language);
            var detail = LayoutName(language, layout);
            sources.Add(new InputSource(name, detail, hkl == current));
        }

        return sources;
    }

    /// <summary>
    /// The keyboard layout part of an HKL: the language's own layout, another language's layout (a high word without
    /// the 0xF000 bits is that language's id, e.g. a Spanish keyboard for English), or a variant.
    /// </summary>
    private static string LayoutName(int languageId, int layoutId)
    {
        if (layoutId == languageId)
        {
            return "Standard keyboard";
        }

        return (layoutId & 0xF000) == 0xF000
            ? $"Keyboard variant {layoutId & 0x0FFF}"
            : LanguageName(layoutId) + " keyboard";
    }

    private static string LanguageName(int languageId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return $"Language {languageId:X4}";
        }
    }

    private void WriteDelay()
    {
        var delay = (uint)_delay;
        _writer.Run($"set the key repeat delay to step {delay}",
            () => Spi.Set(NativeMethods.SPI_SETKEYBOARDDELAY, delay, 0, "the key repeat delay"),
            () => ReportWriteFailure("the repeat delay"));
    }

    private void WriteSpeed()
    {
        var speed = (uint)_speed;
        _writer.Run($"set the key repeat rate to step {speed}",
            () => Spi.Set(NativeMethods.SPI_SETKEYBOARDSPEED, speed, 0, "the key repeat rate"),
            () => ReportWriteFailure("the repeat speed"));
    }
}
