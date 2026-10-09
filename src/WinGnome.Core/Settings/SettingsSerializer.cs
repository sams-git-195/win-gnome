using System.Text.Json;

namespace WinGnome.Core.Settings;

/// <summary>JSON (de)serialisation for <see cref="AppSettings"/> with lenient reading.</summary>
public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LenientEnumConverterFactory() },
    };

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Parses settings JSON and normalises the result. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static AppSettings Deserialize(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalize();
}
