using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;

namespace GenesysExtensionAudit.Scheduling;

public sealed class ScheduledAuditService : IScheduledAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ScheduledAuditOptions _options;

    public ScheduledAuditService(IOptions<ScheduledAuditOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ScheduledTaskInfo> CreateAsync(ScheduledAuditDefinition definition, CancellationToken ct)
    {
        ValidateDefinition(definition);

        var runnerExePath = ResolveRunnerExecutablePath();
        var profileDir = ResolveProfileDirectory();

        Directory.CreateDirectory(profileDir);

        var scheduleId = Guid.NewGuid().ToString("N");
        var taskName = $"{_options.TaskNamePrefix}{scheduleId}";
        var fullTaskPath = $"{NormalizeTaskFolderPath()}{taskName}";
        var profilePath = Path.Combine(profileDir, $"{scheduleId}.json");

        var profile = new ScheduledAuditProfile
        {
            ScheduleId = scheduleId,
            Name = definition.Name.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = definition.RunAsUserName,
            PageSize = Math.Clamp(definition.PageSize, 1, 500),
            IncludeInactiveUsers = definition.IncludeInactiveUsers,
            StaleFlowThresholdDays = Math.Max(1, definition.StaleFlowThresholdDays),
            InactiveUserThresholdDays = Math.Max(1, definition.InactiveUserThresholdDays),
            RunExtensionAudit = definition.RunExtensionAudit,
            RunGroupAudit = definition.RunGroupAudit,
            RunQueueAudit = definition.RunQueueAudit,
            RunFlowAudit = definition.RunFlowAudit,
            RunInactiveUserAudit = definition.RunInactiveUserAudit,
            RunDidAudit = definition.RunDidAudit,
            RunAuditLogs = definition.RunAuditLogs,
            AuditLogLookbackHours = Math.Max(1, definition.AuditLogLookbackHours),
            AuditLogServiceName = string.IsNullOrWhiteSpace(definition.AuditLogServiceName)
                ? null
                : definition.AuditLogServiceName!.Trim()
        };

        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile, JsonOptions), ct).ConfigureAwait(false);

        try
        {
            var runCommand = ScheduledAuditCommandLine.BuildTaskRunCommand(runnerExePath, profilePath);
            var args = BuildCreateTaskArguments(definition, fullTaskPath, runCommand, profilePath);
            await RunSchtasksAsync(args, ct).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(profilePath); } catch { /* best effort rollback */ }
            throw;
        }

        var items = await ListAsync(ct).ConfigureAwait(false);
        var created = items.FirstOrDefault(i => string.Equals(i.TaskPath, fullTaskPath, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
            return created;

        return new ScheduledTaskInfo
        {
            TaskName = taskName,
            TaskPath = fullTaskPath,
            NextRunTime = "Pending scheduler refresh",
            LastRunTime = "N/A",
            LastResult = "N/A",
            Recurrence = definition.RecurrenceType.ToString(),
            IsEnabled = true,
            ProfilePath = profilePath
        };
    }

    public async Task<IReadOnlyList<ScheduledTaskInfo>> ListAsync(CancellationToken ct)
    {
        var output = await RunSchtasksAsync(
            ["/Query", "/FO", "CSV", "/V"],
            ct,
            allowNonZeroExit: true).ConfigureAwait(false);

        var rows = ParseCsv(output.StandardOutput);
        if (rows.Count == 0)
            return [];

        var results = new List<ScheduledTaskInfo>();
        var folderPrefix = NormalizeTaskFolderPath();
        foreach (var row in rows)
        {
            var taskPath = GetValue(row, "TaskName");
            if (string.IsNullOrWhiteSpace(taskPath) ||
                !taskPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            var taskToRun = GetValue(row, "Task To Run");

            results.Add(new ScheduledTaskInfo
            {
                TaskName = taskName,
                TaskPath = taskPath,
                NextRunTime = GetValue(row, "Next Run Time"),
                LastRunTime = GetValue(row, "Last Run Time"),
                LastResult = GetValue(row, "Last Result"),
                Recurrence = GetValue(row, "Schedule Type"),
                IsEnabled = !GetValue(row, "Scheduled Task State").Contains("Disabled", StringComparison.OrdinalIgnoreCase),
                ProfilePath = ScheduledAuditCommandLine.TryExtractProfilePath(taskToRun)
            });
        }

        return results.OrderBy(x => x.TaskName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task DeleteAsync(ScheduledTaskInfo task, CancellationToken ct)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        if (string.IsNullOrWhiteSpace(task.TaskPath))
            throw new InvalidOperationException("Task path is required.");

        await RunSchtasksAsync(
            ["/Delete", "/TN", task.TaskPath, "/F"],
            ct,
            allowNonZeroExit: false).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(task.ProfilePath) && File.Exists(task.ProfilePath))
        {
            try
            {
                File.Delete(task.ProfilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Task deleted but profile file could not be deleted: {task.ProfilePath}. {ex.Message}");
            }
        }
    }

    private static void ValidateDefinition(ScheduledAuditDefinition definition)
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));

        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new InvalidOperationException("Schedule name is required.");

        if (string.IsNullOrWhiteSpace(definition.RunAsUserName))
            throw new InvalidOperationException("Run-as username is required.");

        if (string.IsNullOrWhiteSpace(definition.RunAsPassword))
            throw new InvalidOperationException("Run-as password is required.");

        if (!definition.HasAnyAuditSelected)
            throw new InvalidOperationException("Select at least one audit path.");

        if (definition.RecurrenceType == ScheduledRecurrenceType.Once &&
            definition.StartLocalDateTime <= DateTime.Now)
        {
            throw new InvalidOperationException("One-time schedules must be set to a future date/time.");
        }

        if (definition.RecurrenceType == ScheduledRecurrenceType.Weekly &&
            (definition.WeeklyDays is null || definition.WeeklyDays.Count == 0))
        {
            throw new InvalidOperationException("Select at least one weekday for weekly schedules.");
        }
    }

    private string ResolveProfileDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var preferred = Path.Combine(programData, "GenesysExtensionAudit", "Schedules");

        try
        {
            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "GenesysExtensionAudit", "Schedules");
        }
    }

    private string ResolveRunnerExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.RunnerExecutablePath) && File.Exists(_options.RunnerExecutablePath))
            return Path.GetFullPath(_options.RunnerExecutablePath);

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "GenesysExtensionAudit.Runner.exe"),
            Path.Combine(baseDir, "runner", "GenesysExtensionAudit.Runner.exe"),
            Path.Combine(baseDir, "..", "GenesysExtensionAudit.Runner.exe")
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "Could not find GenesysExtensionAudit.Runner.exe. Set Scheduling:RunnerExecutablePath in appsettings.json.");
    }

    private string[] BuildCreateTaskArguments(
        ScheduledAuditDefinition definition,
        string fullTaskPath,
        string runCommand,
        string profilePath)
    {
        var args = new List<string>
        {
            "/Create",
            "/TN", fullTaskPath,
            "/TR", runCommand,
            "/RU", definition.RunAsUserName,
            "/RP", definition.RunAsPassword,
            "/RL", "LIMITED",
            "/F"
        };

        var date = definition.StartLocalDateTime.ToString("MM/dd/yyyy");
        var time = definition.StartLocalDateTime.ToString("HH:mm");

        switch (definition.RecurrenceType)
        {
            case ScheduledRecurrenceType.Once:
                args.AddRange(["/SC", "ONCE", "/SD", date, "/ST", time]);
                break;
            case ScheduledRecurrenceType.Daily:
                args.AddRange(["/SC", "DAILY", "/SD", date, "/ST", time]);
                break;
            case ScheduledRecurrenceType.Weekly:
                var weeklyDays = string.Join(",",
                    definition.WeeklyDays
                        .Distinct()
                        .OrderBy(d => d)
                        .Select(ToTaskSchedulerWeekday));
                args.AddRange(["/SC", "WEEKLY", "/D", weeklyDays, "/SD", date, "/ST", time]);
                break;
            default:
                throw new InvalidOperationException($"Unsupported recurrence: {definition.RecurrenceType}");
        }

        var enabledAudits = new List<string>();
        if (definition.RunExtensionAudit) enabledAudits.Add("Extensions");
        if (definition.RunGroupAudit) enabledAudits.Add("Groups");
        if (definition.RunQueueAudit) enabledAudits.Add("Queues");
        if (definition.RunFlowAudit) enabledAudits.Add("Flows");
        if (definition.RunInactiveUserAudit) enabledAudits.Add("InactiveUsers");
        if (definition.RunDidAudit) enabledAudits.Add("DIDs");
        if (definition.RunAuditLogs) enabledAudits.Add("AuditLogs");

        return args.ToArray();
    }

    private string NormalizeTaskFolderPath()
    {
        var folder = _options.TaskFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
            folder = "\\GenesysExtensionAudit\\";
        if (!folder.StartsWith('\\'))
            folder = "\\" + folder;
        if (!folder.EndsWith('\\'))
            folder += "\\";
        return folder;
    }

    private static string ToTaskSchedulerWeekday(DayOfWeek day)
        => day switch
        {
            DayOfWeek.Sunday => "SUN",
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null)
        };

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunSchtasksAsync(
        IReadOnlyList<string> args,
        CancellationToken ct,
        bool allowNonZeroExit = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!allowNonZeroExit && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"schtasks failed with exit code {process.ExitCode}. STDOUT: {stdout} STDERR: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(csv))
            return rows;

        using var reader = new StringReader(csv);
        using var parser = new TextFieldParser(reader)
        {
            Delimiters = [","],
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };

        if (parser.EndOfData)
            return rows;

        var headers = parser.ReadFields() ?? [];
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.Length == 0)
                continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var key = headers[i];
                var value = i < fields.Length ? fields[i] : string.Empty;
                row[key] = value;
            }
            rows.Add(row);
        }

        return rows;
    }

    private static string GetValue(Dictionary<string, string> row, string key)
    {
        if (row.TryGetValue(key, out var value))
            return value ?? string.Empty;

        // Gracefully handle localized/variant headers by checking a compact match.
        var normalized = key.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var kvp in row)
        {
            var candidate = kvp.Key.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                return kvp.Value ?? string.Empty;
        }

        return string.Empty;
    }
}
