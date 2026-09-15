using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubastaYa.API.Serialization;

public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles InvariantDecimalStyles =
        NumberStyles.AllowLeadingSign |
        NumberStyles.AllowDecimalPoint;

    public override decimal Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(
                reader.GetString(),
                InvariantDecimalStyles,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue;
        }

        throw new JsonException(
            "Se esperaba un decimal JSON numérico o una cadena decimal invariante.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        decimal value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
