using System.Globalization;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>
/// A numeric setting edited with a slider. The thumb follows the pointer immediately while the settings are
/// saved after a short pause (see <see cref="Debouncer"/>), and outside changes never yank the thumb mid-drag.
/// </summary>
internal sealed class SliderSetting : SettingItem
{
    private readonly SettingsService _settings;
    private readonly Func<AppSettings, double> _get;
    private readonly Action<AppSettings, double> _set;
    private readonly Func<double, string> _format;
    private readonly Debouncer _debouncer;
    private double _value;

    /// <param name="settings">The live settings.</param>
    /// <param name="get">Reads the value from settings.</param>
    /// <param name="set">Writes the value into a settings draft.</param>
    /// <param name="minimum">Lowest slider value.</param>
    /// <param name="maximum">Highest slider value.</param>
    /// <param name="step">Snap interval.</param>
    /// <param name="format">Turns a value into the label shown beside the slider.</param>
    public SliderSetting(SettingsService settings, Func<AppSettings, double> get, Action<AppSettings, double> set,
        double minimum, double maximum, double step, Func<double, string> format)
    {
        _settings = settings;
        _get = get;
        _set = set;
        _format = format;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        _debouncer = new Debouncer(Commit);
        _value = get(settings.Current);
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    /// <summary>Current slider position.</summary>
    public double Value
    {
        get => _value;
        set
        {
            // Snapping to ticks leaves floating-point dust (0.30000000000000004); keep the saved value clean.
            if (SetProperty(ref _value, Math.Round(value, 4)))
            {
                OnPropertyChanged(nameof(Text));
                _debouncer.Trigger();
            }
        }
    }

    /// <summary>The formatted value shown beside the slider.</summary>
    public string Text => _format(_value);

    /// <summary>Formats a value as a whole-number percentage ("75%").</summary>
    public static string Percent(double value) => value.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>Formats a value as whole device-independent pixels ("32 px").</summary>
    public static string Pixels(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0.#} px");

    public override void Refresh()
    {
        if (_debouncer.IsPending)
        {
            return;
        }

        var current = _get(_settings.Current);
        if (SetProperty(ref _value, current, nameof(Value)))
        {
            OnPropertyChanged(nameof(Text));
        }
    }

    public override void Flush() => _debouncer.Flush();

    private void Commit()
    {
        var value = _value;
        if (value != _get(_settings.Current))
        {
            _settings.Update(s => _set(s, value));
        }
    }
}
