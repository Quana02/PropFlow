using System.Text.Json;
using System.Text.Json.Serialization;

namespace PropFlow.Api.Serialization;

/// <summary>
/// Accepts either an enum name or its numeric representation while preserving
/// the existing numeric response contract.
/// </summary>
public sealed class FlexibleEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(FlexibleEnumJsonConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class FlexibleEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                    return parsed;
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var numeric))
            {
                var parsed = (TEnum)Enum.ToObject(typeof(TEnum), numeric);
                if (Enum.IsDefined(parsed)) return parsed;
            }

            throw new JsonException($"Giá trị không hợp lệ cho {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Convert.ToInt64(value));
    }
}
