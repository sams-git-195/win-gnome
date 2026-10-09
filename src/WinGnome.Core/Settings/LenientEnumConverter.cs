using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinGnome.Core.Settings;

/// <summary>
/// Writes enum settings as their names and reads them leniently. A name this build doesn't know (one written by a
/// newer WinGnome, or a typo), an undefined number, or a value of the wrong JSON type becomes an undefined value,
/// which <see cref="AppSettings.Normalize"/> replaces with that setting's own default. A strict reader would throw,
/// and the store would then set the whole file aside as corrupt and reset every setting. Core has no logging, so
/// the fallback is silent.
/// </summary>
internal sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class LenientEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        // No settings enum uses this value, and Normalize resets anything that isn't a named member.
        private static readonly T Undefined = (T)Enum.ToObject(typeof(T), int.MinValue);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    // Names only: Enum.TryParse would also accept "2" or "Left, Right".
                    var name = reader.GetString();
                    foreach (var value in Enum.GetValues<T>())
                    {
                        if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            return value;
                        }
                    }

                    return Undefined;

                case JsonTokenType.Number when reader.TryGetInt32(out var number):
                    return (T)Enum.ToObject(typeof(T), number);

                default:
                    // Objects and arrays: step over their contents so the rest of the file still reads.
                    reader.Skip();
                    return Undefined;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
