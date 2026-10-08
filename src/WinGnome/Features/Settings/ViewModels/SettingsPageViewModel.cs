using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Features.Settings.ViewModels.Items;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>
/// Base of every settings page. It builds the page's <see cref="SettingItem"/>s, keeps them in step with the live
/// settings, and flushes pending debounced edits when the window closes.
/// </summary>
internal abstract class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly List<SettingItem> _items = [];

    protected SettingsPageViewModel(SettingsService settings, string title, string glyph)
    {
        Settings = settings;
        Title = title;
        Glyph = glyph;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>Page title, shown in the sidebar and above the page.</summary>
    public string Title { get; }

    /// <summary>Segoe Fluent Icons glyph shown in the sidebar.</summary>
    public string Glyph { get; }

    protected SettingsService Settings { get; }

    /// <summary>Called when the page becomes the visible page. Pages whose state lives outside the settings refresh here.</summary>
    public virtual void OnSelected()
    {
    }

    /// <summary>Saves edits that are still waiting for their debounce timer.</summary>
    public void Flush()
    {
        foreach (var item in _items)
        {
            item.Flush();
        }
    }

    public virtual void Dispose() => Settings.Changed -= OnSettingsChanged;

    /// <summary>Called after the settings changed, once every tracked item has refreshed.</summary>
    protected virtual void OnSettingsApplied(AppSettings settings)
    {
    }

    protected T Track<T>(T item)
        where T : SettingItem
    {
        _items.Add(item);
        return item;
    }

    protected ToggleSetting Toggle(Func<AppSettings, bool> get, Action<AppSettings, bool> set) =>
        Track(new ToggleSetting(Settings, get, set));

    protected SliderSetting Slider(Func<AppSettings, double> get, Action<AppSettings, double> set,
        double minimum, double maximum, double step, Func<double, string> format) =>
        Track(new SliderSetting(Settings, get, set, minimum, maximum, step, format));

    protected ChoiceSetting Choice<T>(Func<AppSettings, T> get, Action<AppSettings, T> set, params ChoiceOption[] options)
        where T : notnull =>
        Track(ChoiceSetting.For(Settings, get, set, options));

    protected ColorEditor Color(Func<AppSettings, string> get, Action<AppSettings, string> set, IReadOnlyList<ColorPreset> presets,
        bool isOptional = false, Action<AppSettings, ColorPreset>? applyPreset = null) =>
        Track(new ColorEditor(Settings, get, set, presets, isOptional, applyPreset));

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        foreach (var item in _items)
        {
            item.Refresh();
        }

        OnSettingsApplied(settings);
        OnPropertyChanged(string.Empty);
    }
}
