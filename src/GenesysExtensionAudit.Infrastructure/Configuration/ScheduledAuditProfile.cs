using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Serialized schedule profile passed from the WPF scheduler UI to the headless runner.
/// </summary>
public sealed class ScheduledAuditProfile
{
    public string ScheduleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public int PageSize { get; set; } = 100;
    public bool IncludeInactiveUsers { get; set; }
    public int StaleFlowThresholdDays { get; set; } = 90;
    public int InactiveUserThresholdDays { get; set; } = 90;

    public bool RunExtensionAudit { get; set; } = true;
    public bool RunGroupAudit { get; set; } = true;
    public bool RunQueueAudit { get; set; } = true;
    public bool RunFlowAudit { get; set; } = true;
    public bool RunInactiveUserAudit { get; set; } = true;
    public bool RunDidAudit { get; set; } = true;
    public bool RunAuditLogs { get; set; } = false;
    public int AuditLogLookbackHours { get; set; } = 1;

    /// <summary>
    /// Optional single service name selected in the scheduler UI.
    /// Null/empty means all entities.
    /// </summary>
    public string? AuditLogServiceName { get; set; }

    [JsonIgnore]
    public bool HasAnyAuditSelected =>
        RunExtensionAudit || RunGroupAudit || RunQueueAudit || RunFlowAudit ||
        RunInactiveUserAudit || RunDidAudit || RunAuditLogs;
}
