using System.Text.RegularExpressions;

namespace GenesysExtensionAudit.Infrastructure.Configuration;

public static class ScheduledAuditCommandLine
{
    public static string BuildTaskRunCommand(string runnerExePath, string profilePath)
    {
        if (string.IsNullOrWhiteSpace(runnerExePath))
            throw new ArgumentException("Runner executable path is required.", nameof(runnerExePath));
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("Schedule profile path is required.", nameof(profilePath));

        var runnerDir = Path.GetDirectoryName(runnerExePath)
            ?? throw new InvalidOperationException("Runner executable has no parent directory.");

        // Ensure scheduled runs execute from the runner folder instead of System32.
        return $"cmd.exe /c \"cd /d \"\"{runnerDir}\"\" && \"\"{runnerExePath}\"\" --schedule-profile \"\"{profilePath}\"\"\"";
    }

    public static string? TryExtractProfilePath(string? taskRunCommand)
    {
        if (string.IsNullOrWhiteSpace(taskRunCommand))
            return null;

        var match = Regex.Match(
            taskRunCommand,
            "--schedule-profile\\s+\"\"(?<path>[^\"]+)\"\"|--schedule-profile\\s+\"(?<path2>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
            return null;

        var raw = match.Groups["path"].Success
            ? match.Groups["path"].Value
            : match.Groups["path2"].Value;

        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
