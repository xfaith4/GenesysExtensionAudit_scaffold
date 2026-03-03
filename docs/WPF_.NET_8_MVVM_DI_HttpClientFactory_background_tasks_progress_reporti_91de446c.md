# Windows Desktop Tech Stack & Architecture (WPF/.NET 8, MVVM, DI, HttpClientFactory, background tasks, progress, cancellation)

## 1) Tech stack selection

### UI / Desktop

- **WPF** on **.NET 8 (Windows)**
- **MVVM** pattern:
  - CommunityToolkit.Mvvm (recommended) or ReactiveUI (optional).
  - Use observable properties, commands, validation, and messenger/events.

### Composition / DI / Hosting

- **Microsoft.Extensions.Hosting** (Generic Host) inside WPF:
  - `IHost` for DI container, config, and logging.
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.Configuration` (appsettings.json + user secrets optional)
  - `Microsoft.Extensions.Logging` (Serilog recommended for file logs)

### HTTP & resiliency

- **HttpClientFactory** (`IHttpClientFactory`)
- Delegating handlers for:
  - OAuth token acquisition/attach bearer
  - retry/backoff (Polly strongly recommended)
  - simple client-side rate limiting (custom handler or Polly)
  - request/response logging (without secrets)

### Background tasks & UX

- `async/await` with `CancellationToken`
- `IProgress<T>` (or `Progress<T>`) to marshal progress updates to UI thread
- Optional: `System.Threading.Channels` if streaming results/logs to UI

### Data & reporting

- JSON: `System.Text.Json`
- CSV export: `CsvHelper` (recommended)
- Excel export (optional): ClosedXML

---

## 2) High-level architecture (layers)

### Presentation (WPF)

- Views: `MainWindow`, `RunAuditView`, `SettingsView`, `ResultsView`, `LogView`
- ViewModels:
  - `RunAuditViewModel` (inputs, start/cancel, progress)
  - `ResultsViewModel` (tables + export)
  - `SettingsViewModel` (region, page size, include inactive, throttling)
- No HTTP calls in ViewModels; they call application services.

### Application layer (orchestration/use cases)

- `IAuditRunner`
  - `Task<AuditResult> RunAsync(AuditOptions options, IProgress<AuditProgress> progress, CancellationToken ct)`
- `AuditOptions`:
  - Region/BaseUrls, PageSize, IncludeInactive, LeadingZeroPolicy, etc.
- `AuditProgress`:
  - phase (UsersFetch / ExtensionsFetch / Compute / Export)
  - currentPage/totalPages (when known)
  - counts fetched, elapsed time

### Domain layer (pure logic)

- Models:
  - `UserProfileExtensionRecord { UserId, Name, State, RawExtension, NormalizedExtension }`
  - `AssignedExtensionRecord { ExtensionId, RawExtension, NormalizedExtension, AssignedToType, AssignedToId, ... }`
- Services:
  - `IExtensionNormalizer`
  - `IAuditAnalyzer` (produces duplicates/unassigned sets)
- Domain should be testable without API.

### Infrastructure layer (Genesys Cloud + storage)

- `IGenesysClient` / typed API clients:
  - `Task<PagedResult<UserDto>> GetUsersPageAsync(pageNumber, pageSize, includeInactive, ct)`
  - `Task<PagedResult<ExtensionDto>> GetExtensionsPageAsync(pageNumber, pageSize, ct)`
- OAuth:
  - `ITokenProvider` for client credentials flow
- Persistence:
  - secure storage for client secret (DPAPI/Credential Manager)
  - report writers: `IReportWriter` (CSV/JSON)

---

## 3) App composition using Generic Host in WPF

### Startup pattern

- `App.xaml.cs`:
  - Build `Host.CreateDefaultBuilder()`
  - Configure services
  - Start host
  - Resolve main window/viewmodel

### Recommended registrations (sketch)

- `services.AddSingleton<IExtensionNormalizer, ExtensionNormalizer>();`
- `services.AddSingleton<IAuditAnalyzer, AuditAnalyzer>();`
- `services.AddSingleton<IAuditRunner, AuditRunner>();`

- Http:
  - `services.AddHttpClient("GenesysApi", c => c.BaseAddress = new Uri(apiBase))`
  - `services.AddHttpClient("GenesysAuth", c => c.BaseAddress = new Uri(authBase))`

- Add handlers:
  - `AddHttpMessageHandler<OAuthBearerHandler>()`
  - `AddPolicyHandler(PollyPolicies.RetryPolicy())`
  - `AddHttpMessageHandler<RateLimitHandler>()`
  - `AddHttpMessageHandler<HttpLoggingHandler>()`

---

## 4) Genesys Cloud API integration architecture

### Typed client approach

Prefer typed clients to keep endpoints explicit and testable:

- `GenesysUsersClient : IGenesysUsersClient`
- `GenesysExtensionsClient : IGenesysExtensionsClient`

Each uses an injected `HttpClient` from factory.

### Query composition (important detail)

- Users endpoint:
  - When `IncludeInactive == false`:
    - `/api/v2/users?pageSize={PageSize}&pageNumber={page}&state=active`
  - When `IncludeInactive == true`:
    - `/api/v2/users?pageSize={PageSize}&pageNumber={page}`
    - (do **not** send `state=` empty)

- Extensions endpoint:
  - `/api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={page}`

### Pagination orchestration

Centralize paging to avoid duplicating logic:
- `IPaginator` with:
  - `IAsyncEnumerable<T> FetchAllAsync(Func<int, Task<PagedResult<T>>> getPage, ct)`
- Termination conditions:
  - use `pageCount/total` when returned
  - otherwise stop when entities count == 0
- Handle pageNumber base:
  - make configurable or detect from first response metadata.

---

## 5) Background execution, cancellation, and progress reporting

### Execution flow

`AuditRunner.RunAsync(...)` does:

1. Report progress: “Fetching users…”
2. Fetch all user pages sequentially (safer for rate limits)
   - `ct.ThrowIfCancellationRequested()` between pages
   - `progress.Report(new AuditProgress(...))`
3. Report progress: “Fetching extensions…”
4. Fetch all extension pages
5. Report progress: “Analyzing…”
6. Normalize, then compute:
   - duplicates in profiles
   - duplicates in assignments
   - unassigned (profile-only) = `U \ A`
7. Return `AuditResult` with metadata (timestamp, options, counts)

### In WPF ViewModel

- Keep a `CancellationTokenSource _cts`
- Start command:
  - disables UI inputs
  - calls `await _auditRunner.RunAsync(options, progress, _cts.Token)`
- Cancel command:
  - `_cts.Cancel()`
- Use `Progress<AuditProgress>` to update bindable VM properties (phase, percent, status text).

---

## 6) Reliability: retry/backoff, rate limiting, logging

### Retry/backoff

- Use Polly policy on the API client pipeline:
  - retry on 429/408/5xx + transient network
  - respect `Retry-After` when present
  - exponential backoff + jitter
- Special case:
  - on 401: token provider reacquires token and retry once

### Rate limiting

- Simple process-wide limiter (recommended):
  - e.g., `maxRequestsPerSecond` configurable
  - shared across both endpoints
- Keep paging sequential by default to minimize 429s.

### Logging/telemetry

- Structured logs to file:
  - request path (no query secrets), status code, elapsed, retry count
  - correlation ids if Genesys returns any headers
- UI log panel can bind to an in-memory log sink (optional).

---

## 7) Key components (suggested class list)

### Presentation

- `RunAuditViewModel`
- `ResultsViewModel`
- `SettingsViewModel`

### Application

- `AuditRunner : IAuditRunner`
- `AuditOptions`, `AuditResult`, `AuditProgress`

### Domain

- `ExtensionNormalizer`
- `AuditAnalyzer`
- domain result models:
  - `DuplicateProfileExtension`
  - `DuplicateAssignedExtension`
  - `UnassignedProfileExtension`
  - `InvalidProfileExtension` (optional report)

### Infrastructure

- `TokenProvider : ITokenProvider`
- `OAuthBearerHandler : DelegatingHandler`
- `GenesysUsersClient`
- `GenesysExtensionsClient`
- `RateLimitHandler`
- `HttpLoggingHandler`
- `ReportWriterCsv/Json`

---

## 8) UI/UX implications (minimal but practical)

- Inputs:
  - Region (api/auth base)
  - ClientId, ClientSecret (secured)
  - PageSize (clamp to API max)
  - IncludeInactive toggle
  - Normalization options (leading zeros policy, allowed chars)
- Outputs:
  - Tabs/grids for:
    - duplicates (profiles)
    - duplicates (assignments)
    - unassigned (profile-only)
    - invalid/missing profile extension (optional)
  - Export buttons (CSV/JSON) for each table
- Run metadata shown (timestamp, counts, duration).

---

## 9) Recommended defaults

- .NET 8 WPF + CommunityToolkit.Mvvm
- Generic Host + DI
- HttpClientFactory + Polly
- Sequential pagination; configurable page size (default 100 or API max)
- Cancellation always enabled; progress per page and per phase
- File logging enabled by default (rotating logs)

This stack cleanly supports the two target endpoints (`/api/v2/users` with optional `state=active` and `/api/v2/telephony/providers/edges/extensions`), robust pagination, and a responsive desktop UI with safe background execution, progress, and cancellation.
