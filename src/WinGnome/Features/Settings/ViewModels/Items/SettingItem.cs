using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>
/// One editable value of <see cref="AppSettings"/> exposed to a page: it reads the live settings and writes
/// changes back through <see cref="SettingsService.Update"/>, so every change applies immediately.
/// </summary>
internal abstract class SettingItem : ObservableObject
{
    /// <summary>Re-reads the live settings and raises change notifications. Called after any settings change.</summary>
    public abstract void Refresh();

    /// <summary>Commits a change that is still waiting on a debounce timer. Called before the window closes.</summary>
    public virtual void Flush()
    {
    }
}

/// <summary>An on/off setting bound to a toggle switch.</summary>
internal sealed class ToggleSetting(SettingsService settings, Func<AppSettings, bool> get, Action<AppSettings, bool> set) : SettingItem
{
    public bool Value
    {
        get => get(settings.Current);
        set
        {
            if (value != Value)
            {
                settings.Update(s => set(s, value));
            }
        }
    }

    public override void Refresh() => OnPropertyChanged(nameof(Value));
}
