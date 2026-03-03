using Azure.Identity;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// Uploads audit report files to a SharePoint document library via Microsoft Graph.
/// Requires an Azure AD app registration with Sites.ReadWrite.All (application permission).
/// </summary>
public sealed class SharePointUploadService : IFileUploadService
{
    private readonly SharePointOptions _opts;
    private readonly ILogger<SharePointUploadService> _logger;

    public SharePointUploadService(
        IOptions<SharePointOptions> options,
        ILogger<SharePointUploadService> logger)
    {
        _opts = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(string fileName, byte[] content, CancellationToken ct)
    {
        if (!_opts.IsConfigured)
            throw new InvalidOperationException(
                "SharePoint upload is not configured. Provide SharePoint:TenantId, ClientId, " +
                "ClientSecret, and SiteUrl in appsettings.json.");

        var credential = new ClientSecretCredential(_opts.TenantId, _opts.ClientId, _opts.ClientSecret);
        var graphClient = new GraphServiceClient(credential);

        // Resolve site — Graph uses hostname:server-relative-path format.
        var uri = new Uri(_opts.SiteUrl);
        var siteKey = $"{uri.Host}:{uri.AbsolutePath}";

        _logger.LogDebug("Resolving SharePoint site: {SiteKey}", siteKey);
        var site = await graphClient.Sites[siteKey].GetAsync(cancellationToken: ct);

        _logger.LogDebug("Resolving default drive for site: {SiteId}", site!.Id);
        var drive = await graphClient.Sites[site.Id].Drive.GetAsync(cancellationToken: ct);

        // Construct the upload path inside the document library.
        var uploadPath = $"{_opts.FolderPath.TrimEnd('/')}/{fileName}";
        _logger.LogInformation("Uploading {Bytes} bytes → drive path: {Path}", content.Length, uploadPath);

        // PutAsync replaces or creates the file (supports up to ~250 MB in a single PUT).
        using var stream = new MemoryStream(content);
        var item = await graphClient
            .Drives[drive!.Id]
            .Root
            .ItemWithPath(uploadPath)
            .Content
            .PutAsync(stream, cancellationToken: ct);

        var url = item?.WebUrl ?? $"{_opts.SiteUrl}/{uploadPath}";
        _logger.LogInformation("Upload complete: {Url}", url);
        return url;
    }
}
