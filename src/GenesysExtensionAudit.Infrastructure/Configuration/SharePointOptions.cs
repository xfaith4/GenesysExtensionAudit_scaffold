namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Binds to the "SharePoint" configuration section.
/// Configures the Microsoft Graph upload destination.
/// All four required fields must be non-empty for upload to be active.
/// </summary>
public sealed class SharePointOptions
{
    /// <summary>Azure AD tenant ID (GUID or domain).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Azure AD application (client) ID registered with Sites.ReadWrite.All permission.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret for the above application registration.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Full URL of the SharePoint site, e.g. https://tenant.sharepoint.com/sites/YourSite</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Server-relative folder path within the drive, e.g. "Shared Documents/GenesysAudit"</summary>
    public string FolderPath { get; set; } = "Shared Documents/GenesysAudit";

    /// <summary>True when all required credentials and site URL are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(SiteUrl);
}
