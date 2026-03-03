// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/Dtos.Users.cs
using System.Text.Json.Serialization;

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
    /// Date/time of the last OAuth token issued to this user.
    /// Populated when fetching with expand=tokenLastIssuedDate.
    /// May be null for service accounts or users who have never logged in via OAuth.
    /// </summary>
    [JsonPropertyName("tokenLastIssuedDate")]
    public DateTimeOffset? TokenLastIssuedDate { get; init; }
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
