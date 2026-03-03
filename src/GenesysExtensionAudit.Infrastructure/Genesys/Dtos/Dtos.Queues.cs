namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

// Response wrapper for GET /api/v2/routing/queues
public sealed class QueuesPageDto
{
    public List<QueueDto>? Entities { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public int? PageCount { get; set; }
    public int? Total { get; set; }
}

public sealed class QueueDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? MemberCount { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateModified { get; set; }
}
