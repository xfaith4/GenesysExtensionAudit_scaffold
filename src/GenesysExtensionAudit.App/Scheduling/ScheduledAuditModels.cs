namespace GenesysExtensionAudit.Scheduling;

public enum ScheduledRecurrenceType
{
    Once = 0,
    Daily = 1,
    Weekly = 2
}

public sealed class ScheduledAuditDefinition
{
    public string Name { get; set; } = "Scheduled Audit";
    public ScheduledRecurrenceType RecurrenceType { get; set; } = ScheduledRecurrenceType.Once;
    public DateTime StartLocalDateTime { get; set; } = DateTime.Now.AddMinutes(5);
    public IReadOnlyList<DayOfWeek> WeeklyDays { get; set; } = [];

    public string RunAsUserName { get; set; } = Environment.UserName;
    public string RunAsPassword { get; set; } = string.Empty;

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
    public bool RunAuditLogs { get; set; }
    public int AuditLogLookbackHours { get; set; } = 1;
    public string? AuditLogServiceName { get; set; }

    public bool HasAnyAuditSelected =>
        RunExtensionAudit || RunGroupAudit || RunQueueAudit || RunFlowAudit ||
        RunInactiveUserAudit || RunDidAudit || RunAuditLogs;
}

public sealed class ScheduledTaskInfo
{
    public string TaskName { get; init; } = string.Empty;
    public string TaskPath { get; init; } = string.Empty;
    public string NextRunTime { get; init; } = string.Empty;
    public string LastRunTime { get; init; } = string.Empty;
    public string LastResult { get; init; } = string.Empty;
    public string Recurrence { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public string? ProfilePath { get; init; }
}

public sealed class ScheduledAuditOptions
{
    public string TaskFolderPath { get; set; } = "\\GenesysExtensionAudit\\";
    public string TaskNamePrefix { get; set; } = "GenesysExtensionAudit_";
    public string? RunnerExecutablePath { get; set; }
}
