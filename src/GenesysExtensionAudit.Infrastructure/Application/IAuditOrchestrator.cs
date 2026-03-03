using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Application;

/// <summary>
/// Orchestrates a complete multi-category Genesys Cloud audit.
/// Returns an <see cref="AuditReportData"/> containing findings from all checks.
/// </summary>
public interface IAuditOrchestrator
{
    Task<AuditReportData> RunAsync(
        AuditRunOptions options,
        IProgress<AuditProgress> progress,
        CancellationToken ct);
}
