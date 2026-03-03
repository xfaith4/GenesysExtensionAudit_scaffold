using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        _paginator = paginator ?? throw new ArgumentNullException(nameof(paginator));
        _region = regionOptions?.Value ?? throw new ArgumentNullException(nameof(regionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditReportData> RunAsync(
        AuditRunOptions options,
        IProgress<AuditProgress> progress,
        CancellationToken ct)
    {
        var ps = Math.Clamp(options.PageSize, 1, 500);
        _logger.LogInformation(
            "Audit started. PageSize={PageSize} IncludeInactive={IncludeInactive} StaleFlowDays={StaleFlowDays} InactiveUserDays={InactiveUserDays}",
            ps, options.IncludeInactiveUsers, options.StaleFlowThresholdDays, options.InactiveUserThresholdDays);

        // ── Phase 1 (0-10%): Users ────────────────────────────────────────
        Report(progress, 0, "Fetching users...");
        var userDtos = await _paginator.FetchAllAsync(
            pn => _usersClient.GetUsersPageAsync(pn, ps, options.IncludeInactiveUsers, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} users", userDtos.Count);

        // ── Phase 2 (10-20%): Extensions ─────────────────────────────────
        Report(progress, 10, $"Fetched {userDtos.Count} users. Fetching extensions...");
        var extDtos = await _paginator.FetchAllAsync(
            pn => _extensionsClient.GetExtensionsPageAsync(pn, ps, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} extensions", extDtos.Count);

        // ── Phase 3 (20-30%): Groups ──────────────────────────────────────
        Report(progress, 20, $"Fetched {extDtos.Count} extensions. Fetching groups...");
        var groupDtos = await _paginator.FetchAllAsync(
            pn => _groupsClient.GetGroupsPageAsync(pn, ps, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} groups", groupDtos.Count);

        // ── Phase 4 (30-40%): Queues ──────────────────────────────────────
        Report(progress, 30, $"Fetched {groupDtos.Count} groups. Fetching queues...");
        var queueDtos = await _paginator.FetchAllAsync(
            pn => _queuesClient.GetQueuesPageAsync(pn, ps, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} queues", queueDtos.Count);

        // ── Phase 5 (40-50%): Flows ───────────────────────────────────────
        Report(progress, 40, $"Fetched {queueDtos.Count} queues. Fetching Architect flows...");
        var flowDtos = await _paginator.FetchAllAsync(
            pn => _flowsClient.GetFlowsPageAsync(pn, ps, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} flows", flowDtos.Count);

        // ── Phase 6 (50-60%): DIDs ────────────────────────────────────────
        Report(progress, 50, $"Fetched {flowDtos.Count} flows. Fetching DIDs...");
        var didDtos = await _paginator.FetchAllAsync(
            pn => _didsClient.GetDidsPageAsync(pn, ps, ct), ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Fetched {Count} DIDs", didDtos.Count);

        // ── Phase 7 (60-70%): Extension cross-reference ───────────────────
        Report(progress, 60, "Running extension audit...");
        var extensionReport = RunExtensionAudit(userDtos, extDtos, options);

        // ── Phase 8 (70-80%): Group / Queue / Flow / DID analysis ─────────
        Report(progress, 70, "Analyzing groups, queues, flows, DIDs...");
        var groupFindings = AnalyzeGroups(groupDtos);
        var queueFindings = AnalyzeQueues(queueDtos);
        var flowFindings = AnalyzeFlows(flowDtos, options.StaleFlowThresholdDays);
        var didFindings = AnalyzeDids(didDtos, userDtos);

        // ── Phase 9 (80-90%): User activity ──────────────────────────────
        Report(progress, 80, "Analyzing user activity...");
        var inactiveUserFindings = AnalyzeUserActivity(userDtos, options.InactiveUserThresholdDays);

        // ── Phase 10 (90-100%): Compose result ───────────────────────────
        Report(progress, 90, "Composing report...");

        var totalFindings = extensionReport.DuplicateProfileExtensions.Count
            + extensionReport.DuplicateAssignedExtensions.Count
            + extensionReport.ProfileExtensionsNotAssigned.Count
            + extensionReport.AssignedExtensionsMissingFromProfiles.Count
            + extensionReport.InvalidProfileExtensions.Count
            + extensionReport.InvalidAssignedExtensions.Count
            + groupFindings.Count + queueFindings.Count
            + flowFindings.Count + inactiveUserFindings.Count
            + didFindings.Count;

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
            OrgRegion = _region.Region,
            Options = options,
            ExtensionReport = extensionReport,
            GroupFindings = groupFindings,
            QueueFindings = queueFindings,
            FlowFindings = flowFindings,
            InactiveUserFindings = inactiveUserFindings,
            DidFindings = didFindings
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
