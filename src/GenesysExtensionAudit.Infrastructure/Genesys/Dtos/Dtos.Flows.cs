namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

// Response wrapper for GET /api/v2/flows
public sealed class FlowsPageDto
{
    public List<FlowDto>? Entities { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public int? PageCount { get; set; }
    public int? Total { get; set; }
}

public sealed class FlowDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }

    // "INBOUNDCALL", "OUTBOUNDCALL", "INBOUNDSHORTMESSAGE", "INBOUNDEMAIL", etc.
    public string? Type { get; set; }

    public bool? Active { get; set; }
    public bool? Deleted { get; set; }

    public DateTime? DateCreated { get; set; }
    public DateTime? DateModified { get; set; }

    // Nested: the currently-published version metadata
    public FlowPublishedVersionDto? PublishedVersion { get; set; }
}

public sealed class FlowPublishedVersionDto
{
    public string? Id { get; set; }
    public DateTime? PublishedDate { get; set; }
}
