namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

// Response wrapper for GET /api/v2/telephony/providers/edges/dids
public sealed class DidsPageDto
{
    public List<DidDto>? Entities { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public int? PageCount { get; set; }
    public int? Total { get; set; }
}

public sealed class DidDto
{
    public string? Id { get; set; }

    // The actual phone number (E.164 or display format)
    public string? PhoneNumber { get; set; }

    // Who this DID is assigned to (can be null for unassigned)
    public DidOwnerDto? Owner { get; set; }

    // The DID pool this number belongs to
    public DidPoolRefDto? DidPool { get; set; }
}

public sealed class DidOwnerDto
{
    // "User", "Phone", "Station", etc.
    public string? Type { get; set; }
    public string? Id { get; set; }
}

public sealed class DidPoolRefDto
{
    public string? Id { get; set; }
}

// Response wrapper for GET /api/v2/telephony/providers/edges/didpools
public sealed class DidPoolsPageDto
{
    public List<DidPoolDto>? Entities { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public int? PageCount { get; set; }
    public int? Total { get; set; }
}

public sealed class DidPoolDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? StartPhoneNumber { get; set; }
    public string? EndPhoneNumber { get; set; }
    public string? Description { get; set; }
}
