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
}
