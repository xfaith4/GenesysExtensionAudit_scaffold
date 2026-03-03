using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysFlowsClient
{
    Task<PagedResult<FlowDto>> GetFlowsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysFlowsClient : GenesysCloudApiClient, IGenesysFlowsClient
{
    public GenesysFlowsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysFlowsClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<FlowDto>> GetFlowsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        // include=publishedVersion surfaces publishedDate in the response
        var path = $"/api/v2/flows?pageSize={pageSize}&pageNumber={pageNumber}&include=publishedVersion";
        var dto = await GetJsonAsync<FlowsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<FlowDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
