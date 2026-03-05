using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

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
            RunOperationalEventLogs = definition.RunOperationalEventLogs,
            OperationalEventLookbackDays = Math.Max(1, definition.OperationalEventLookbackDays),
            RunOutboundEvents = definition.RunOutboundEvents,
            AuditLogServiceName = string.IsNullOrWhiteSpace(definition.AuditLogServiceName)
                ? null
                : definition.AuditLogServiceName!.Trim(),
            PushToGitHub = definition.PushToGitHub
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
        var json = await QueryScheduledTasksAsJsonAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
            return [];

        var rows = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => [root],
            _ => []
        };

        var results = new List<ScheduledTaskInfo>(rows.Length);
        foreach (var row in rows)
        {
            var taskName = GetJsonString(row, "TaskName");
            var taskPath = GetJsonString(row, "TaskPath");
            var taskToRun = $"{GetJsonString(row, "Execute")} {GetJsonString(row, "Arguments")}".Trim();
            if (string.IsNullOrWhiteSpace(taskName) || string.IsNullOrWhiteSpace(taskPath))
                continue;

            results.Add(new ScheduledTaskInfo
            {
                TaskName = taskName,
                TaskPath = taskPath,
                NextRunTime = GetJsonString(row, "NextRunTime"),
                LastRunTime = GetJsonString(row, "LastRunTime"),
                LastResult = GetJsonString(row, "LastTaskResult"),
                Recurrence = GetJsonString(row, "Recurrence"),
                IsEnabled = GetJsonBool(row, "Enabled"),
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
        if (definition.RunOperationalEventLogs) enabledAudits.Add("OperationalEvents");
        if (definition.RunOutboundEvents) enabledAudits.Add("OutboundEvents");

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

    private async Task<string> QueryScheduledTasksAsJsonAsync(CancellationToken ct)
    {
        var taskPath = NormalizeTaskFolderPath().Replace("'", "''", StringComparison.Ordinal);
        var script = $@"
$ErrorActionPreference = 'Stop'
$taskPath = '{taskPath}'
$tasks = @(Get-ScheduledTask -TaskPath $taskPath -ErrorAction SilentlyContinue)
if ($tasks.Count -eq 0) {{
  '[]'
  exit 0
}}

$rows = foreach ($t in $tasks) {{
  $info = Get-ScheduledTaskInfo -TaskName $t.TaskName -TaskPath $t.TaskPath
  $action = $t.Actions | Select-Object -First 1
  $recurrence = ($t.Triggers | ForEach-Object {{ $_.CimClass.CimClassName }}) -join ';'
  [pscustomobject]@{{
    TaskName       = $t.TaskName
    TaskPath       = $t.TaskPath
    NextRunTime    = if ($info.NextRunTime -and $info.NextRunTime.Year -gt 1900) {{ $info.NextRunTime.ToString('s') }} else {{ '' }}
    LastRunTime    = if ($info.LastRunTime -and $info.LastRunTime.Year -gt 1900) {{ $info.LastRunTime.ToString('s') }} else {{ '' }}
    LastTaskResult = [string]$info.LastTaskResult
    Recurrence     = $recurrence
    Enabled        = [bool]$t.Settings.Enabled
    Execute        = [string]$action.Execute
    Arguments      = [string]$action.Arguments
  }}
}}

$rows | ConvertTo-Json -Depth 5 -Compress
";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Failed to enumerate scheduled tasks (PowerShell exit {process.ExitCode}). STDERR: {stderr}");

        return stdout.Trim();
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : value.ToString();
        return string.Empty;
    }

    private static bool GetJsonBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
                return parsed;
        }
        return false;
    }
}
