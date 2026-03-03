using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysUsersClient
{
    Task<PagedResult<GenesysUserDto>> GetUsersPageAsync(
        int pageNumber,
        int pageSize,
        bool includeInactive,
        CancellationToken ct);
}

public sealed class GenesysUsersClient : GenesysCloudApiClient, IGenesysUsersClient
{
    public GenesysUsersClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysUsersClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<PagedResult<GenesysUserDto>> GetUsersPageAsync(
        int pageNumber,
        int pageSize,
        bool includeInactive,
        CancellationToken ct)
    {
        var state = includeInactive ? "" : "&state=active";
        // expand=tokenLastIssuedDate surfaces last-login info for inactive-user checks
        var path = $"/api/v2/users?pageSize={pageSize}&pageNumber={pageNumber}{state}&expand=tokenLastIssuedDate";

        var dto = await GetJsonAsync<UsersPageDto>(path, ct).ConfigureAwait(false);

        return new PagedResult<GenesysUserDto>(
            Items: dto.Entities ?? [],
            PageNumber: dto.PageNumber ?? pageNumber,
            PageSize: dto.PageSize ?? pageSize,
            PageCount: dto.PageCount,
            Total: dto.Total);
    }
}
