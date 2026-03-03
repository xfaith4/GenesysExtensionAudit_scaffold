using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GenesysExtensionAudit.Infrastructure.Application;

/// <summary>
/// Runs all audit categories and returns a combined <see cref="AuditReportData"/>.
/// Each phase reports progress and is independently cancellable.
/// </summary>
public sealed class AuditOrchestrator : IAuditOrchestrator
{
    private readonly IGenesysUsersClient _usersClient;
    private readonly IGenesysExtensionsClient _extensionsClient;
    private readonly IGenesysGroupsClient _groupsClient;
    private readonly IGenesysQueuesClient _queuesClient;
    private readonly IGenesysFlowsClient _flowsClient;
    private readonly IGenesysDidsClient _didsClient;
    private readonly IGenesysAuditLogsClient _auditLogsClient;
    private readonly IPaginator _paginator;
    private readonly GenesysRegionOptions _region;
    private readonly ILogger<AuditOrchestrator> _logger;

    public AuditOrchestrator(
        IGenesysUsersClient usersClient,
        IGenesysExtensionsClient extensionsClient,
        IGenesysGroupsClient groupsClient,
        IGenesysQueuesClient queuesClient,
        IGenesysFlowsClient flowsClient,
        IGenesysDidsClient didsClient,
        IGenesysAuditLogsClient auditLogsClient,
        IPaginator paginator,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<AuditOrchestrator> logger)
    {
        _usersClient = usersClient ?? throw new ArgumentNullException(nameof(usersClient));
        _extensionsClient = extensionsClient ?? throw new ArgumentNullException(nameof(extensionsClient));
        _groupsClient = groupsClient ?? throw new ArgumentNullException(nameof(groupsClient));
        _queuesClient = queuesClient ?? throw new ArgumentNullException(nameof(queuesClient));
        _flowsClient = flowsClient ?? throw new ArgumentNullException(nameof(flowsClient));
        _didsClient = didsClient ?? throw new ArgumentNullException(nameof(didsClient));
        _auditLogsClient = auditLogsClient ?? throw new ArgumentNullException(nameof(auditLogsClient));
        _paginator = paginator ?? throw new ArgumentNullException(nameof(paginator));
        _region = regionOptions?.Value ?? throw new ArgumentNullException(nameof(regionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditReportData> RunAsync(
        AuditRunOptions options,
        IProgress<AuditProgress> progress,
        CancellationToken ct)
    {
        var runStartedUtc = DateTimeOffset.UtcNow;
        var ps = Math.Clamp(options.PageSize, 1, 500);
        var runAny =
            options.RunExtensionAudit ||
            options.RunGroupAudit ||
            options.RunQueueAudit ||
            options.RunFlowAudit ||
            options.RunInactiveUserAudit ||
            options.RunDidAudit ||
            options.RunAuditLogs;

        if (!runAny)
            throw new InvalidOperationException("At least one audit path must be selected.");

        _logger.LogInformation(
            "Audit started. PageSize={PageSize} IncludeInactive={IncludeInactive} StaleFlowDays={StaleFlowDays} InactiveUserDays={InactiveUserDays} " +
            "RunExtension={RunExtension} RunGroups={RunGroups} RunQueues={RunQueues} RunFlows={RunFlows} RunInactiveUsers={RunInactiveUsers} RunDids={RunDids} RunAuditLogs={RunAuditLogs}",
            ps, options.IncludeInactiveUsers, options.StaleFlowThresholdDays, options.InactiveUserThresholdDays,
            options.RunExtensionAudit, options.RunGroupAudit, options.RunQueueAudit, options.RunFlowAudit,
            options.RunInactiveUserAudit, options.RunDidAudit, options.RunAuditLogs);

        var needsUsers = options.RunExtensionAudit || options.RunInactiveUserAudit || options.RunDidAudit;
        var needsExtensions = options.RunExtensionAudit;
        var needsGroups = options.RunGroupAudit;
        var needsQueues = options.RunQueueAudit;
        var needsFlows = options.RunFlowAudit;
        var needsDids = options.RunDidAudit;

        IReadOnlyList<GenesysUserDto> userDtos = [];
        IReadOnlyList<EdgeExtensionEntityDto> extDtos = [];
        IReadOnlyList<GroupDto> groupDtos = [];
        IReadOnlyList<QueueDto> queueDtos = [];
        IReadOnlyList<FlowDto> flowDtos = [];
        IReadOnlyList<DidDto> didDtos = [];
        IReadOnlyList<AuditLogFinding> auditLogFindings = [];

        if (needsUsers)
        {
            Report(progress, 0, "Fetching users...");
            userDtos = await _paginator.FetchAllAsync(
                pn => _usersClient.GetUsersPageAsync(pn, ps, options.IncludeInactiveUsers, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} users", userDtos.Count);
        }

        if (needsExtensions)
        {
            Report(progress, 10, "Fetching extensions...");
            extDtos = await _paginator.FetchAllAsync(
                pn => _extensionsClient.GetExtensionsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} extensions", extDtos.Count);
        }

        if (needsGroups)
        {
            Report(progress, 20, "Fetching groups...");
            groupDtos = await _paginator.FetchAllAsync(
                pn => _groupsClient.GetGroupsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} groups", groupDtos.Count);
        }

        if (needsQueues)
        {
            Report(progress, 30, "Fetching queues...");
            queueDtos = await _paginator.FetchAllAsync(
                pn => _queuesClient.GetQueuesPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} queues", queueDtos.Count);
        }

        if (needsFlows)
        {
            Report(progress, 40, "Fetching Architect flows...");
            flowDtos = await _paginator.FetchAllAsync(
                pn => _flowsClient.GetFlowsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} flows", flowDtos.Count);
        }

        if (needsDids)
        {
            Report(progress, 50, "Fetching DIDs...");
            didDtos = await _paginator.FetchAllAsync(
                pn => _didsClient.GetDidsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} DIDs", didDtos.Count);
        }

        if (options.RunAuditLogs)
        {
            Report(progress, 55, "Fetching audit logs service mappings...");
            var serviceMappings = await _auditLogsClient.GetServiceMappingsAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} audit service mappings", serviceMappings.Count);

            var now = DateTimeOffset.UtcNow;
            var lookbackHours = Math.Max(1, options.AuditLogLookbackHours);
            var interval = $"{now.AddHours(-lookbackHours):o}/{now:o}";

            var submit = new AuditLogsSubmitRequestDto
            {
                Interval = interval,
                ServiceName = options.AuditLogServiceNames.Count > 0
                    ? options.AuditLogServiceNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : serviceMappings.ToList(),
                Action = []
            };

            Report(progress, 58, "Submitting audit logs transaction...");
            var transactionId = await _auditLogsClient.SubmitAuditQueryAsync(submit, ct).ConfigureAwait(false);

            const int maxPolls = 60;
            const int pollIntervalSeconds = 2;
            string state = "RUNNING";
            for (var i = 1; i <= maxPolls; i++)
            {
                ct.ThrowIfCancellationRequested();
                var status = await _auditLogsClient.GetAuditQueryStatusAsync(transactionId, ct).ConfigureAwait(false);
                state = (status.State ?? string.Empty).Trim().ToUpperInvariant();

                if (state == "FULFILLED")
                    break;
                if (state is "FAILED" or "CANCELLED")
                    throw new InvalidOperationException($"Audit transaction ended in state '{state}'.");

                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct).ConfigureAwait(false);
            }

            if (state != "FULFILLED")
                throw new TimeoutException($"Audit transaction did not complete within {maxPolls} polls.");

            Report(progress, 59, "Fetching audit logs results...");
            var records = new List<JsonElement>();
            string? nextUri = null;
            do
            {
                var page = await _auditLogsClient
                    .GetAuditQueryResultsPageAsync(transactionId, nextUri, ct)
                    .ConfigureAwait(false);

                if (page.Results is { Count: > 0 })
                    records.AddRange(page.Results);

                nextUri = page.NextUri;
            } while (!string.IsNullOrWhiteSpace(nextUri));

            auditLogFindings = AnalyzeAuditLogs(records);
            _logger.LogInformation("Fetched {Count} audit log records", auditLogFindings.Count);
        }

        Report(progress, 60, "Running selected audit paths...");
        var extensionReport = options.RunExtensionAudit
            ? RunExtensionAudit(userDtos, extDtos, options)
            : new AuditEngine.AuditReport();
        var groupFindings = options.RunGroupAudit
            ? AnalyzeGroups(groupDtos)
            : [];
        var queueFindings = options.RunQueueAudit
            ? AnalyzeQueues(queueDtos)
            : [];
        var flowFindings = options.RunFlowAudit
            ? AnalyzeFlows(flowDtos, options.StaleFlowThresholdDays)
            : [];
        var didFindings = options.RunDidAudit
            ? AnalyzeDids(didDtos, userDtos)
            : [];
        var inactiveUserFindings = options.RunInactiveUserAudit
            ? AnalyzeUserActivity(userDtos, options.InactiveUserThresholdDays)
            : [];

        Report(progress, 90, "Composing report...");

        var totalFindings = extensionReport.DuplicateProfileExtensions.Count
            + extensionReport.DuplicateAssignedExtensions.Count
            + extensionReport.ProfileExtensionsNotAssigned.Count
            + extensionReport.AssignedExtensionsMissingFromProfiles.Count
            + extensionReport.InvalidProfileExtensions.Count
            + extensionReport.InvalidAssignedExtensions.Count
            + groupFindings.Count + queueFindings.Count
            + flowFindings.Count + inactiveUserFindings.Count
            + didFindings.Count + auditLogFindings.Count;

        _logger.LogInformation(
            "Audit complete. TotalFindings={TotalFindings} Groups={Groups} Queues={Queues} Flows={Flows} InactiveUsers={InactiveUsers} DIDs={DIDs}",
            totalFindings, groupFindings.Count, queueFindings.Count,
            flowFindings.Count, inactiveUserFindings.Count, didFindings.Count);

        Report(progress, 100,
            $"Complete — {totalFindings} total findings across all checks.",
            status: "Audit completed successfully.");

        return new AuditReportData
        {
            GeneratedAt = DateTimeOffset.Now,
            RunStartedAtUtc = runStartedUtc,
            RunCompletedAtUtc = DateTimeOffset.UtcNow,
            OrgRegion = _region.Region,
            Options = options,
            ExtensionReport = extensionReport,
            GroupFindings = groupFindings,
            QueueFindings = queueFindings,
            FlowFindings = flowFindings,
            InactiveUserFindings = inactiveUserFindings,
            DidFindings = didFindings,
            AuditLogFindings = auditLogFindings
        };
    }

    // ─── Extension audit (delegates to AuditEngine) ───────────────────────

    private static AuditEngine.AuditReport RunExtensionAudit(
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        AuditRunOptions options)
    {
        var engine = new AuditEngine();

        var userProfiles = users
            .Where(u => u.Id is not null)
            .Select(u => new AuditEngine.UserProfileRecord(
                UserId: u.Id!,
                UserName: u.Name,
                State: u.State,
                WorkPhoneExtensionRaw: ExtractWorkPhoneExtension(u)))
            .ToList();

        var assignments = extensions
            .Where(e => e.Id is not null)
            .Select(e => new AuditEngine.ExtensionAssignmentRecord(
                AssignmentId: e.Id!,
                ExtensionRaw: e.Extension,
                TargetType: e.AssignedTo?.Type,
                TargetId: e.AssignedTo?.Id))
            .ToList();

        return engine.Run(userProfiles, assignments, new AuditEngine.AuditEngineOptions
        {
            IncludeInactiveUsers = options.IncludeInactiveUsers,
            ComputeDuplicateAssignedExtensions = true,
            ComputeAssignedButMissingFromProfiles = true
        });
    }

    // ─── Group analysis ───────────────────────────────────────────────────

    private static IReadOnlyList<GroupFinding> AnalyzeGroups(IReadOnlyList<GroupDto> groups)
    {
        var findings = new List<GroupFinding>();

        foreach (var g in groups)
        {
            if (g.Id is null) continue;

            var memberCount = g.MemberCount ?? 0;
            if (memberCount == 0)
            {
                findings.Add(new GroupFinding(
                    GroupId: g.Id,
                    GroupName: g.Name,
                    Type: g.Type,
                    State: g.State,
                    MemberCount: memberCount,
                    DateModified: g.DateModified,
                    Issue: "Empty group — no members"));
            }
            else if (memberCount == 1)
            {
                findings.Add(new GroupFinding(
                    GroupId: g.Id,
                    GroupName: g.Name,
                    Type: g.Type,
                    State: g.State,
                    MemberCount: memberCount,
                    DateModified: g.DateModified,
                    Issue: "Single-member group — review if intentional"));
            }
        }

        return findings.OrderBy(f => f.MemberCount).ThenBy(f => f.GroupName).ToList();
    }

    // ─── Queue analysis ───────────────────────────────────────────────────

    private static IReadOnlyList<QueueFinding> AnalyzeQueues(IReadOnlyList<QueueDto> queues)
    {
        var findings = new List<QueueFinding>();

        // Empty queues
        foreach (var q in queues.Where(q => q.Id is not null && (q.MemberCount ?? 0) == 0))
        {
            findings.Add(new QueueFinding(
                QueueId: q.Id!,
                QueueName: q.Name,
                Description: q.Description,
                MemberCount: q.MemberCount ?? 0,
                Issue: "Empty queue — no members"));
        }

        // Duplicate queue names (case-insensitive)
        var byName = queues
            .Where(q => q.Name is not null)
            .GroupBy(q => q.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in byName)
        {
            foreach (var q in group.Where(q => q.Id is not null))
            {
                findings.Add(new QueueFinding(
                    QueueId: q.Id!,
                    QueueName: q.Name,
                    Description: q.Description,
                    MemberCount: q.MemberCount ?? 0,
                    Issue: $"Duplicate queue name (case-insensitive match): \"{group.Key}\""));
            }
        }

        return findings.OrderBy(f => f.Issue).ThenBy(f => f.QueueName).ToList();
    }

    // ─── Flow analysis ────────────────────────────────────────────────────

    private static IReadOnlyList<FlowFinding> AnalyzeFlows(
        IReadOnlyList<FlowDto> flows, int thresholdDays)
    {
        var findings = new List<FlowFinding>();
        var cutoff = DateTime.UtcNow.AddDays(-thresholdDays);

        foreach (var f in flows)
        {
            if (f.Id is null) continue;

            // Draft / never published
            if (f.PublishedVersion is null)
            {
                findings.Add(new FlowFinding(
                    FlowId: f.Id,
                    FlowName: f.Name,
                    FlowType: f.Type,
                    IsPublished: false,
                    PublishedDate: null,
                    DateModified: f.DateModified,
                    DaysSincePublished: null,
                    Issue: "Never published (draft)"));
                continue;
            }

            var publishedDate = f.PublishedVersion.PublishedDate;
            if (publishedDate.HasValue && publishedDate.Value < cutoff)
            {
                var days = (int)(DateTime.UtcNow - publishedDate.Value).TotalDays;
                findings.Add(new FlowFinding(
                    FlowId: f.Id,
                    FlowName: f.Name,
                    FlowType: f.Type,
                    IsPublished: true,
                    PublishedDate: publishedDate.Value,
                    DateModified: f.DateModified,
                    DaysSincePublished: days,
                    Issue: $"Not republished in {days} days (threshold: {thresholdDays})"));
            }
        }

        return findings
            .OrderByDescending(f => f.DaysSincePublished ?? int.MaxValue)
            .ThenBy(f => f.FlowName)
            .ToList();
    }

    // ─── User activity analysis ───────────────────────────────────────────

    private static IReadOnlyList<InactiveUserFinding> AnalyzeUserActivity(
        IReadOnlyList<GenesysUserDto> users, int thresholdDays)
    {
        var findings = new List<InactiveUserFinding>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-thresholdDays);

        foreach (var u in users.Where(u => u.Id is not null))
        {
            string issue;
            int? days = null;

            if (u.TokenLastIssuedDate is null)
            {
                issue = "No token ever issued — user has never logged in via OAuth";
            }
            else if (u.TokenLastIssuedDate < cutoff)
            {
                days = (int)(DateTimeOffset.UtcNow - u.TokenLastIssuedDate.Value).TotalDays;
                issue = $"No login in {days} days (threshold: {thresholdDays})";
            }
            else
            {
                continue; // active enough
            }

            findings.Add(new InactiveUserFinding(
                UserId: u.Id!,
                UserName: u.Name,
                Email: u.Email,
                State: u.State,
                TokenLastIssuedDate: u.TokenLastIssuedDate,
                DaysSinceLogin: days,
                Issue: issue));
        }

        return findings
            .OrderByDescending(f => f.DaysSinceLogin ?? int.MaxValue)
            .ThenBy(f => f.UserName)
            .ToList();
    }

    // ─── DID analysis ─────────────────────────────────────────────────────

    private static IReadOnlyList<DidFinding> AnalyzeDids(
        IReadOnlyList<DidDto> dids,
        IReadOnlyList<GenesysUserDto> users)
    {
        var findings = new List<DidFinding>();

        var userById = users
            .Where(u => u.Id is not null)
            .ToDictionary(u => u.Id!, u => u, StringComparer.OrdinalIgnoreCase);

        // Build set of phone numbers appearing in user profiles (any contact info)
        var userPhoneNumbers = users
            .SelectMany(u => u.PrimaryContactInfo ?? [])
            .Where(ci => !string.IsNullOrWhiteSpace(ci.Address))
            .Select(ci => NormalizePhoneNumber(ci.Address!))
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (var did in dids)
        {
            if (did.Id is null || string.IsNullOrWhiteSpace(did.PhoneNumber)) continue;

            var normalizedNumber = NormalizePhoneNumber(did.PhoneNumber);

            // DID in pool but not assigned to any entity
            if (did.Owner is null || string.IsNullOrWhiteSpace(did.Owner.Id))
            {
                findings.Add(new DidFinding(
                    DidId: did.Id,
                    PhoneNumber: did.PhoneNumber,
                    PoolId: did.DidPool?.Id,
                    OwnerType: null,
                    OwnerId: null,
                    OwnerName: null,
                    Issue: "DID in pool has no assigned owner"));
                continue;
            }

            // DID assigned to a user — verify user exists and is active
            if (string.Equals(did.Owner.Type, "User", StringComparison.OrdinalIgnoreCase))
            {
                if (!userById.TryGetValue(did.Owner.Id, out var owner))
                {
                    findings.Add(new DidFinding(
                        DidId: did.Id,
                        PhoneNumber: did.PhoneNumber,
                        PoolId: did.DidPool?.Id,
                        OwnerType: did.Owner.Type,
                        OwnerId: did.Owner.Id,
                        OwnerName: null,
                        Issue: "DID assigned to user not found in user list"));
                }
                else if (string.Equals(owner.State, "inactive", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new DidFinding(
                        DidId: did.Id,
                        PhoneNumber: did.PhoneNumber,
                        PoolId: did.DidPool?.Id,
                        OwnerType: did.Owner.Type,
                        OwnerId: did.Owner.Id,
                        OwnerName: owner.Name,
                        Issue: "DID assigned to inactive user"));
                }
            }

            // DID number not appearing on any user profile contact info
            if (normalizedNumber is not null && !userPhoneNumbers.Contains(normalizedNumber))
            {
                findings.Add(new DidFinding(
                    DidId: did.Id,
                    PhoneNumber: did.PhoneNumber,
                    PoolId: did.DidPool?.Id,
                    OwnerType: did.Owner?.Type,
                    OwnerId: did.Owner?.Id,
                    OwnerName: did.Owner?.Id is not null && userById.TryGetValue(did.Owner.Id, out var u) ? u.Name : null,
                    Issue: "DID number not found on any user profile"));
            }
        }

        return findings.OrderBy(f => f.Issue).ThenBy(f => f.PhoneNumber).ToList();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string? ExtractWorkPhoneExtension(GenesysUserDto user)
    {
        if (user.PrimaryContactInfo is null or { Count: 0 }) return null;

        var candidates = user.PrimaryContactInfo
            .Where(ci => string.Equals(ci.MediaType, "PHONE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ci => string.Equals(ci.Type, "work", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        foreach (var ci in candidates)
        {
            if (!string.IsNullOrWhiteSpace(ci.Extension))
                return ci.Extension.Trim();
        }

        return null;
    }

    private static string? NormalizePhoneNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Keep only digits for comparison
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }

    private static IReadOnlyList<AuditLogFinding> AnalyzeAuditLogs(IReadOnlyList<JsonElement> records)
    {
        var findings = new List<AuditLogFinding>(records.Count);

        foreach (var record in records)
        {
            if (record.ValueKind != JsonValueKind.Object)
                continue;

            var map = record.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

            map.TryGetValue("id", out var idValue);

            findings.Add(new AuditLogFinding(
                AuditId: AsString(idValue),
                TimestampUtc: ParseTimestamp(map),
                ServiceName: GetString(map, "serviceName"),
                Action: GetString(map, "action"),
                UserName: GetString(map, "userName", "name"),
                UserEmail: GetString(map, "userEmail", "email"),
                EntityType: GetString(map, "entityType", "targetType"),
                EntityName: GetString(map, "entityName", "targetName")));
        }

        return findings
            .OrderByDescending(f => f.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToList();

        static string? GetString(Dictionary<string, JsonElement> map, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var value))
                {
                    var text = AsString(value);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        static DateTimeOffset? ParseTimestamp(Dictionary<string, JsonElement> map)
        {
            var raw = GetString(map, "dateIssued", "timestamp", "eventTime", "dateCreated");
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return DateTimeOffset.TryParse(raw, out var ts) ? ts : null;
        }

        static string? AsString(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
    }

    private static void Report(IProgress<AuditProgress> progress, int percent, string message, string? status = null)
    {
        progress.Report(new AuditProgress
        {
            Percent = percent,
            Message = message,
            Status = status
        });
    }
}
