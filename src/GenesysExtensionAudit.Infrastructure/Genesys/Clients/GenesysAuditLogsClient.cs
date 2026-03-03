using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysAuditLogsClient
{
    Task<IReadOnlyList<string>> GetServiceMappingsAsync(CancellationToken ct);
    Task<string> SubmitAuditQueryAsync(AuditLogsSubmitRequestDto request, CancellationToken ct);
    Task<AuditQueryStatusDto> GetAuditQueryStatusAsync(string transactionId, CancellationToken ct);
    Task<AuditLogsResultsPageDto> GetAuditQueryResultsPageAsync(string transactionId, string? nextUri, CancellationToken ct);
}

public sealed class GenesysAuditLogsClient : GenesysCloudApiClient, IGenesysAuditLogsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public GenesysAuditLogsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysAuditLogsClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<IReadOnlyList<string>> GetServiceMappingsAsync(CancellationToken ct)
    {
        var mappings = ParseServiceMappings(
            await GetJsonAsync<JsonElement>("/api/v2/audits/query/servicemapping", ct).ConfigureAwait(false));

        if (mappings.Count > 0)
            return mappings;

        // Fallback for tenants where service mapping shape is sparse but action catalog is available.
        try
        {
            var catalog = await GetJsonAsync<JsonElement>("/api/v2/audits/query/actioncatalog", ct).ConfigureAwait(false);
            var catalogMappings = ParseServiceMappings(catalog);
            if (catalogMappings.Count > 0)
                return catalogMappings;
        }
        catch
        {
            // Keep behavior non-fatal for the dropdown refresh path.
        }

        return mappings;
    }

    public async Task<string> SubmitAuditQueryAsync(AuditLogsSubmitRequestDto request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiUri("/api/v2/audits/query"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var json = await SendJsonAsync<JsonElement>(message, ct).ConfigureAwait(false);
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("transactionId", out var tx) &&
            tx.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(tx.GetString()))
        {
            return tx.GetString()!;
        }

        throw new InvalidOperationException("Audit query submit response did not contain transactionId.");
    }

    public Task<AuditQueryStatusDto> GetAuditQueryStatusAsync(string transactionId, CancellationToken ct)
    {
        var path = $"/api/v2/audits/query/{transactionId}";
        return GetJsonAsync<AuditQueryStatusDto>(path, ct);
    }

    public async Task<AuditLogsResultsPageDto> GetAuditQueryResultsPageAsync(
        string transactionId,
        string? nextUri,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(nextUri))
        {
            if (Uri.TryCreate(nextUri, UriKind.Absolute, out var absolute))
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, absolute);
                msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return await SendJsonAsync<AuditLogsResultsPageDto>(msg, ct).ConfigureAwait(false);
            }

            return await GetJsonAsync<AuditLogsResultsPageDto>(nextUri, ct).ConfigureAwait(false);
        }

        var path = $"/api/v2/audits/query/{transactionId}/results";
        return await GetJsonAsync<AuditLogsResultsPageDto>(path, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ParseServiceMappings(JsonElement json)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectMappings(values, json, null);
        var list = values.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static void CollectMappings(HashSet<string> values, JsonElement element, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value.Trim());
                return;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectMappings(values, item, null);
                return;
            case JsonValueKind.Object:
                break;
            default:
                return;
        }

        if (TryAddFromKnownFields(values, element))
            return;

        foreach (var prop in element.EnumerateObject())
        {
            // Some responses use { "<serviceName>": [...] } dictionaries.
            if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                if (!string.IsNullOrWhiteSpace(prop.Name) &&
                    !IsContainerPropertyName(prop.Name))
                {
                    values.Add(prop.Name.Trim());
                }

                CollectMappings(values, prop.Value, prop.Name);
                continue;
            }

            CollectMappings(values, prop.Value, prop.Name);
        }

        if (!string.IsNullOrWhiteSpace(propertyName) && !IsContainerPropertyName(propertyName!))
            values.Add(propertyName!.Trim());
    }

    private static bool TryAddFromKnownFields(HashSet<string> values, JsonElement element)
    {
        var added = false;

        if (element.TryGetProperty("serviceName", out var serviceName) && serviceName.ValueKind == JsonValueKind.String)
        {
            var value = serviceName.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
                added = true;
            }
        }

        if (element.TryGetProperty("entityType", out var entityType) && entityType.ValueKind == JsonValueKind.String)
        {
            var value = entityType.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
                added = true;
            }
        }

        return added;
    }

    private static bool IsContainerPropertyName(string name)
        => name.Equals("entities", StringComparison.OrdinalIgnoreCase)
           || name.Equals("results", StringComparison.OrdinalIgnoreCase)
           || name.Equals("items", StringComparison.OrdinalIgnoreCase)
           || name.Equals("actions", StringComparison.OrdinalIgnoreCase)
           || name.Equals("selfUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("nextUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("previousUri", StringComparison.OrdinalIgnoreCase);
}
}
