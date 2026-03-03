# Genesys Extension Audit (Windows Desktop App)

A Windows (WPF) desktop application that audits **Genesys Cloud** extension data to identify:

- **Duplicate extension assignments based on user profile Work Phone extension**
- **Extensions present on user profiles but not assigned** in the Edge extension assignment list
- (Additionally exported/reportable if enabled in the build) duplicate assigned extensions, assigned extensions missing from profiles, and invalid extension values.

This tool focuses on these Genesys Cloud endpoints:

- Users:
  - `GET /api/v2/users?pageSize={PageSize}&pageNumber={n}&state=active` (when `IncludeInactive=false`)
  - `GET /api/v2/users?pageSize={PageSize}&pageNumber={n}` (when `IncludeInactive=true`)
- Edge Extensions:
  - `GET /api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={n}`

---

## Table of contents

- [Genesys Extension Audit (Windows Desktop App)](#genesys-extension-audit-windows-desktop-app)
  - [Table of contents](#table-of-contents)
  - [Prerequisites](#prerequisites)
  - [Genesys Cloud OAuth setup](#genesys-cloud-oauth-setup)
  - [Required Genesys Cloud permissions (OAuth scopes)](#required-genesys-cloud-permissions-oauth-scopes)
  - [Configuration](#configuration)
    - [appsettings.json](#appsettingsjson)
    - [Secrets for local development (user-secrets)](#secrets-for-local-development-user-secrets)
    - [Environment variables (recommended for CI/packaging)](#environment-variables-recommended-for-cipackaging)
  - [Build and run](#build-and-run)
  - [Running an audit](#running-an-audit)
  - [Interpreting the reports](#interpreting-the-reports)
    - [Summary](#summary)
    - [Duplicates By Profile (Work Phone Extension)](#duplicates-by-profile-work-phone-extension)
    - [Extensions On Profiles But Not Assigned](#extensions-on-profiles-but-not-assigned)
    - [Other exported sections (if present)](#other-exported-sections-if-present)
  - [Exporting results to CSV](#exporting-results-to-csv)
  - [Troubleshooting](#troubleshooting)
    - [401 Unauthorized / 403 Forbidden](#401-unauthorized--403-forbidden)
    - [429 Too Many Requests (rate limiting)](#429-too-many-requests-rate-limiting)
    - [Audit is slow / large tenants](#audit-is-slow--large-tenants)
    - [No results / missing users](#no-results--missing-users)
    - [CSV opens incorrectly in Excel](#csv-opens-incorrectly-in-excel)
    - [TLS/Proxy/Firewall issues](#tlsproxyfirewall-issues)
  - [Notes and limitations](#notes-and-limitations)
  - [License](#license)

---

## Prerequisites

- Windows 10/11
- .NET SDK **8.x** (for building/running from source)
- A Genesys Cloud organization with permission to create an OAuth client (or access to an existing one)

---

## Genesys Cloud OAuth setup

1. In Genesys Cloud Admin, create an OAuth client:
   - Client type: **Client Credentials**
2. Record:
   - **Client ID**
   - **Client Secret**
3. Ensure your OAuth client (or the roles of the user used to create it) includes the permissions listed below.

> This application authenticates using the **Client Credentials** flow.

---

## Required Genesys Cloud permissions (OAuth scopes)

The app needs read access to:

- Users list (for Work Phone extension field):
  - `GET /api/v2/users`
- Edge extensions list:
  - `GET /api/v2/telephony/providers/edges/extensions`

Genesys Cloud permissions are typically granted via **roles** and OAuth client configuration. Exact permission names can vary by org setup and Genesys UI wording, but at minimum you must be able to:
- Read users
- Read telephony/edge extensions

If you receive **403 Forbidden**, see [Troubleshooting](#401-unauthorized--403-forbidden).

---

## Configuration

### appsettings.json

Non-secret settings are stored in:

`src/GenesysExtensionAudit.App/appsettings.json`

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

**Settings:**

- `Genesys:Region`
  Your Genesys Cloud region domain, for example:
  - `mypurecloud.com`
  - `usw2.pure.cloud`
  - `euw2.pure.cloud`

- `Genesys:PageSize`
  Number of records per page when calling Genesys endpoints. Typical values: `100`–`500`.
  (Genesys APIs often cap page sizes; if you set it too high the app may clamp it or the API may reject it.)

- `Genesys:IncludeInactive`
  - `false` (default): users are requested with `&state=active`
  - `true`: users are requested without the `state=active` filter (includes inactive users)

- `Genesys:MaxRequestsPerSecond`
  Throttling control to reduce the chance of 429 rate limiting.

### Secrets for local development (user-secrets)

Do **not** commit client credentials into source control. For local development, use **user-secrets**:

```powershell
cd .\src\GenesysExtensionAudit.App\
dotnet user-secrets set "GenesysOAuth:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "GenesysOAuth:ClientSecret" "YOUR_CLIENT_SECRET"
```

User secrets are loaded when the environment is `Development`.

### Environment variables (recommended for CI/packaging)

You can override any config setting via environment variables:

- `GenesysOAuth__ClientId`
- `GenesysOAuth__ClientSecret`
- `Genesys__Region`
- `Genesys__PageSize`
- `Genesys__IncludeInactive`
- `Genesys__MaxRequestsPerSecond`

Example:

```powershell
setx GenesysOAuth__ClientId "YOUR_CLIENT_ID"
setx GenesysOAuth__ClientSecret "YOUR_CLIENT_SECRET"
setx Genesys__Region "mypurecloud.com"
```

---

## Build and run

From the repository root:

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project .\src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

---

## Running an audit

1. Launch the application.
2. Confirm configuration:
   - Region is correct
   - IncludeInactive is set as desired
   - PageSize is appropriate for your tenant size
3. Click **Start** to run the audit.
4. Watch:
   - Status/progress indicators (if enabled in your build)
   - Results tabs/views update when complete
5. Optionally click **Cancel** during the run.

**What the app fetches:**

- All users across all pages, using:
  - `.../api/v2/users?pageSize={PageSize}&pageNumber={n}&state=active` when `IncludeInactive=false`
  - `.../api/v2/users?pageSize={PageSize}&pageNumber={n}` when `IncludeInactive=true`
- All Edge extensions across all pages using:
  - `.../api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={n}`

---

## Interpreting the reports

### Summary

The Summary section aggregates counts such as:

- Total users considered
- Total assignments considered
- Number of findings in each category

Use this as a quick health check:

- High duplicate counts may indicate provisioning drift or bulk updates gone wrong.
- Many “profile but not assigned” findings often indicate profile data is out of sync with telephony assignments.

### Duplicates By Profile (Work Phone Extension)

**What it means:**
More than one user profile contains the **same Work Phone extension** value.

**Why it matters:**

- Duplicate extensions can cause call routing confusion, failed provisioning, or reporting inaccuracies.
- Even if assignments are correct, profile values may be wrong or copied.

**How to fix:**

- Choose the correct user for the extension.
- Update other users’ Work Phone extension values to the correct extension (or blank).
- Re-run the audit to confirm.

### Extensions On Profiles But Not Assigned

**What it means:**
A user profile contains a Work Phone extension value, but that extension **does not appear** in the Edge extensions assignment list retrieved from:

`/api/v2/telephony/providers/edges/extensions`

**Common causes:**

- User profile extension field manually edited and no longer matches real assignment
- Extensions were deleted/recycled
- The org uses a different telephony approach and the Edge extensions endpoint doesn’t reflect the actual provisioning source

**How to fix:**

- If the extension is supposed to exist: ensure it is created/assigned in Genesys telephony.
- If the extension is not real: clear or correct the user’s profile extension field.

### Other exported sections (if present)

Depending on the build/version, exports may also include:

- **DuplicateAssignedExtensions**: same assigned extension appears on multiple assignments
- **AssignedExtensionsMissingFromProfiles**: assigned extensions exist but are missing from user profiles
- **InvalidProfileExtensions / InvalidAssignedExtensions**: values that don’t match expected formatting rules (for example, non-numeric or whitespace-only)

---

## Exporting results to CSV

The application includes a CSV export capability (Excel-friendly) that writes:

- `Summary.csv`
- `DuplicateProfileExtensions.csv`
- `ProfileExtensionsNotAssigned.csv`
- `DuplicateAssignedExtensions.csv`
- `AssignedExtensionsMissingFromProfiles.csv`
- `InvalidProfileExtensions.csv`
- `InvalidAssignedExtensions.csv`

**Excel-friendly behavior:**

- UTF-8 BOM can be included (recommended for Excel)
- Values are written as CSV columns so you can filter/sort/pivot

If the UI has an **Export** button:

1. Run an audit.
2. Click Export and choose an output directory (or accept the default).
3. Open the files in Excel.

If your build exposes export via code only, see `ExportService` in the source tree and wire it into the UI (typical output is a timestamped file prefix).

---

## Troubleshooting

### 401 Unauthorized / 403 Forbidden

**Symptoms:**

- Audit fails immediately
- Error indicates Unauthorized/Forbidden

**Checks:**

- Client ID/Secret are correct (no extra whitespace)
- `Genesys:Region` matches your org region
- OAuth client is configured for **Client Credentials**
- The OAuth client/roles have permission to:
  - read users
  - read telephony/edge extensions

### 429 Too Many Requests (rate limiting)

**Symptoms:**

- Audit slows down or fails with 429 responses

**Mitigations:**

- Lower request rate:
  - Reduce `Genesys:MaxRequestsPerSecond`
- Avoid running during peak admin/provisioning periods
- Re-run later

The app is expected to respect `Retry-After` when returned and retry transient failures up to configured limits.

### Audit is slow / large tenants

**Why:**

- The app must page through all users and all extensions.
- Large tenants can involve hundreds of pages.

**Suggestions:**

- Use a larger `PageSize` (up to the API limit; commonly 200–500)
- Keep `MaxRequestsPerSecond` conservative to reduce 429 retries
- Run during low-usage periods

### No results / missing users

**If IncludeInactive=false:**

- Only `state=active` users are included.
- Set `IncludeInactive=true` to include inactive users.

**Also check:**

- Users may have blank Work Phone extension fields; those users won’t appear in profile-based findings.

### CSV opens incorrectly in Excel

**Symptoms:**

- Columns shifted
- Quotes/newlines appear wrong

**Checks:**

- Ensure you open the CSV using Excel’s import if your locale expects semicolons instead of commas.
- If names/fields contain commas, quotes, or newlines, proper CSV escaping is required by the exporter. If you see malformed rows, open an issue and include a sanitized sample row.

### TLS/Proxy/Firewall issues

**Symptoms:**

- Network errors, name resolution failures, or proxy authentication prompts

**Checks:**

- Verify access to `https://api.{Region}` endpoints from the workstation
- If your environment requires a proxy, ensure .NET is configured to use it (system proxy settings typically apply)
- Confirm firewall rules allow outbound HTTPS to Genesys Cloud

---

## Notes and limitations

- The audit relies on the **Work Phone extension field** on the user profile as the “profile extension” source of truth for certain reports.
- Genesys Cloud tenant configurations vary; some organizations may manage extensions in ways that do not fully align with the Edge extensions endpoint used here.
- Results should be treated as an audit aid; always validate against your organization’s telephony provisioning model before making bulk changes.

---

## License

Add your license here (MIT/Apache-2.0/etc.), or remove this section if not applicable.
