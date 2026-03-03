namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Binds to the "Export" configuration section.
/// Controls local file output for the headless runner.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>Directory where XLSX reports are written. Relative paths resolved from CWD.</summary>
    public string OutputDirectory { get; set; } = "reports";

    /// <summary>Prefix applied to each generated filename before the timestamp.</summary>
    public string FilePrefix { get; set; } = "GenesysAudit";
}
