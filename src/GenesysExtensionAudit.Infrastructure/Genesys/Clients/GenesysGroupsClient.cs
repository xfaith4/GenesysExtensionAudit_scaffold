using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysGroupsClient
{
    Task<PagedResult<GroupDto>> GetGroupsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysGroupsClient : GenesysCloudApiClient, IGenesysGroupsClient
{
    public GenesysGroupsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysGroupsClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<GroupDto>> GetGroupsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/groups?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<GroupsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<GroupDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
