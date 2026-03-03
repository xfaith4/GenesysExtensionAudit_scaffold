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
        var json = await GetJsonAsync<JsonElement>("/api/v2/audits/query/servicemapping", ct).ConfigureAwait(false);
        return ParseServiceMappings(json);
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
        var values = new List<string>();

        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in json.EnumerateArray())
                AddMapping(values, el);
            return values;
        }

        if (json.ValueKind != JsonValueKind.Object)
            return values;

        if (json.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in entities.EnumerateArray())
                AddMapping(values, el);
        }
        else if (json.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in results.EnumerateArray())
                AddMapping(values, el);
        }
        else
        {
            foreach (var prop in json.EnumerateObject())
                AddMapping(values, prop.Value);
        }

        return values
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddMapping(List<string> values, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("serviceName", out var serviceName) && serviceName.ValueKind == JsonValueKind.String)
        {
            var value = serviceName.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }
    }
}
