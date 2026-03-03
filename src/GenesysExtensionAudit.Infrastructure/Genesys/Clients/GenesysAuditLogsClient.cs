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
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var standard = await TryGetCatalogAsync("/api/v2/audits/query/servicemapping", ct).ConfigureAwait(false);
        if (standard.HasValue)
            CollectMappings(values, standard.Value, null, allowDictionaryKeyNames: true);

        var realtime = await TryGetCatalogAsync("/api/v2/audits/query/realtime/servicemapping", ct).ConfigureAwait(false);
        if (realtime.HasValue)
            CollectMappings(values, realtime.Value, null, allowDictionaryKeyNames: true);

        if (values.Count == 0)
        {
            // Fallback for tenants where service mapping shape is sparse but action catalog is available.
            var catalog = await TryGetCatalogAsync("/api/v2/audits/query/actioncatalog", ct).ConfigureAwait(false);
            if (catalog.HasValue)
                CollectMappings(values, catalog.Value, null, allowDictionaryKeyNames: true);
        }

        var list = values.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
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

    private async Task<JsonElement?> TryGetCatalogAsync(string path, CancellationToken ct)
    {
        try
        {
            return await GetJsonAsync<JsonElement>(path, ct).ConfigureAwait(false);
        }
        catch (GenesysApiException ex) when ((int)ex.StatusCode == 404 || (int)ex.StatusCode == 400)
        {
            return null;
        }
        catch
        {
            // Keep behavior non-fatal for dropdown population paths.
            return null;
        }
    }

    private static void CollectMappings(
        HashSet<string> values,
        JsonElement element,
        string? parentPropertyName,
        bool allowDictionaryKeyNames)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                if (IsMappingValuePropertyName(parentPropertyName))
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value.Trim());
                }

                return;
            }

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectMappings(values, item, parentPropertyName, allowDictionaryKeyNames);
                return;

            case JsonValueKind.Object:
                break;

            default:
                return;
        }

        TryAddFromKnownFields(values, element);

        foreach (var prop in element.EnumerateObject())
        {
            var propName = prop.Name?.Trim();
            if (string.IsNullOrWhiteSpace(propName))
                continue;

            if (allowDictionaryKeyNames
                && prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                && !IsContainerPropertyName(propName))
            {
                values.Add(propName);
            }

            CollectMappings(values, prop.Value, propName, allowDictionaryKeyNames);
        }
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
           || name.Equals("serviceMappings", StringComparison.OrdinalIgnoreCase)
           || name.Equals("actionMappings", StringComparison.OrdinalIgnoreCase)
           || name.Equals("selfUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("nextUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("previousUri", StringComparison.OrdinalIgnoreCase);

    private static bool IsMappingValuePropertyName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && (name.Equals("serviceName", StringComparison.OrdinalIgnoreCase)
               || name.Equals("serviceNames", StringComparison.OrdinalIgnoreCase)
               || name.Equals("entityType", StringComparison.OrdinalIgnoreCase)
               || name.Equals("entityTypes", StringComparison.OrdinalIgnoreCase));
}
