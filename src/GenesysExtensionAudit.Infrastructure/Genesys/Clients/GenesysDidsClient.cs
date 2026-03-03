using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysDidsClient
{
    Task<PagedResult<DidDto>> GetDidsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
    Task<PagedResult<DidPoolDto>> GetDidPoolsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysDidsClient : GenesysCloudApiClient, IGenesysDidsClient
{
    public GenesysDidsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysDidsClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<DidDto>> GetDidsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges/dids?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<DidsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<DidDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }

    public async Task<PagedResult<DidPoolDto>> GetDidPoolsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges/didpools?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<DidPoolsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<DidPoolDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
