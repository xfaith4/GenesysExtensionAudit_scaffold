// File: AuditEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenesysExtensionAudit.Domain.Services
{
    /// <summary>
    /// Computes extension audit findings from two sources:
    /// (1) User profile "work phone extension" field
    /// (2) Edge extension assignment list
    /// </summary>
    public sealed class AuditEngine
    {
        public sealed class AuditEngineOptions
        {
            /// <summary>
            /// If false, inactive users are excluded from all computations (duplicates/unassigned/etc).
            /// </summary>
            public bool IncludeInactiveUsers { get; init; } = false;

            /// <summary>
            /// If true, compute duplicates in the assignment list (same extension key appears more than once).
            /// </summary>
            public bool ComputeDuplicateAssignedExtensions { get; init; } = true;

            /// <summary>
            /// If true, compute "assigned but not present on any profile" (optional report).
            /// </summary>
            public bool ComputeAssignedButMissingFromProfiles { get; init; } = false;

            /// <summary>
            /// How extension strings are normalized into join keys.
            /// </summary>
            public ExtensionNormalizationOptions Normalization { get; init; } = ExtensionNormalizationOptions.Default;

            /// <summary>
            /// If true, duplicate detection on profiles considers distinct users only.
            /// Kept for safety if upstream ever provides multiple profile extension entries per user.
            /// </summary>
            public bool DistinctUsersPerExtensionForProfileDuplicates { get; init; } = true;
        }

        // ------------------------
        // Input models
        // ------------------------

        /// <summary>
        /// Minimal user profile record for auditing.
        /// Provide WorkPhoneExtensionRaw already extracted from the Genesys user payload.
        /// </summary>
        public sealed record UserProfileRecord(
            string UserId,
            string? UserName,
            string? State,
            string? WorkPhoneExtensionRaw
        );

        /// <summary>
        /// Minimal extension assignment record for auditing.
        /// TargetType/TargetId are optional and may not be available depending on API payload shape.
        /// </summary>
        public sealed record ExtensionAssignmentRecord(
            string AssignmentId,
            string? ExtensionRaw,
            string? TargetType = null,
            string? TargetId = null
        );

        // ------------------------
        // Output models
        // ------------------------

        public sealed record ProfileExtensionDetail(
            string UserId,
            string? UserName,
            string? State,
            string? ExtensionRaw
        );

        public sealed record AssignmentExtensionDetail(
            string AssignmentId,
            string? ExtensionRaw,
            string? TargetType,
            string? TargetId
        );

        public sealed record DuplicateProfileExtensionFinding(
            string ExtensionKey,
            IReadOnlyList<ProfileExtensionDetail> Users
        );

        public sealed record DuplicateAssignedExtensionFinding(
            string ExtensionKey,
            IReadOnlyList<AssignmentExtensionDetail> Assignments
        );

        public sealed record ProfileExtensionNotAssignedFinding(
            string ExtensionKey,
            IReadOnlyList<ProfileExtensionDetail> Users
        );

        public sealed record AssignedExtensionMissingFromProfilesFinding(
            string ExtensionKey,
            IReadOnlyList<AssignmentExtensionDetail> Assignments
        );

        /// <summary>
        /// A user's profile extension exists in the telephony assignment list, but the assignment
        /// is not owned by that user (it is assigned to a different user or a non-user entity).
        /// This is the primary known platform bug: the profile and the telephony assignment system
        /// are out of sync regarding extension ownership.
        /// </summary>
        public sealed record ExtensionAssignedToWrongEntityFinding(
            string ExtensionKey,
            ProfileExtensionDetail User,
            IReadOnlyList<AssignmentExtensionDetail> ActualAssignments
        );

        public sealed record InvalidProfileExtensionFinding(
            string UserId,
            string? UserName,
            string? State,
            string? ExtensionRaw,
            ExtensionNormalizationStatus Status,
            string Notes
        );

        public sealed record InvalidAssignedExtensionFinding(
            string AssignmentId,
            string? ExtensionRaw,
            ExtensionNormalizationStatus Status,
            string Notes
        );

        public sealed class AuditReport
        {
            public IReadOnlyList<DuplicateProfileExtensionFinding> DuplicateProfileExtensions { get; init; }
                = Array.Empty<DuplicateProfileExtensionFinding>();

            public IReadOnlyList<DuplicateAssignedExtensionFinding> DuplicateAssignedExtensions { get; init; }
                = Array.Empty<DuplicateAssignedExtensionFinding>();

            public IReadOnlyList<ProfileExtensionNotAssignedFinding> ProfileExtensionsNotAssigned { get; init; }
                = Array.Empty<ProfileExtensionNotAssignedFinding>();

            public IReadOnlyList<AssignedExtensionMissingFromProfilesFinding> AssignedExtensionsMissingFromProfiles { get; init; }
                = Array.Empty<AssignedExtensionMissingFromProfilesFinding>();

            /// <summary>
            /// Extensions present in the telephony assignment list for a user profile, but the assignment
            /// is not owned by that user. This captures the primary platform bug where the assignment
            /// record's owner does not match the user whose profile claims that extension.
            /// </summary>
            public IReadOnlyList<ExtensionAssignedToWrongEntityFinding> ExtensionAssignedToWrongEntity { get; init; }
                = Array.Empty<ExtensionAssignedToWrongEntityFinding>();

            public IReadOnlyList<InvalidProfileExtensionFinding> InvalidProfileExtensions { get; init; }
                = Array.Empty<InvalidProfileExtensionFinding>();

            public IReadOnlyList<InvalidAssignedExtensionFinding> InvalidAssignedExtensions { get; init; }
                = Array.Empty<InvalidAssignedExtensionFinding>();

            public int TotalUsersConsidered { get; init; }
            public int TotalAssignmentsConsidered { get; init; }
        }

        // ------------------------
        // Public API
        // ------------------------

        public AuditReport Run(
            IEnumerable<UserProfileRecord> userProfiles,
            IEnumerable<ExtensionAssignmentRecord> assignments,
            AuditEngineOptions? options = null)
        {
            options ??= new AuditEngineOptions();

            var users = (userProfiles ?? Enumerable.Empty<UserProfileRecord>()).ToList();
            var assigns = (assignments ?? Enumerable.Empty<ExtensionAssignmentRecord>()).ToList();

            // Filter users based on IncludeInactiveUsers.
            if (!options.IncludeInactiveUsers)
            {
                users = users
                    .Where(u => string.Equals(u.State, "active", StringComparison.OrdinalIgnoreCase)
                             || string.IsNullOrWhiteSpace(u.State)) // some feeds may omit state
                    .ToList();
            }

            // Normalize and index: Profiles
            var invalidProfile = new List<InvalidProfileExtensionFinding>();
            var profileByExt = new Dictionary<string, List<ProfileExtensionDetail>>(StringComparer.Ordinal);

            foreach (var u in users)
            {
                var norm = ExtensionNormalization.Normalize(u.WorkPhoneExtensionRaw, options.Normalization);

                if (!norm.IsOk)
                {
                    // Only report invalids if there was something there (or if you want empty tracking, include it).
                    if (norm.Status != ExtensionNormalizationStatus.Empty)
                    {
                        invalidProfile.Add(new InvalidProfileExtensionFinding(
                            u.UserId,
                            u.UserName,
                            u.State,
                            u.WorkPhoneExtensionRaw,
                            norm.Status,
                            norm.Notes
                        ));
                    }

                    continue;
                }

                var key = norm.Normalized!;
                if (!profileByExt.TryGetValue(key, out var list))
                {
                    list = new List<ProfileExtensionDetail>();
                    profileByExt[key] = list;
                }

                list.Add(new ProfileExtensionDetail(
                    u.UserId,
                    u.UserName,
                    u.State,
                    u.WorkPhoneExtensionRaw
                ));
            }

            // Normalize and index: Assignments
            var invalidAssigned = new List<InvalidAssignedExtensionFinding>();
            var assignByExt = new Dictionary<string, List<AssignmentExtensionDetail>>(StringComparer.Ordinal);

            foreach (var a in assigns)
            {
                var norm = ExtensionNormalization.Normalize(a.ExtensionRaw, options.Normalization);

                if (!norm.IsOk)
                {
                    if (norm.Status != ExtensionNormalizationStatus.Empty)
                    {
                        invalidAssigned.Add(new InvalidAssignedExtensionFinding(
                            a.AssignmentId,
                            a.ExtensionRaw,
                            norm.Status,
                            norm.Notes
                        ));
                    }
                    continue;
                }

                var key = norm.Normalized!;
                if (!assignByExt.TryGetValue(key, out var list))
                {
                    list = new List<AssignmentExtensionDetail>();
                    assignByExt[key] = list;
                }

                list.Add(new AssignmentExtensionDetail(
                    a.AssignmentId,
                    a.ExtensionRaw,
                    a.TargetType,
                    a.TargetId
                ));
            }

            // (1) Duplicates in user profile work phone extension field
            var dupProfiles = new List<DuplicateProfileExtensionFinding>();
            foreach (var kvp in profileByExt)
            {
                var extKey = kvp.Key;
                var holders = kvp.Value;

                int distinctUsers = options.DistinctUsersPerExtensionForProfileDuplicates
                    ? holders.Select(h => h.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    : holders.Count;

                if (distinctUsers >= 2)
                {
                    // Keep stable ordering for UX/export
                    var ordered = holders
                        .OrderBy(h => h.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(h => h.UserId, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    dupProfiles.Add(new DuplicateProfileExtensionFinding(extKey, ordered));
                }
            }

            dupProfiles = dupProfiles
                .OrderBy(f => f.ExtensionKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // (2) Duplicates in assigned extensions list (if required)
            var dupAssigned = new List<DuplicateAssignedExtensionFinding>();
            if (options.ComputeDuplicateAssignedExtensions)
            {
                foreach (var kvp in assignByExt)
                {
                    var extKey = kvp.Key;
                    var rows = kvp.Value;

                    if (rows.Count >= 2)
                    {
                        var ordered = rows
                            .OrderBy(r => r.TargetType ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(r => r.TargetId ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(r => r.AssignmentId, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        dupAssigned.Add(new DuplicateAssignedExtensionFinding(extKey, ordered));
                    }
                }

                dupAssigned = dupAssigned
                    .OrderBy(f => f.ExtensionKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // (3) Extensions in profiles but not assigned (U \ A)
            var unassigned = new List<ProfileExtensionNotAssignedFinding>();
            foreach (var kvp in profileByExt)
            {
                var extKey = kvp.Key;
                if (!assignByExt.ContainsKey(extKey))
                {
                    var ordered = kvp.Value
                        .OrderBy(h => h.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(h => h.UserId, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    unassigned.Add(new ProfileExtensionNotAssignedFinding(extKey, ordered));
                }
            }

            unassigned = unassigned
                .OrderBy(f => f.ExtensionKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // (4) Assigned but not present on any profile (optional)
            var assignedMissingProfiles = new List<AssignedExtensionMissingFromProfilesFinding>();
            if (options.ComputeAssignedButMissingFromProfiles)
            {
                foreach (var kvp in assignByExt)
                {
                    var extKey = kvp.Key;
                    if (!profileByExt.ContainsKey(extKey))
                    {
                        var ordered = kvp.Value
                            .OrderBy(r => r.TargetType ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(r => r.TargetId ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(r => r.AssignmentId, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        assignedMissingProfiles.Add(new AssignedExtensionMissingFromProfilesFinding(extKey, ordered));
                    }
                }

                assignedMissingProfiles = assignedMissingProfiles
                    .OrderBy(f => f.ExtensionKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // (5) Extension on profile exists in the assignment list, but the assignment is not
            // owned by this user. This captures the primary platform bug: the user profile claims
            // an extension that the telephony system has assigned to a different entity.
            var ownershipMismatches = new List<ExtensionAssignedToWrongEntityFinding>();
            foreach (var kvp in profileByExt)
            {
                var extKey = kvp.Key;
                if (!assignByExt.TryGetValue(extKey, out var extAssignments))
                    continue; // Not in assignments at all → captured by ProfileExtensionsNotAssigned

                foreach (var user in kvp.Value)
                {
                    // The assignment is correct if it is explicitly owned by this user
                    bool correctlyAssigned = extAssignments.Any(a =>
                        string.Equals(a.TargetType, "USER", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.TargetId, user.UserId, StringComparison.OrdinalIgnoreCase));

                    if (correctlyAssigned)
                        continue;

                    var actualAssignments = extAssignments
                        .OrderBy(a => a.TargetType ?? "", StringComparer.OrdinalIgnoreCase)
                        .ThenBy(a => a.TargetId ?? "", StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    ownershipMismatches.Add(new ExtensionAssignedToWrongEntityFinding(
                        extKey, user, actualAssignments));
                }
            }

            ownershipMismatches = ownershipMismatches
                .OrderBy(f => f.ExtensionKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.User.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AuditReport
            {
                DuplicateProfileExtensions = dupProfiles,
                DuplicateAssignedExtensions = dupAssigned,
                ProfileExtensionsNotAssigned = unassigned,
                AssignedExtensionsMissingFromProfiles = assignedMissingProfiles,
                ExtensionAssignedToWrongEntity = ownershipMismatches,
                InvalidProfileExtensions = invalidProfile
                    .OrderBy(i => i.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.UserId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                InvalidAssignedExtensions = invalidAssigned
                    .OrderBy(i => i.AssignmentId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                TotalUsersConsidered = users.Count,
                TotalAssignmentsConsidered = assigns.Count
            };
        }
    }
}
