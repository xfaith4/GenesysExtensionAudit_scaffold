namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// Pushes audit report files to a GitHub repository via the Contents REST API.
/// </summary>
public interface IGitHubUploadService
{
    /// <summary>True when all required GitHub credentials are present in configuration.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Creates or updates <paramref name="fileName"/> inside the configured
    /// repository folder and returns the HTML URL of the committed file.
    /// </summary>
    Task<string> UploadAsync(string fileName, byte[] content, CancellationToken ct);
}
