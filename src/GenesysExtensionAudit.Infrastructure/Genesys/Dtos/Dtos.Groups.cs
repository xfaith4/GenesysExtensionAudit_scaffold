namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

// Response wrapper for GET /api/v2/groups
public sealed class GroupsPageDto
{
    public List<GroupDto>? Entities { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public int? PageCount { get; set; }
    public int? Total { get; set; }
}

public sealed class GroupDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public int? MemberCount { get; set; }
    public DateTime? DateModified { get; set; }
}
