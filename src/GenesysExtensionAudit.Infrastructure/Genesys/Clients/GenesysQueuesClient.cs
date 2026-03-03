using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysQueuesClient
{
    Task<PagedResult<QueueDto>> GetQueuesPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysQueuesClient : GenesysCloudApiClient, IGenesysQueuesClient
{
    public GenesysQueuesClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysQueuesClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<QueueDto>> GetQueuesPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/routing/queues?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<QueuesPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<QueueDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
