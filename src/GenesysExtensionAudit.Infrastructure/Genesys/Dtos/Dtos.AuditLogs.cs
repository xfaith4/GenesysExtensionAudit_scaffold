using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class AuditLogsSubmitRequestDto
{
    [JsonPropertyName("interval")]
    public string Interval { get; init; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public List<string> ServiceName { get; init; } = [];

    [JsonPropertyName("action")]
    public List<string> Action { get; init; } = [];
}

public sealed class AuditQueryStatusDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }
}

public sealed class AuditLogsResultsPageDto
{
    [JsonPropertyName("results")]
    public List<JsonElement>? Results { get; init; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; init; }

    [JsonPropertyName("totalHits")]
    public int? TotalHits { get; init; }
}
