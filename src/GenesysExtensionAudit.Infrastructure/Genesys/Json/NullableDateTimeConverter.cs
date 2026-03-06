// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Json/NullableDateTimeConverter.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Json;

/// <summary>
/// Tolerant converter for <see cref="DateTime?"/> that silently maps an
/// empty or whitespace-only string (as returned by the Genesys Cloud API for
/// records with no date value) to <see langword="null"/> instead of throwing
/// a deserialization exception.
/// </summary>
internal sealed class NullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(
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

            if (DateTime.TryParse(raw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;

            // Unrecognized format — treat as absent rather than crashing.
            return null;
        }

        // Fallback: let the built-in converter handle any other token type.
        return reader.GetDateTime();
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime? value,
        JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}
