using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class ScheduledAuditProfileTests
{
    [Fact]
    public void ScheduledAuditProfile_RoundTrips_AndPreservesAuditSelections()
    {
        var profile = new ScheduledAuditProfile
        {
            ScheduleId = "abc123",
            Name = "Nightly Queues",
            CreatedBy = "CONTOSO\\jane",
            CreatedAtUtc = new DateTimeOffset(2026, 3, 3, 12, 0, 0, TimeSpan.Zero),
            PageSize = 250,
            IncludeInactiveUsers = true,
            StaleFlowThresholdDays = 45,
            InactiveUserThresholdDays = 30,
            RunExtensionAudit = false,
            RunGroupAudit = false,
            RunQueueAudit = true,
            RunFlowAudit = false,
            RunInactiveUserAudit = false,
            RunDidAudit = true,
            RunAuditLogs = true,
            AuditLogLookbackHours = 3,
            AuditLogServiceName = "telephony"
        };

        var json = JsonSerializer.Serialize(profile);
        var rehydrated = JsonSerializer.Deserialize<ScheduledAuditProfile>(json);

        Assert.NotNull(rehydrated);
        Assert.Equal("abc123", rehydrated!.ScheduleId);
        Assert.Equal(250, rehydrated.PageSize);
        Assert.True(rehydrated.RunQueueAudit);
        Assert.True(rehydrated.RunDidAudit);
        Assert.True(rehydrated.RunAuditLogs);
        Assert.Equal("telephony", rehydrated.AuditLogServiceName);
        Assert.True(rehydrated.HasAnyAuditSelected);
    }

    [Fact]
    public void ScheduledAuditProfile_HasAnyAuditSelected_FalseWhenAllUnchecked()
    {
        var profile = new ScheduledAuditProfile
        {
            RunExtensionAudit = false,
            RunGroupAudit = false,
            RunQueueAudit = false,
            RunFlowAudit = false,
            RunInactiveUserAudit = false,
            RunDidAudit = false,
            RunAuditLogs = false
        };

        Assert.False(profile.HasAnyAuditSelected);
    }

    [Fact]
    public void ScheduledAuditCommandLine_BuildsAndExtractsScheduleProfilePath()
    {
        var runner = @"C:\Tools\Genesys\GenesysExtensionAudit.Runner.exe";
        var profile = @"C:\ProgramData\GenesysExtensionAudit\Schedules\abc123.json";

        var command = ScheduledAuditCommandLine.BuildTaskRunCommand(runner, profile);
        var extracted = ScheduledAuditCommandLine.TryExtractProfilePath(command);

        Assert.Contains("--schedule-profile", command, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(profile, extracted);
    }
}
