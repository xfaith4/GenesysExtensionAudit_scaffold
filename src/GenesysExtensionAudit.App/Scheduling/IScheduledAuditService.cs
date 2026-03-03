namespace GenesysExtensionAudit.Scheduling;

public interface IScheduledAuditService
{
    Task<IReadOnlyList<ScheduledTaskInfo>> ListAsync(CancellationToken ct);
    Task<ScheduledTaskInfo> CreateAsync(ScheduledAuditDefinition definition, CancellationToken ct);
    Task DeleteAsync(ScheduledTaskInfo task, CancellationToken ct);
}
