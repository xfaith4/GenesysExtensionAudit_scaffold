using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Services;

public interface IAuditLogCatalogCache
{
    Task<IReadOnlyList<string>> GetOrRefreshAsync(bool forceRefresh, CancellationToken ct);
    Task WarmAsync(CancellationToken ct);
}

public sealed class AuditLogCatalogCache : IAuditLogCatalogCache
{
    private readonly IGenesysAuditLogsClient _auditLogsClient;
    private readonly ILogger<AuditLogCatalogCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<string> _cached = [];
    private bool _isLoaded;

    public AuditLogCatalogCache(
        IGenesysAuditLogsClient auditLogsClient,
        ILogger<AuditLogCatalogCache> logger)
    {
        _auditLogsClient = auditLogsClient ?? throw new ArgumentNullException(nameof(auditLogsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<string>> GetOrRefreshAsync(bool forceRefresh, CancellationToken ct)
        => LoadCoreAsync(forceRefresh, ct);

    public async Task WarmAsync(CancellationToken ct)
    {
        try
        {
            await LoadCoreAsync(forceRefresh: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Warm-up is best effort; UI refresh still allows explicit retry.
            _logger.LogDebug(ex, "Audit-log catalog warm-up failed.");
        }
    }

    private async Task<IReadOnlyList<string>> LoadCoreAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _isLoaded)
            return _cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _isLoaded)
                return _cached;

            var entities = await _auditLogsClient.GetServiceMappingsAsync(ct).ConfigureAwait(false);
            var ordered = entities
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cached = ordered;
            _isLoaded = true;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
