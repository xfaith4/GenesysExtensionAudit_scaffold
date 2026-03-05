using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// Pushes audit report files into a GitHub repository's Code section via the
/// GitHub Contents REST API (creates or replaces the file with a new commit).
/// </summary>
public sealed class GitHubUploadService : IGitHubUploadService
{
    private const string GitHubApiBase = "https://api.github.com";
    private const string ApiVersion = "2022-11-28";
    private const string UserAgentProduct = "GenesysCloudAuditor";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly GitHubOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<GitHubUploadService> _logger;

    public GitHubUploadService(
        IOptions<GitHubOptions> options,
        HttpClient httpClient,
        ILogger<GitHubUploadService> logger)
    {
        _opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConfigureHttpClient();
    }

    /// <inheritdoc/>
    public bool IsConfigured => _opts.IsConfigured;

    /// <inheritdoc/>
    public async Task<string> UploadAsync(string fileName, byte[] content, CancellationToken ct)
    {
        if (!_opts.IsConfigured)
            throw new InvalidOperationException(
                "GitHub push is not configured. Set GitHub:Token, GitHub:Owner, and GitHub:Repository in appsettings.json.");

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var filePath = BuildFilePath(fileName);
        var url = $"{GitHubApiBase}/repos/{Uri.EscapeDataString(_opts.Owner)}/{Uri.EscapeDataString(_opts.Repository)}/contents/{filePath}";

        _logger.LogDebug("Checking for existing file at GitHub path: {FilePath}", filePath);
        var existingSha = await GetExistingFileShaAsync(url, ct).ConfigureAwait(false);

        var message = _opts.CommitMessage.Replace("{fileName}", fileName, StringComparison.OrdinalIgnoreCase);
        var requestBody = new PutContentsRequest
        {
            Message = message,
            Content = Convert.ToBase64String(content),
            Branch = string.IsNullOrWhiteSpace(_opts.Branch) ? "main" : _opts.Branch,
            Sha = existingSha
        };

        _logger.LogInformation(
            "Pushing {Bytes:N0} bytes to GitHub: {Owner}/{Repo}/{FilePath} (branch: {Branch})",
            content.Length, _opts.Owner, _opts.Repository, filePath, requestBody.Branch);

        using var response = await _http
            .PutAsJsonAsync(url, requestBody, JsonOpts, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        // Build the unescaped folder/file path for the fallback URL to avoid double-encoding.
        var folder = (_opts.FolderPath ?? string.Empty).Trim('/');
        var unescapedFilePath = string.IsNullOrWhiteSpace(folder) ? fileName : $"{folder}/{fileName}";
        var branch = requestBody.Branch;

        var htmlUrl = doc.RootElement
            .TryGetProperty("content", out var contentEl) &&
            contentEl.TryGetProperty("html_url", out var htmlUrlEl)
            ? htmlUrlEl.GetString() ?? string.Empty
            : $"https://github.com/{_opts.Owner}/{_opts.Repository}/blob/{branch}/{unescapedFilePath}";

        _logger.LogInformation("GitHub push complete: {Url}", htmlUrl);
        return htmlUrl;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ConfigureHttpClient()
    {
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(GitHubApiBase);

        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(UserAgentProduct, "1.0"));

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        _http.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);

        if (!string.IsNullOrWhiteSpace(_opts.Token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opts.Token);
        }
    }

    /// <summary>Returns the blob SHA of an existing file, or null if it does not exist.</summary>
    private async Task<string?> GetExistingFileShaAsync(string url, CancellationToken ct)
    {
        var branch = string.IsNullOrWhiteSpace(_opts.Branch) ? "main" : _opts.Branch;
        var requestUri = $"{url}?ref={Uri.EscapeDataString(branch)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        return doc.RootElement.TryGetProperty("sha", out var sha)
            ? sha.GetString()
            : null;
    }

    private string BuildFilePath(string fileName)
    {
        var folder = (_opts.FolderPath ?? string.Empty).Trim('/');
        return string.IsNullOrWhiteSpace(folder)
            ? Uri.EscapeDataString(fileName)
            : $"{string.Join('/', folder.Split('/').Select(Uri.EscapeDataString))}/{Uri.EscapeDataString(fileName)}";
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class PutContentsRequest
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("branch")]
        public string Branch { get; init; } = "main";

        /// <summary>Required only when updating an existing file.</summary>
        [JsonPropertyName("sha")]
        public string? Sha { get; init; }
    }
}
