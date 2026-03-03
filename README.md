# Genesys Cloud Auditor — WPF Desktop App (.NET 8)

Audits **Genesys Cloud** configuration and activity data (extensions, groups, queues, flows, inactive users, DIDs, audit logs, operational event logs, and outbound events) in a navigable WPF UI with Excel export.

---

## Architecture

```text
┌────────────────────────────────────────────────────────────────────┐
│  GenesysExtensionAudit.App  (WPF, net8.0-windows)                 │
│  ├─ App.xaml / Bootstrapper.cs  (DI wiring, host lifecycle)       │
│  ├─ MainWindow.xaml             (shell: TabControl navigation)     │
│  ├─ Views/RunAuditView.xaml     (Start/Cancel, progress, results)  │
│  └─ ViewModels/                                                    │
│     ├─ MainViewModel            (shell, navigation)                │
│     └─ AuditRunViewModel        (run controls, progress, errors)   │
├────────────────────────────────────────────────────────────────────┤
│  GenesysExtensionAudit.Core  (class library, net8.0)              │
│  ├─ Application/                                                   │
│  │  ├─ IAuditRunner / AuditOptions / AuditProgress / AuditResult  │
│  ├─ Domain/Services/                                               │
│  │  ├─ ExtensionNormalization   (normalize/validate ext strings)   │
│  │  ├─ IExtensionNormalizer / ExtensionNormalizer                  │
│  │  ├─ IAuditAnalyzer / AuditAnalyzer  (cross-reference logic)    │
│  ├─ Domain/Models/                                                 │
│  │  └─ UserProfileExtensionRecord, AssignedExtensionRecord,        │
│  │     AuditFindings (DuplicateProfileExtension, etc.)            │
│  └─ Domain/Paging/  IPaginator, PagedResult<T>                    │
├────────────────────────────────────────────────────────────────────┤
│  GenesysExtensionAudit.Infrastructure  (class library, net8.0)    │
│  ├─ Application/AuditRunner    (orchestrates fetch + analyze)      │
│  ├─ Domain/Services/           ExtensionNormalizer, AuditAnalyzer  │
│  ├─ Http/                                                          │
│  │  ├─ GenesysRegionOptions    (Genesys:Region, PageSize, etc.)   │
│  │  ├─ ITokenProvider / TokenProvider   (OAuth client-creds)      │
│  │  ├─ OAuthBearerHandler      (attaches Bearer token)            │
│  │  ├─ HttpLoggingHandler      (request/response telemetry)       │
│  │  └─ RateLimitHandler        (token-bucket throttle)            │
│  ├─ Genesys/Clients/           IGenesysUsersClient (+ impl)        │
│  │                             IGenesysExtensionsClient (+ impl)   │
│  ├─ Genesys/Pagination/        Paginator (sequential page fetch)   │
│  ├─ Genesys/Dtos/              UserDto, ExtensionDto, page wrappers │
│  ├─ Logging/                   Serilog config, redaction utils     │
│  └─ Reporting/ExportService    (CSV export, Excel-friendly)        │
└────────────────────────────────────────────────────────────────────┘

Genesys Cloud API endpoints consumed
  Users:       GET /api/v2/users?pageSize={n}&pageNumber={p}[&state=active]
  Extensions:  GET /api/v2/telephony/providers/edges/extensions?pageSize={n}&pageNumber={p}
```

**Project dependency graph:**

```text
App  ──► Core
App  ──► Infrastructure
Infrastructure ──► Core
(Tests reference Core + Infrastructure)
```

---

## Implementation Status

| Component | Status | Notes |
| --- | --- | --- |
| OAuth `TokenProvider` (`client_credentials`) | ✅ Complete | Real token POST to `https://login.{Region}/oauth/token`, cached until expiry minus safety window |
| DTO → domain mapping in `AuditRunner` | ✅ Complete | Maps users/extensions to normalized domain records before analyze |
| `AuditAnalyzer` implementation | ✅ Complete | Produces duplicate profile, duplicate assigned, and profile-only findings |
| Results display in `RunAuditView` | ✅ Complete | Last run summary grid is bound and shown after completion |
| Export button / picker | ✅ Complete | Auto save prompt after run + `Export Last Report...` command |
| Serilog wiring in app startup | ✅ Complete | `Logging.ConfigureSerilog(hostBuilder)` is called in `Bootstrapper` |
| 429 `Retry-After` handling | ✅ Complete | Parses real `Retry-After` response header (delta/date) with body fallback |
| Help command / audit path documentation | ✅ Complete | Help button enabled and lists all currently available audit paths |

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Genesys Cloud OAuth setup](#genesys-cloud-oauth-setup)
- [Required permissions (OAuth scopes)](#required-genesys-cloud-permissions-oauth-scopes)
- [Configuration](#configuration)
- [Build and run](#build-and-run)
- [Running an audit](#running-an-audit)
- [Scheduling audits](#scheduling-audits)
- [Interpreting the reports](#interpreting-the-reports)
- [Exporting results to Excel](#exporting-results-to-excel)
- [Troubleshooting](#troubleshooting)
- [Developer guide](#developer-guide)
- [Notes and limitations](#notes-and-limitations)

---

## Prerequisites

- Windows 10/11
- .NET SDK **8.x** (for building from source)
- A Genesys Cloud org with an OAuth client (Client Credentials type)

---

## Genesys Cloud OAuth setup

1. In Genesys Cloud Admin → Integrations → OAuth, create an OAuth client:
   - **Grant Type:** Client Credentials
2. Record the **Client ID** and **Client Secret**.
3. Assign the required permissions to the OAuth client's roles (see below).

> The app uses **Client Credentials** flow. No user login required.

---

## Required Genesys Cloud permissions (OAuth scopes)

| Permission | Endpoint |
| --- | --- |
| `user:view` (or read users) | `GET /api/v2/users` |
| `telephony:plugin:all` or read edge extensions | `GET /api/v2/telephony/providers/edges/extensions` |

Exact permission names depend on org setup and Genesys UI wording. If you receive **403 Forbidden**, verify role assignments on the OAuth client.

---

## Configuration

### appsettings.json

Located at `src/GenesysExtensionAudit.App/appsettings.json`:

```json
{
  "Genesys": {
    "Region": "mypurecloud.com",
    "PageSize": 100,
    "IncludeInactive": false,
    "MaxRequestsPerSecond": 3
  },
  "GenesysOAuth": {
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

| Setting | Description |
| --- | --- |
| `Genesys:Region` | Your org's API domain — e.g. `mypurecloud.com`, `usw2.pure.cloud`, `euw2.pure.cloud` |
| `Genesys:PageSize` | Records per page (1–500). Genesys caps vary by endpoint; 100 is safe. |
| `Genesys:IncludeInactive` | `false` → `&state=active` filter applied. `true` → all users (including inactive). |
| `Genesys:MaxRequestsPerSecond` | Throttle to avoid 429 rate-limit errors. |
| `GenesysOAuth:ClientId` | OAuth client ID (or use user-secrets). |
| `GenesysOAuth:ClientSecret` | OAuth client secret (or use user-secrets). |

### Secrets for local development (user-secrets)

**Never commit credentials.** Use .NET user-secrets:

```powershell
cd src\GenesysExtensionAudit.App
dotnet user-secrets set "GenesysOAuth:ClientId"     "YOUR_CLIENT_ID"
dotnet user-secrets set "GenesysOAuth:ClientSecret" "YOUR_CLIENT_SECRET"
```

User-secrets are loaded automatically when `DOTNET_ENVIRONMENT=Development`.

### Environment variables (CI/packaging)

Override any config key using `__` as the section separator:

```powershell
setx GenesysOAuth__ClientId     "YOUR_CLIENT_ID"
setx GenesysOAuth__ClientSecret "YOUR_CLIENT_SECRET"
setx Genesys__Region            "mypurecloud.com"
```

---

## Build and run

```powershell
# From repo root
dotnet restore
dotnet build -c Release
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

To run tests:

```powershell
dotnet test
```

---

## Running an audit

1. Launch the application.
2. Verify settings in the **Run Audit** tab:
   - **Region** is correct for your org
   - **IncludeInactive** is set as desired
   - **PageSize** is appropriate (100 is safe; increase for faster large-tenant runs)
3. Click **Start** to begin the audit.
4. Monitor:
   - **Status** bar (current phase, page counts)
   - **Progress** bar (0–100%)
5. Click **Cancel** to abort mid-run (safe — no partial export is written).

**What is fetched (in parallel where possible):**

| Data | Endpoint | State filter |
| --- | --- | --- |
| Users | `/api/v2/users?pageSize={n}&pageNumber={p}` | `&state=active` when `IncludeInactive=false` |
| Edge extensions | `/api/v2/telephony/providers/edges/extensions?pageSize={n}&pageNumber={p}` | None |

---

## Scheduling audits

Use the **Schedule Audits** tab to create local Windows Scheduled Tasks that run the audit headlessly.

Supported recurrence:

- One-time
- Daily
- Weekly (selected weekdays)

Scheduling flow:

1. Configure recurrence, date/time, credentials, and selected audit paths.
2. Click **Create Scheduled Task**.
3. The app writes a schedule profile JSON and registers a task under `\GenesysExtensionAudit\`.
4. The task runs `GenesysExtensionAudit.Runner.exe --schedule-profile "<profile-path>"`.

Notes:

- The GUI does not need to be open when the task runs.
- Existing one-off runs from **Run Audit** are unchanged.
- If runner auto-discovery fails, set `Scheduling:RunnerExecutablePath` in `src/GenesysExtensionAudit.App/appsettings.json`.

---

## Interpreting the reports

### Summary

Quick health check totals:

| Metric | Healthy baseline |
| --- | --- |
| `DuplicateProfileExtensions` | 0 — any > 0 means multiple users share an extension value |
| `ProfileExtensionsNotAssigned` | Low — extensions set on profiles that don't exist in telephony |
| `DuplicateAssignedExtensions` | 0 — same extension assigned more than once at telephony layer |
| `AssignedExtensionsMissingFromProfiles` | Informational — assigned extensions with no matching user profile |
| `InvalidProfileExtensions` | 0 — malformed/non-numeric extension values on user profiles |

### Duplicates By Profile (Work Phone Extension)

**What:** Multiple users have the same value in their Work Phone extension field.

**Why it matters:** Duplicate values cause call routing ambiguity, failed provisioning, and inaccurate reporting.

**How to fix:**

1. Identify which user legitimately owns the extension.
2. Clear or correct the extension field on the other users.
3. Re-run the audit to confirm zero findings.

### Extensions On Profiles But Not Assigned

**What:** A user's profile extension value has no corresponding entry in the Edge extension assignment list.

**Common causes:**

- Profile field manually edited after telephony deprovisioning
- Extension deleted/recycled in telephony but not cleared on the profile
- Org uses a telephony model that doesn't fully reflect in the Edge extensions endpoint

**How to fix:**

- If extension should exist: recreate it in Genesys telephony.
- If extension is stale: clear the user's Work Phone extension field.

### Other exported sections

| Section | Meaning |
| --- | --- |
| `DuplicateAssignedExtensions` | Same extension key appears on multiple telephony assignments |
| `AssignedExtensionsMissingFromProfiles` | Assigned extension has no corresponding user profile value (optional) |
| `InvalidProfileExtensions` | Non-numeric or whitespace-only values on user profiles |
| `InvalidAssignedExtensions` | Malformed extension values in telephony assignments |

---

## Exporting results to Excel

After an audit completes, the app prompts for a `.xlsx` save location.

You can also click **Export Last Report...** to re-export the latest completed run without rerunning the audit.

Workbook output includes:

- `Summary` (audits performed, item counts, severity, run timing)
- `Ext_Duplicates_Profile`
- `Ext_Pool_vs_Profile`
- `Invalid_Extensions`
- `Empty_Groups`
- `Empty_Queues`
- `Stale_Flows`
- `Inactive_Users`
- `DID_Mismatches`
- `Audit_Logs` (when Audit Logs path is selected)
- `Operational_Events` (when Operational Event Logs path is selected)
- `Outbound_Events` (when OutboundEvents path is selected)

---

## Troubleshooting

### 401 Unauthorized / 403 Forbidden

| Check | Action |
| --- | --- |
| Client ID/Secret correct | Verify in appsettings or user-secrets (no trailing whitespace) |
| Region matches org | `Genesys:Region` must match your org's API domain |
| OAuth grant type | Must be **Client Credentials** |
| Role permissions | Ensure the OAuth client's roles include user-read and telephony-read |

The client will automatically retry a 401 once after forcing a token refresh.

### 429 Too Many Requests

- Lower `Genesys:MaxRequestsPerSecond` (try 1 or 2)
- The app respects `Retry-After` on 429 responses and retries with exponential backoff (max 6 attempts, cap 30s)
- Avoid running during peak admin/provisioning windows

### Audit is slow / large tenants

- Increase `Genesys:PageSize` (up to API cap, typically 200–500 for users)
- The `PagingOrchestrator` uses bounded-parallel page fetching; tune `MaxParallelRequests` in options
- Run during off-hours

### No results / missing users

- `IncludeInactive=false` excludes inactive users — set `true` to include them
- Users with blank Work Phone extension fields never appear in profile-based findings
- Verify the OAuth client has read access to all users (some orgs have division-scoped permissions)

### Export workbook issues

- Confirm you can write to the selected output folder.
- If export is skipped, click **Export Last Report...** and select a writable path.
- Very large tenants can create large workbooks; retry with fewer audit paths selected if Excel struggles to open the file.

### TLS/Proxy/Firewall issues

- Verify outbound HTTPS to `api.{Region}` and `login.{Region}` from the workstation
- .NET respects system proxy settings (IE/WinHTTP proxy); no extra config needed in most environments
- Check firewall rules allow outbound 443

---

## Developer Guide

### Implementation Notes

- App startup uses `Logging.ConfigureSerilog(hostBuilder)` in `Bootstrapper` and supports `Logging:*` options in `appsettings.json`.
- OAuth uses real `client_credentials` flow in `TokenProvider` with cached token reuse and force refresh on demand.
- API resiliency in `GenesysCloudApiClient` includes 401 refresh retry and 429 retry honoring `Retry-After` header values.
- `RunAuditViewModel` now stores the last completed report, binds a summary grid, and supports explicit re-export via **Export Last Report...**.

### Extension normalization rules

`ExtensionNormalization.Normalize()` applies this pipeline (configured via `ExtensionNormalizationOptions`):

1. **Trim** whitespace
2. **Strip non-digits** (keeps leading zeros by default)
3. **Validate** result is non-empty and within digit-length bounds
4. Returns `ExtensionNormalizationResult` with `IsOk`, `Normalized`, `Status`, `Notes`

Status values: `Ok`, `Empty`, `WhitespaceOnly`, `NonDigitOnly`, `TooShort`, `TooLong`

### Running tests

```powershell
dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\
```

The test suite covers:

- Users pagination with `state=active` / without
- Extensions pagination across multiple pages
- Mock handler that verifies exact call counts and URLs

See `QA.md` for the full end-to-end QA test matrix (pagination, rate-limit, cancellation, export, UI).

---

## Notes and limitations

- The audit uses the **Work Phone extension field** (`primaryContactInfo` with type=work + mediaType=PHONE) as the profile source of truth. Orgs that store extensions differently (custom fields, division-scoped schemas) may need adapter logic in `DtosExtensions.GetWorkPhoneExtension()`.
- Genesys tenant configurations vary significantly. Treat findings as audit aids; validate against your telephony provisioning model before bulk changes.
- The `AssignedExtensionsMissingFromProfiles` report is enabled in the current extension audit run. Large tenants may produce high row counts in that section.
- `ResultsViews.xaml` uses `ItemsControl` + `DataGrid` per finding — for tenants with thousands of findings, enable UI virtualization or add paging in the view.

---

## License

Add your license here (MIT/Apache-2.0/etc.), or remove this section if not applicable.
