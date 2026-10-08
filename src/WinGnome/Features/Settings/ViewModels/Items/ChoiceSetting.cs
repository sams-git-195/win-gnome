using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>One entry of a <see cref="ChoiceSetting"/>.</summary>
/// <param name="Label">Friendly name shown in the list.</param>
/// <param name="Value">The underlying setting value.</param>
/// <param name="Description">Optional explanation shown under the setting while this option is selected.</param>
internal sealed record ChoiceOption(string Label, object Value, string? Description = null)
{
    public static ChoiceOption Of<T>(T value, string label, string? description = null)
        where T : notnull => new(label, value, description);
}

/// <summary>A setting that picks one value from a short list (an enum), shown in a combo box or segmented control.</summary>
internal sealed class ChoiceSetting : SettingItem
{
    private readonly SettingsService _settings;
    private readonly Func<AppSettings, object> _get;
    private readonly Action<AppSettings, object> _set;

    private ChoiceSetting(SettingsService settings, IReadOnlyList<ChoiceOption> options, Func<AppSettings, object> get, Action<AppSettings, object> set)
    {
        _settings = settings;
        Options = options;
        _get = get;
        _set = set;
    }

    /// <summary>Creates a choice over enum (or other value) options.</summary>
    public static ChoiceSetting For<T>(SettingsService settings, Func<AppSettings, T> get, Action<AppSettings, T> set, params ChoiceOption[] options)
        where T : notnull =>
        new(settings, options, s => get(s), (s, value) => set(s, (T)value));

    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>The option matching the live settings.</summary>
    public ChoiceOption? Selected
    {
        get
        {
            var current = _get(_settings.Current);
            return Options.FirstOrDefault(o => o.Value.Equals(current));
        }
        set
        {
            if (value is not null && !value.Equals(Selected))
            {
                _settings.Update(s => _set(s, value.Value));
            }
        }
    }

    /// <summary>The selected option's description, if it has one.</summary>
    public string? SelectedDescription => Selected?.Description;

    public override void Refresh()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(SelectedDescription));
    }
}
