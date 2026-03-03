namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Binds to the "Audit" configuration section.
/// Provides threshold overrides for the headless runner.
/// The WPF app exposes these as VM properties instead.
/// </summary>
public sealed class AuditOptions
{
    /// <summary>Flows whose last published date exceeds this many days are flagged as stale.</summary>
    public int StaleFlowThresholdDays { get; set; } = 90;

    /// <summary>Users whose last token-issued date exceeds this many days are flagged as inactive.</summary>
    public int InactiveUserThresholdDays { get; set; } = 90;

    /// <summary>When true, inactive users are included in the audit scope.</summary>
    public bool IncludeInactiveUsers { get; set; } = false;

    public bool RunExtensionAudit { get; set; } = true;
    public bool RunGroupAudit { get; set; } = true;
    public bool RunQueueAudit { get; set; } = true;
    public bool RunFlowAudit { get; set; } = true;
    public bool RunInactiveUserAudit { get; set; } = true;
    public bool RunDidAudit { get; set; } = true;
    public bool RunAuditLogs { get; set; } = false;
    public int AuditLogLookbackHours { get; set; } = 1;
    public string[] AuditLogServiceNames { get; set; } = [];
}
