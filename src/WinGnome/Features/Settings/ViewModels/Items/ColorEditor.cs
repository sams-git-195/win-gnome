using System.Windows.Input;
using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>A preset swatch of a <see cref="ColorEditor"/>.</summary>
internal sealed record ColorSwatch(ColorPreset Preset, Brush Brush)
{
    public string Name => Preset.Name;
}

/// <summary>
/// A colour setting edited as hex text with a live swatch and preset swatches. In optional mode an empty value is
/// valid and means "use the default" (the view shows the default colour and offers a button to go back to it).
/// </summary>
internal sealed class ColorEditor : ValidatedTextSetting
{
    private readonly SettingsService _settings;
    private readonly Action<AppSettings, ColorPreset> _applyPreset;

    /// <param name="settings">The live settings.</param>
    /// <param name="get">Reads the colour string from settings.</param>
    /// <param name="set">Writes the colour string into a settings draft.</param>
    /// <param name="presets">Swatches offered next to the text box.</param>
    /// <param name="isOptional">When true, an empty value means "use the default".</param>
    /// <param name="applyPreset">Overrides what choosing a preset changes (for example the text colour as well). Defaults to setting only this colour.</param>
    public ColorEditor(SettingsService settings, Func<AppSettings, string> get, Action<AppSettings, string> set,
        IReadOnlyList<ColorPreset> presets, bool isOptional = false, Action<AppSettings, ColorPreset>? applyPreset = null)
        : base(settings, get, set, text => Validate(text, isOptional))
    {
        _settings = settings;
        _applyPreset = applyPreset ?? ((s, preset) => set(s, preset.Hex));
        IsOptional = isOptional;
        Presets = presets.Select(p => new ColorSwatch(p, ColorConversion.ToBrush(p.Hex, default))).ToList();
        ApplyPresetCommand = new RelayCommand(parameter =>
        {
            if (parameter is ColorPreset preset)
            {
                Choose(preset);
            }
        });
        UseDefaultCommand = new RelayCommand(() => Choose(new ColorPreset("Default", "")), () => IsOptional);
    }

    public bool IsOptional { get; }

    public IReadOnlyList<ColorSwatch> Presets { get; }

    /// <summary>Applies a preset swatch (parameter: a <see cref="ColorPreset"/>).</summary>
    public ICommand ApplyPresetCommand { get; }

    /// <summary>Goes back to the default colour (optional editors only).</summary>
    public ICommand UseDefaultCommand { get; }

    /// <summary>True when the optional colour is unset and the default is in effect.</summary>
    public bool IsDefault => IsOptional && Normalized.Length == 0 && IsValid;

    /// <summary>The colour of the live swatch; null while the optional value is unset or the text is not a colour.</summary>
    public Brush? Swatch => HexColor.TryParse(Normalized, out var color) ? ColorConversion.ToBrush(color) : null;

    private void Choose(ColorPreset preset)
    {
        _settings.Update(s => _applyPreset(s, preset));
        Reload();
    }

    private static TextValidation Validate(string text, bool isOptional)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return isOptional ? TextValidation.Valid("") : TextValidation.Invalid("Enter a colour such as #3584E4.");
        }

        return HexColor.TryParse(text, out var color)
            ? TextValidation.Valid(color.ToString())
            : TextValidation.Invalid("Enter a colour such as #3584E4.");
    }
}
