namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Binds to the "GitHub" configuration section.
/// Configures the GitHub repository push destination for audit reports.
/// Token, Owner, and Repository must all be non-empty for upload to be active.
/// </summary>
public sealed class GitHubOptions
{
    /// <summary>
    /// Personal Access Token (classic) or fine-grained PAT with
    /// Contents read/write permission on the target repository.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>GitHub user or organisation that owns the repository.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Target repository name (without owner prefix).</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Branch to commit the file to. Defaults to "main".</summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    /// Repository-relative folder path where reports are stored,
    /// e.g. "audit-reports". Defaults to "audit-reports".
    /// </summary>
    public string FolderPath { get; set; } = "audit-reports";

    /// <summary>Commit message used when pushing the report. Supports a {fileName} placeholder.</summary>
    public string CommitMessage { get; set; } = "chore: add audit report {fileName}";

    /// <summary>True when all required fields are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) &&
        !string.IsNullOrWhiteSpace(Owner) &&
        !string.IsNullOrWhiteSpace(Repository);
}
