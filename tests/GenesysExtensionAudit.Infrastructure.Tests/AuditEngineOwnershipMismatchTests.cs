using GenesysExtensionAudit.Domain.Services;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for the <see cref="AuditEngine"/> extension ownership mismatch detection.
/// These tests cover the primary platform bug scenario: a user's profile extension exists
/// in the telephony assignment list but is assigned to a different entity.
/// </summary>
public sealed class AuditEngineOwnershipMismatchTests
{
    private static AuditEngine.UserProfileRecord User(string id, string ext, string state = "active")
        => new(id, $"User {id}", state, ext);

    private static AuditEngine.ExtensionAssignmentRecord Assignment(string id, string ext, string? targetType = null, string? targetId = null)
        => new(id, ext, targetType, targetId);

    // ─── No mismatch: extension correctly assigned to the owning user ─────

    [Fact]
    public void NoMismatch_WhenExtensionAssignedToCorrectUser()
    {
        var users = new[] { User("u1", "1001") };
        var assignments = new[] { Assignment("a1", "1001", targetType: "USER", targetId: "u1") };

        var report = new AuditEngine().Run(users, assignments);

        Assert.Empty(report.ExtensionAssignedToWrongEntity);
    }

    [Fact]
    public void NoMismatch_WhenExtensionIdComparison_IsCaseInsensitive()
    {
        var users = new[] { User("USER-001", "2000") };
        var assignments = new[] { Assignment("a1", "2000", targetType: "user", targetId: "user-001") };

        var report = new AuditEngine().Run(users, assignments);

        Assert.Empty(report.ExtensionAssignedToWrongEntity);
    }

    // ─── Mismatch: extension assigned to a different user ─────────────────

    [Fact]
    public void Mismatch_WhenExtensionAssignedToDifferentUser()
    {
        // u1 has 1001 on profile, but 1001 is assigned to u2 in telephony
        var users = new[] { User("u1", "1001") };
        var assignments = new[] { Assignment("a1", "1001", targetType: "USER", targetId: "u2") };

        var report = new AuditEngine().Run(users, assignments);

        Assert.Single(report.ExtensionAssignedToWrongEntity);
        var finding = report.ExtensionAssignedToWrongEntity[0];
        Assert.Equal("1001", finding.ExtensionKey);
        Assert.Equal("u1", finding.User.UserId);
        Assert.Single(finding.ActualAssignments);
        Assert.Equal("u2", finding.ActualAssignments[0].TargetId);
    }

    // ─── Mismatch: extension assigned to a non-user entity ────────────────

    [Fact]
    public void Mismatch_WhenExtensionAssignedToStation()
    {
        var users = new[] { User("u1", "3000") };
        var assignments = new[] { Assignment("a1", "3000", targetType: "STATION", targetId: "station-99") };

        var report = new AuditEngine().Run(users, assignments);

        Assert.Single(report.ExtensionAssignedToWrongEntity);
        Assert.Equal("STATION", report.ExtensionAssignedToWrongEntity[0].ActualAssignments[0].TargetType);
    }

    // ─── No mismatch: extension not in assignments at all (ProfileOnly) ───

    [Fact]
    public void NoMismatch_WhenExtensionNotInAssignments_CapturedByProfileOnly()
    {
        // Extension not in the assignment list → ProfileExtensionsNotAssigned, NOT ownership mismatch
        var users = new[] { User("u1", "9999") };
        var assignments = Array.Empty<AuditEngine.ExtensionAssignmentRecord>();

        var report = new AuditEngine().Run(users, assignments);

        Assert.Empty(report.ExtensionAssignedToWrongEntity);
        Assert.NotEmpty(report.ProfileExtensionsNotAssigned);
    }

    // ─── Multiple users sharing an extension, one correct ─────────────────

    [Fact]
    public void Mismatch_OnlyFlagsUsersWithoutMatchingAssignment()
    {
        // u1 has 4000 and 4000 is assigned to u1 → OK
        // u2 also has 4000 but 4000 is only assigned to u1 → mismatch for u2
        var users = new[] { User("u1", "4000"), User("u2", "4000") };
        var assignments = new[] { Assignment("a1", "4000", targetType: "USER", targetId: "u1") };

        var report = new AuditEngine().Run(users, assignments,
            new AuditEngine.AuditEngineOptions { DistinctUsersPerExtensionForProfileDuplicates = false });

        // u2 should be flagged as a mismatch
        Assert.Single(report.ExtensionAssignedToWrongEntity);
        Assert.Equal("u2", report.ExtensionAssignedToWrongEntity[0].User.UserId);
    }

    // ─── Assignment with null target: no ownership claim → flag it ────────

    [Fact]
    public void Mismatch_WhenAssignmentHasNullTargetId()
    {
        // Extension in the system but not owned by anyone
        var users = new[] { User("u1", "5000") };
        var assignments = new[] { Assignment("a1", "5000", targetType: null, targetId: null) };

        var report = new AuditEngine().Run(users, assignments);

        // No user-to-user assignment exists, so ownership mismatch is flagged
        Assert.Single(report.ExtensionAssignedToWrongEntity);
    }

    // ─── Inactive users excluded by default ───────────────────────────────

    [Fact]
    public void NoMismatch_ForInactiveUsers_WhenIncludeInactiveIsFalse()
    {
        var users = new[] { User("u1", "6000", state: "inactive") };
        var assignments = new[] { Assignment("a1", "6000", targetType: "USER", targetId: "u2") };

        var report = new AuditEngine().Run(users, assignments,
            new AuditEngine.AuditEngineOptions { IncludeInactiveUsers = false });

        Assert.Empty(report.ExtensionAssignedToWrongEntity);
    }

    [Fact]
    public void Mismatch_ForInactiveUsers_WhenIncludeInactiveIsTrue()
    {
        var users = new[] { User("u1", "6000", state: "inactive") };
        var assignments = new[] { Assignment("a1", "6000", targetType: "USER", targetId: "u2") };

        var report = new AuditEngine().Run(users, assignments,
            new AuditEngine.AuditEngineOptions { IncludeInactiveUsers = true });

        Assert.Single(report.ExtensionAssignedToWrongEntity);
    }
}
