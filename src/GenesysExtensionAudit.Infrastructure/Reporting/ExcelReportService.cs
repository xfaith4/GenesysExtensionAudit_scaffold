using ClosedXML.Excel;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface IExcelReportService
{
    Task<byte[]> GenerateAsync(AuditReportData report, CancellationToken ct);
}

/// <summary>
/// Generates a single Excel workbook containing all audit findings.
/// Each category gets its own worksheet with a consistent, professional layout:
///   Row 1 — title band (merged, bold, colored)
///   Row 2 — generated timestamp + org region + finding count
///   Row 3 — column headers (frozen, auto-filter, bold, white-on-dark)
///   Row 4+ — data rows (alternating background)
/// </summary>
public sealed class ExcelReportService : IExcelReportService
{
    // Brand palette
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1F3864");     // dark navy
    private static readonly XLColor HeaderFg = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor TitleBg = XLColor.FromHtml("#2E75B6");      // brand blue
    private static readonly XLColor AltRowBg = XLColor.FromHtml("#EBF3FB");     // light blue tint
    private static readonly XLColor SeverityCritical = XLColor.FromHtml("#FFCCCC");
    private static readonly XLColor SeverityWarning = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor SeverityInfo = XLColor.FromHtml("#E2F0D9");

    public Task<byte[]> GenerateAsync(AuditReportData report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var wb = new XLWorkbook();

        WriteSummarySheet(wb, report);
        WriteExtDuplicatesProfileSheet(wb, report);
        WriteExtPoolVsProfileSheet(wb, report);
        WriteDidMismatchSheet(wb, report);
        WriteEmptyGroupsSheet(wb, report);
        WriteEmptyQueuesSheet(wb, report);
        WriteStaleFlowsSheet(wb, report);
        WriteInactiveUsersSheet(wb, report);
        WriteInvalidExtensionsSheet(wb, report);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    // ─── Summary ────────────────────────────────────────────────────────────

    private static void WriteSummarySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Summary");

        var er = report.ExtensionReport;

        var rows = new[]
        {
            ("Extension Duplicates (Profile)", er.DuplicateProfileExtensions.Count, "Critical", "Multiple users share the same work-phone extension"),
            ("Extension Pool vs Profile Mismatches", er.ProfileExtensionsNotAssigned.Count + er.AssignedExtensionsMissingFromProfiles.Count, "Warning", "Extensions in pool not on profiles, or on profiles not in pool"),
            ("Duplicate Extension Assignments", er.DuplicateAssignedExtensions.Count, "Critical", "Same extension assigned more than once in the telephony pool"),
            ("Invalid Profile Extensions", er.InvalidProfileExtensions.Count, "Warning", "Extension values on user profiles that failed normalization"),
            ("Invalid Pool Extensions", er.InvalidAssignedExtensions.Count, "Warning", "Extension values in pool that failed normalization"),
            ("Empty Groups", report.GroupFindings.Count(f => f.Issue.Contains("Empty")), "Warning", "Groups with zero members"),
            ("Single-Member Groups", report.GroupFindings.Count(f => f.Issue.Contains("Single")), "Info", "Groups with only one member — review if intentional"),
            ("Empty Queues", report.QueueFindings.Count(f => f.Issue.Contains("Empty")), "Critical", "Routing queues with no agents"),
            ("Duplicate Queue Names", report.QueueFindings.Count(f => f.Issue.Contains("Duplicate")), "Warning", "Queues with identical names (case-insensitive)"),
            ("Stale/Unpublished Flows", report.FlowFindings.Count, "Warning", $"Flows not republished in {report.Options.StaleFlowThresholdDays}+ days or never published"),
            ("Inactive Users (No Recent Login)", report.InactiveUserFindings.Count, "Warning", $"Users with no OAuth login in {report.Options.InactiveUserThresholdDays}+ days"),
            ("DID Mismatches", report.DidFindings.Count, "Warning", "DIDs unassigned, orphaned, or assigned to inactive users"),
        };

        var totalFindings = rows.Sum(r => r.Item2);

        string[] headers = ["Check", "Findings", "Severity", "Description"];
        var ws2 = WriteSheetHeader(ws, "Genesys Cloud Audit — Executive Summary",
            report, totalFindings, headers);

        int row = 4;
        foreach (var (check, count, severity, desc) in rows)
        {
            ws.Cell(row, 1).Value = check;
            ws.Cell(row, 2).Value = count;
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = severity;
            ws.Cell(row, 4).Value = desc;

            // Color-code severity + row
            var rowRange = ws.Range(row, 1, row, 4);
            var severityCell = ws.Cell(row, 3);
            if (severity == "Critical")
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (severity == "Warning")
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            if (row % 2 == 0)
            {
                foreach (var cell in rowRange.Cells().Where(c => c.Address.ColumnNumber != 3))
                    cell.Style.Fill.BackgroundColor = AltRowBg;
            }

            row++;
        }

        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        AdjustColumns(ws, 4, minWidth: 20, maxWidth: 80);
        ws.Column(1).Width = 42;
    }

    // ─── Extension Duplicates (Profile) ────────────────────────────────────

    private static void WriteExtDuplicatesProfileSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Ext_Duplicates_Profile");
        var findings = report.ExtensionReport.DuplicateProfileExtensions;

        string[] headers = ["Extension", "User Name", "User ID", "State", "Extension (Raw)"];
        WriteSheetHeader(ws, "Duplicate Extensions — User Profiles", report, findings.Count, headers);

        int row = 4;
        foreach (var finding in findings)
        {
            foreach (var user in finding.Users)
            {
                WriteRow(ws, row, finding.ExtensionKey, user.UserName, user.UserId, user.State, user.ExtensionRaw);
                ApplyAltRow(ws, row, 5);
                row++;
            }
        }

        AdjustColumns(ws, 5);
    }

    // ─── Extension Pool vs Profile ──────────────────────────────────────────

    private static void WriteExtPoolVsProfileSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Ext_Pool_vs_Profile");
        var er = report.ExtensionReport;
        var totalCount = er.ProfileExtensionsNotAssigned.Count + er.AssignedExtensionsMissingFromProfiles.Count;

        string[] headers = ["Extension Key", "Issue Type", "Assignment ID", "User Name", "User ID", "Target Type"];
        WriteSheetHeader(ws, "Extension Pool vs Profile Mismatches", report, totalCount, headers);

        int row = 4;
        foreach (var finding in er.ProfileExtensionsNotAssigned)
        {
            foreach (var user in finding.Users)
            {
                WriteRow(ws, row, finding.ExtensionKey, "On profile, not in pool", "", user.UserName, user.UserId, "");
                ApplyAltRow(ws, row, 6);
                ws.Cell(row, 3).Style.Fill.BackgroundColor = SeverityWarning;
                row++;
            }
        }

        foreach (var finding in er.AssignedExtensionsMissingFromProfiles)
        {
            foreach (var a in finding.Assignments)
            {
                WriteRow(ws, row, finding.ExtensionKey, "In pool, not on any profile", a.AssignmentId, "", "", a.TargetType);
                ApplyAltRow(ws, row, 6);
                ws.Cell(row, 3).Style.Fill.BackgroundColor = SeverityInfo;
                row++;
            }
        }

        AdjustColumns(ws, 6);
    }

    // ─── DID Mismatches ─────────────────────────────────────────────────────

    private static void WriteDidMismatchSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("DID_Mismatches");
        var findings = report.DidFindings;

        string[] headers = ["Phone Number", "Pool ID", "Owner Type", "Owner ID", "Owner Name", "Issue"];
        WriteSheetHeader(ws, "DID Mismatches", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            WriteRow(ws, row, f.PhoneNumber, f.PoolId, f.OwnerType, f.OwnerId, f.OwnerName, f.Issue);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        AdjustColumns(ws, 6);
    }

    // ─── Empty Groups ───────────────────────────────────────────────────────

    private static void WriteEmptyGroupsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Empty_Groups");
        var findings = report.GroupFindings;

        string[] headers = ["Group Name", "Group ID", "Type", "State", "Members", "Last Modified", "Issue"];
        WriteSheetHeader(ws, "Groups — Empty or Single-Member", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.GroupName;
            ws.Cell(row, 2).Value = f.GroupId;
            ws.Cell(row, 3).Value = f.Type;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.MemberCount;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.DateModified?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            if (f.MemberCount == 0)
                ws.Cell(row, 5).Style.Fill.BackgroundColor = SeverityCritical;
            row++;
        }

        AdjustColumns(ws, 7);
    }

    // ─── Empty Queues ───────────────────────────────────────────────────────

    private static void WriteEmptyQueuesSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Empty_Queues");
        var findings = report.QueueFindings;

        string[] headers = ["Queue Name", "Queue ID", "Description", "Members", "Issue"];
        WriteSheetHeader(ws, "Queues — Empty or Duplicate Names", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.QueueName;
            ws.Cell(row, 2).Value = f.QueueId;
            ws.Cell(row, 3).Value = f.Description;
            ws.Cell(row, 4).Value = f.MemberCount;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 5).Value = f.Issue;
            ApplyAltRow(ws, row, 5);
            if (f.MemberCount == 0)
                ws.Cell(row, 4).Style.Fill.BackgroundColor = SeverityCritical;
            row++;
        }

        AdjustColumns(ws, 5);
    }

    // ─── Stale Flows ─────────────────────────────────────────────────────────

    private static void WriteStaleFlowsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Stale_Flows");
        var findings = report.FlowFindings;

        string[] headers = ["Flow Name", "Flow ID", "Type", "Published Date", "Days Since Published", "Last Modified", "Issue"];
        WriteSheetHeader(ws, $"Architect Flows — Stale (>{report.Options.StaleFlowThresholdDays} days) or Never Published",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FlowName;
            ws.Cell(row, 2).Value = f.FlowId;
            ws.Cell(row, 3).Value = f.FlowType;
            ws.Cell(row, 4).Value = f.PublishedDate?.ToString("yyyy-MM-dd") ?? "Never";
            ws.Cell(row, 5).Value = f.DaysSincePublished.HasValue ? f.DaysSincePublished.Value : "N/A";
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.DateModified?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            if (!f.IsPublished)
                ws.Cell(row, 4).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 7);
    }

    // ─── Inactive Users ──────────────────────────────────────────────────────

    private static void WriteInactiveUsersSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Inactive_Users");
        var findings = report.InactiveUserFindings;

        string[] headers = ["User Name", "User ID", "Email", "State", "Last Login (Token)", "Days Since Login", "Issue"];
        WriteSheetHeader(ws, $"Users — No Login in {report.Options.InactiveUserThresholdDays}+ Days",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.TokenLastIssuedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
            ws.Cell(row, 6).Value = f.DaysSinceLogin.HasValue ? f.DaysSinceLogin.Value : "N/A";
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            if (f.TokenLastIssuedDate is null)
                ws.Cell(row, 5).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 7);
    }

    // ─── Invalid Extensions ──────────────────────────────────────────────────

    private static void WriteInvalidExtensionsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Invalid_Extensions");
        var er = report.ExtensionReport;
        var totalCount = er.InvalidProfileExtensions.Count + er.InvalidAssignedExtensions.Count;

        string[] headers = ["Source", "ID", "Name/Description", "Extension (Raw)", "Problem", "Notes"];
        WriteSheetHeader(ws, "Invalid Extension Values", report, totalCount, headers);

        int row = 4;
        foreach (var f in er.InvalidProfileExtensions)
        {
            WriteRow(ws, row, "Profile", f.UserId, f.UserName, f.ExtensionRaw, f.Status.ToString(), f.Notes);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        foreach (var f in er.InvalidAssignedExtensions)
        {
            WriteRow(ws, row, "Pool", f.AssignmentId, "", f.ExtensionRaw, f.Status.ToString(), f.Notes);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        AdjustColumns(ws, 6);
    }

    // ─── Shared helpers ──────────────────────────────────────────────────────

    private static IXLWorksheet WriteSheetHeader(
        IXLWorksheet ws,
        string title,
        AuditReportData report,
        int findingCount,
        string[] headers)
    {
        int colCount = headers.Length;

        // Row 1: title band
        var titleRange = ws.Range(1, 1, 1, colCount);
        titleRange.Merge();
        titleRange.Style.Fill.BackgroundColor = TitleBg;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontColor = HeaderFg;
        titleRange.Style.Font.FontSize = 14;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 24;
        ws.Cell(1, 1).Value = $"  {title}";

        // Row 2: metadata
        var metaRange = ws.Range(2, 1, 2, colCount);
        metaRange.Merge();
        metaRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D6E4F0");
        metaRange.Style.Font.FontSize = 10;
        metaRange.Style.Font.Italic = true;
        ws.Cell(2, 1).Value =
            $"  Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}   |   Org: {report.OrgRegion}   |   Findings: {findingCount}";

        // Row 3: column headers
        for (int c = 1; c <= colCount; c++)
        {
            var cell = ws.Cell(3, c);
            cell.Value = headers[c - 1];
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderFg;
            cell.Style.Font.FontSize = 10;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Row(3).Height = 18;
        ws.SheetView.FreezeRows(3);
        ws.RangeUsed()?.SetAutoFilter();

        return ws;
    }

    private static void WriteRow(IXLWorksheet ws, int row, params object?[] values)
    {
        for (int c = 0; c < values.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = values[c] switch
            {
                null => "",
                string s => s,
                int i => (XLCellValue)i,
                _ => values[c]?.ToString() ?? ""
            };
        }
    }

    private static void ApplyAltRow(IXLWorksheet ws, int row, int colCount)
    {
        if (row % 2 == 0)
        {
            for (int c = 1; c <= colCount; c++)
            {
                var cell = ws.Cell(row, c);
                // Only apply if not already colored
                if (cell.Style.Fill.BackgroundColor == XLColor.NoColor
                    || cell.Style.Fill.BackgroundColor == XLColor.Transparent)
                {
                    cell.Style.Fill.BackgroundColor = AltRowBg;
                }
            }
        }
    }

    private static void AdjustColumns(IXLWorksheet ws, int colCount, int minWidth = 10, int maxWidth = 60)
    {
        for (int c = 1; c <= colCount; c++)
        {
            ws.Column(c).AdjustToContents();
            if (ws.Column(c).Width < minWidth) ws.Column(c).Width = minWidth;
            if (ws.Column(c).Width > maxWidth) ws.Column(c).Width = maxWidth;
        }
    }
}
