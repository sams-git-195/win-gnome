using System.IO;
using WinGnome.Core.Settings;

namespace WinGnome.Infrastructure;

/// <summary>
/// Owns the live <see cref="AppSettings"/>. Consumers read <see cref="Current"/> (treat it as read-only)
/// and listen to <see cref="Changed"/>. Edits go through <see cref="Update"/>, which persists and notifies.
/// </summary>
internal sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    public string Directory => _store.Directory;

    /// <summary>Raised on the UI thread after settings change and have been saved.</summary>
    public event EventHandler<AppSettings>? Changed;

    /// <summary>Applies <paramref name="edit"/> to a copy of the settings, normalises, saves and notifies.</summary>
    public void Update(Action<AppSettings> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var draft = Current.Clone();
        edit(draft);
        Replace(draft);
    }

    /// <summary>Replaces the settings wholesale (e.g. from the settings window or "reset to defaults").</summary>
    public void Replace(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = settings.Normalize();
        try
        {
            _store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Failed to save settings", ex);
        }

        Changed?.Invoke(this, Current);
    }
}
