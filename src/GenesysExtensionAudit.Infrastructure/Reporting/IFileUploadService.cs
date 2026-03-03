namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// Uploads audit report files to a remote destination (e.g. SharePoint/OneDrive).
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Uploads the given byte array as a file with the given name.
    /// Returns the URL or path of the uploaded file.
    /// </summary>
    Task<string> UploadAsync(string fileName, byte[] content, CancellationToken ct);
}
