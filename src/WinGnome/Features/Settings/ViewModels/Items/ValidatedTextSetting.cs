using System.ComponentModel;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>Result of validating what the user typed.</summary>
/// <param name="IsValid">Whether the text can be saved.</param>
/// <param name="Normalized">The canonical form that is saved when valid.</param>
/// <param name="Error">Message shown when invalid.</param>
internal readonly record struct TextValidation(bool IsValid, string Normalized, string? Error)
{
    public static TextValidation Valid(string normalized) => new(true, normalized, null);

    public static TextValidation Invalid(string error) => new(false, "", error);
}

/// <summary>
/// A text setting with validation. Invalid text is kept in the box, flagged through <see cref="IDataErrorInfo"/>
/// (the text box turns red) and never saved; valid text is saved in its normalised form after a short pause.
/// </summary>
internal class ValidatedTextSetting : SettingItem, IDataErrorInfo
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsService _settings;
    private readonly Func<AppSettings, string> _get;
    private readonly Action<AppSettings, string> _set;
    private readonly Func<string, TextValidation> _validate;
    private readonly Debouncer _debouncer;
    private string _text;
    private TextValidation _state;
    private string _committed;

    public ValidatedTextSetting(SettingsService settings, Func<AppSettings, string> get, Action<AppSettings, string> set,
        Func<string, TextValidation> validate)
    {
        _settings = settings;
        _get = get;
        _set = set;
        _validate = validate;
        _debouncer = new Debouncer(Commit, SaveDelay);
        _committed = get(settings.Current);
        _text = _committed;
        _state = validate(_text);
    }

    /// <summary>The text in the box.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text)
            {
                return;
            }

            _text = value;
            _state = _validate(value);
            OnPropertyChanged(string.Empty);
            if (_state.IsValid)
            {
                _debouncer.Trigger();
            }
            else
            {
                _debouncer.Cancel();
            }
        }
    }

    /// <summary>True when <see cref="Text"/> can be saved.</summary>
    public bool IsValid => _state.IsValid;

    /// <summary>The canonical form of the text (for example a hotkey as "Ctrl+Alt+T"); empty while invalid.</summary>
    public string Normalized => _state.Normalized;

    /// <summary>The validation message, or an empty string.</summary>
    public string Error => _state.Error ?? "";

    string IDataErrorInfo.this[string columnName] => columnName == nameof(Text) ? Error : "";

    public override void Refresh()
    {
        var current = _get(_settings.Current);
        if (current != _committed)
        {
            ShowCommitted(current);
        }
    }

    public override void Flush() => _debouncer.Flush();

    /// <summary>Discards whatever was being typed and shows the value currently in the settings.</summary>
    protected void Reload()
    {
        _debouncer.Cancel();
        ShowCommitted(_get(_settings.Current));
    }

    private void ShowCommitted(string value)
    {
        _committed = value;
        _text = value;
        _state = _validate(value);
        OnPropertyChanged(string.Empty);
    }

    private void Commit()
    {
        if (_state.IsValid && _state.Normalized != _committed)
        {
            _committed = _state.Normalized;
            _settings.Update(s => _set(s, _state.Normalized));
        }
    }
}
