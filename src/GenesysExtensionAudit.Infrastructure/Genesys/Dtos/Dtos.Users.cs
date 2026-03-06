// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/Dtos.Users.cs
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Genesys.Json;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/users
/// </summary>
public sealed class UsersPageDto
{
    [JsonPropertyName("entities")]
    public List<GenesysUserDto>? Entities { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

/// <summary>
/// Minimal user shape needed for extension audit.
/// </summary>
public sealed class GenesysUserDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// Typically "active" or "inactive" when requested via /api/v2/users?state=...
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>
    /// Genesys commonly returns primaryContactInfo as an array.
    /// We use this to try to find a work phone extension.
    /// </summary>
    [JsonPropertyName("primaryContactInfo")]
    public List<GenesysPrimaryContactInfoDto>? PrimaryContactInfo { get; init; }

    /// <summary>
    /// Additional contact list used by some orgs for Work Phone 2/3 style fields.
    /// </summary>
    [JsonPropertyName("addresses")]
    public List<GenesysPrimaryContactInfoDto>? Addresses { get; init; }

    /// <summary>
    /// Date/time of the last OAuth token issued to this user.
    /// Populated when fetching with expand=lasttokenissued.
    /// May be null for service accounts or users who have never logged in via OAuth.
    /// The Genesys API may return an empty string instead of null for users
    /// with no token history; the converter handles this gracefully.
    /// </summary>
    [JsonPropertyName("lasttokenissued")]
    [JsonConverter(typeof(NullableDateTimeOffsetConverter))]
    public DateTimeOffset? TokenLastIssuedDate { get; init; }

    /// <summary>
    /// Backward-compatible mapping for tenants still returning tokenLastIssuedDate.
    /// </summary>
    [JsonPropertyName("tokenLastIssuedDate")]
    [JsonConverter(typeof(NullableDateTimeOffsetConverter))]
    public DateTimeOffset? TokenLastIssuedDateLegacy { get; init; }

    /// <summary>
    /// Populated when fetching with expand=locations.
    /// </summary>
    [JsonPropertyName("locations")]
    public List<GenesysLocationRefDto>? Locations { get; init; }

    /// <summary>
    /// Populated when fetching with expand=station.
    /// </summary>
    [JsonPropertyName("station")]
    public GenesysStationRefDto? Station { get; init; }
}

public sealed class GenesysPrimaryContactInfoDto
{
    /// <summary>E.g. "work", "cell", etc.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>E.g. "PHONE".</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; init; }

    /// <summary>Can contain phone number and sometimes extension depending on configuration.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>Some tenants include a dedicated extension field.</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }
}

public sealed class GenesysLocationRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class GenesysStationRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
