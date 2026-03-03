namespace GenesysExtensionAudit.Application;

/// <summary>
/// Input parameters for a single audit run.
/// Passed from the ViewModel to IAuditOrchestrator.
/// </summary>
public sealed class AuditRunOptions
{
    /// <summary>Records per page when paging Genesys endpoints (1–500).</summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// When false (default): requests /api/v2/users with &amp;state=active.
    /// When true: requests all users (active + inactive).
    /// </summary>
    public bool IncludeInactiveUsers { get; init; }

    /// <summary>
    /// Flows whose last published date is older than this many days are flagged as stale.
    /// Default: 90 days.
    /// </summary>
    public int StaleFlowThresholdDays { get; init; } = 90;

    /// <summary>
    /// Users whose last token-issued date is older than this many days are flagged as inactive.
    /// Default: 90 days.
    /// </summary>
    public int InactiveUserThresholdDays { get; init; } = 90;

    /// <summary>
    /// Run extension consistency checks (profile vs assigned extensions).
    /// </summary>
    public bool RunExtensionAudit { get; init; } = true;

    /// <summary>
    /// Run group checks (empty/single-member groups).
    /// </summary>
    public bool RunGroupAudit { get; init; } = true;

    /// <summary>
    /// Run queue checks (empty queues / duplicate names).
    /// </summary>
    public bool RunQueueAudit { get; init; } = true;

    /// <summary>
    /// Run flow checks (stale/unpublished architect flows).
    /// </summary>
    public bool RunFlowAudit { get; init; } = true;

    /// <summary>
    /// Run inactive-user checks based on last login/token issue date.
    /// </summary>
    public bool RunInactiveUserAudit { get; init; } = true;

    /// <summary>
    /// Run DID checks (unassigned/orphaned/inactive-owner/mismatch).
    /// </summary>
    public bool RunDidAudit { get; init; } = true;

    /// <summary>
    /// Run audit logs transaction flow (service mapping -> submit -> poll -> results).
    /// </summary>
    public bool RunAuditLogs { get; init; } = false;

    /// <summary>
    /// Lookback window used for audit logs query interval.
    /// Default aligns with Genesys.Core behavior.
    /// </summary>
    public int AuditLogLookbackHours { get; init; } = 1;

    /// <summary>
    /// Optional audit-log service names to include in the query.
    /// Empty means "all catalog entities" from service mapping.
    /// </summary>
    public IReadOnlyList<string> AuditLogServiceNames { get; init; } = [];
}
