// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Json/NullableDateTimeOffsetConverter.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Json;

/// <summary>
/// Tolerant converter for <see cref="DateTimeOffset?"/> that silently maps an
/// empty or whitespace-only string (as returned by the Genesys Cloud API for
/// users who have never issued an OAuth token) to <see langword="null"/> instead
/// of throwing a deserialization exception.
/// </summary>
internal sealed class NullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (DateTimeOffset.TryParse(raw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;

            // Unrecognized format — treat as absent rather than crashing.
            return null;
        }

        // Fallback: let the built-in converter handle any other token type.
        return reader.GetDateTimeOffset();
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset? value,
        JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}
