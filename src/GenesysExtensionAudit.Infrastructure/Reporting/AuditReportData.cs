using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

// ─── Findings ───────────────────────────────────────────────────────────────

public sealed record GroupFinding(
    string GroupId,
    string? GroupName,
    string? Type,
    string? State,
    int MemberCount,
    DateTime? DateModified,
    string Issue);

public sealed record QueueFinding(
    string QueueId,
    string? QueueName,
    string? Description,
    int MemberCount,
    string Issue);

public sealed record FlowFinding(
    string FlowId,
    string? FlowName,
    string? FlowType,
    bool IsPublished,
    DateTime? PublishedDate,
    DateTime? DateModified,
    int? DaysSincePublished,
    string Issue);

public sealed record InactiveUserFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    DateTimeOffset? TokenLastIssuedDate,
    int? DaysSinceLogin,
    string Issue);

public sealed record NoLocationUserFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    int LocationCount,
    string Issue);

public sealed record DidFinding(
    string DidId,
    string? PhoneNumber,
    string? PoolId,
    string? OwnerType,
    string? OwnerId,
    string? OwnerName,
    string Issue);

public sealed record AuditLogFinding(
    string? AuditId,
    DateTimeOffset? TimestampUtc,
    string? ServiceName,
    string? Action,
    string? UserName,
    string? UserEmail,
    string? EntityType,
    string? EntityName);

public sealed record OperationalEventFinding(
    DateTimeOffset? TimestampUtc,
    string? EventDefinitionId,
    string? EventDefinitionName,
    string? EntityId,
    string? EntityName,
    string? CurrentValue,
    string? PreviousValue,
    string? ErrorCode,
    string? ConversationId);

public sealed record OutboundEventFinding(
    DateTimeOffset? TimestampUtc,
    string? EventId,
    string? Name,
    string? Category,
    string? Level,
    string? Code,
    string? Message,
    string? CorrelationId);

// ─── Combined report ─────────────────────────────────────────────────────────

/// <summary>
/// All findings from a complete audit run, ready for Excel export.
/// </summary>
public sealed class AuditReportData
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset RunStartedAtUtc { get; init; }
    public DateTimeOffset RunCompletedAtUtc { get; init; }
    public string OrgRegion { get; init; } = string.Empty;
    public AuditRunOptions Options { get; init; } = new();

    // Extension audit (existing engine)
    public AuditEngine.AuditReport ExtensionReport { get; init; } = new();

    // New checks
    public IReadOnlyList<GroupFinding> GroupFindings { get; init; } = [];
    public IReadOnlyList<QueueFinding> QueueFindings { get; init; } = [];
    public IReadOnlyList<FlowFinding> FlowFindings { get; init; } = [];
    public IReadOnlyList<InactiveUserFinding> InactiveUserFindings { get; init; } = [];
    public IReadOnlyList<NoLocationUserFinding> NoLocationUserFindings { get; init; } = [];
    public IReadOnlyList<DidFinding> DidFindings { get; init; } = [];
    public IReadOnlyList<AuditLogFinding> AuditLogFindings { get; init; } = [];
    public IReadOnlyList<OperationalEventFinding> OperationalEventFindings { get; init; } = [];
    public IReadOnlyList<OutboundEventFinding> OutboundEventFindings { get; init; } = [];
}
