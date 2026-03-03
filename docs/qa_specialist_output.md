# End-to-end QA pass plan: large tenant pagination, rate-limit behavior, cancellation, export correctness, UI responsiveness

## 1) Scope & target behaviors

### Endpoints under test

- Users:
  - When `IncludeInactive=false`: `GET /api/v2/users?pageSize={PageSize}&pageNumber={n}&state=active`
  - When `IncludeInactive=true`: `GET /api/v2/users?pageSize={PageSize}&pageNumber={n}`
- Extensions:
  - `GET /api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={n}`

### Core quality goals

1. Large tenants (high `pageCount`, large totals) complete successfully with bounded parallelism and stable memory usage.
2. Correct rate-limit handling: 429 + `Retry-After` respected; retries bounded; eventual success or actionable error.
3. Cancellation works quickly and safely at any point (including while waiting to retry).
4. Export produces correct, Excel-friendly CSVs (BOM, quoting, row counts, deterministic headers).
5. UI remains responsive (no blocking), progress updates, Start/Cancel enablement correct.

---

## 2) End-to-end test matrix (what to run)

### A. Large tenant pagination (functional + performance-ish)

**A1. Users pagination includes correct `state`**

- Setup: mock server returns `pageCount=50`, `pageSize=100` (or smaller for test speed).
- Run twice:
  - `IncludeInactive=false` => verify every request contains `&state=active`.
  - `IncludeInactive=true` => verify no request includes `state=active`.
- Pass:
  - Total users returned == sum across all pages.
  - No missing/duplicated pages.
  - Correct query string per mode.

**A2. Extensions pagination across many pages**

- Setup: `pageCount=200` with small entities (or minimal JSON) to simulate large tenant.
- Pass:
  - All pages fetched exactly once each (unless caching enabled and intentionally reused).
  - Result count matches expected.
  - No deadlocks / thread starvation.

**A3. PageSize boundary behavior**

- In UI: set page size to `0`, `-1`, `9999`, `1`, `500`.
- Expected per ViewModel clamp:
  - Values clamp to [1..500].
  - Start refuses only if <=0 at runtime (should not happen if clamped).
- Pass:
  - Clamping is visible and audit uses clamped value in requests.

**A4. Bounded parallelism actually bounded**

- If `PagingOrchestratorOptions.MaxParallelRequests = k`:
  - Mock handler delays each response; measure max concurrent in-flight requests.
- Pass:
  - Peak concurrency never exceeds `k`.
  - Completion time roughly aligns with bounded concurrency (sanity check).

---

### B. Rate-limit behavior (429 Retry-After + transient failures)

**B1. 429 with Retry-After respected**

- Mock page N returns:
  - First call: `429` with `Retry-After: 2`
  - Second call: `200` success
- Pass:
  - Orchestrator waits ~2 seconds (allow small tolerance) before retrying.
  - Eventually succeeds without surfacing error.
  - Retry attempt count increments but stays within `MaxRetries`.

**B2. 429 without Retry-After uses backoff**

- Mock `429` without header for first 2 attempts then success.
- Pass:
  - Backoff delay increases (exponential+jitter as implemented).
  - Succeeds within `MaxRetries`.

**B3. Sustained 429 exceeds MaxRetries**

- Mock always `429`.
- Pass:
  - Audit fails with clear error (not infinite loop).
  - UI shows `ErrorMessage` and `StatusMessage="Audit failed."`
  - `IsRunning` resets false; Start enabled.

**B4. Mixed transient errors**

- Inject occasional `502/503` if your HTTP layer maps to transient exceptions.
- Pass:
  - Retries occur (if configured).
  - Final result correct or error surfaced.

---

### C. Cancellation (fast, safe, no partial export unless intended)

**C1. Cancel during page 1**

- Delay page 1 response (simulate slow network).
- Start audit, then Cancel quickly.
- Pass:
  - `OperationCanceledException` handled -> Status “Audit cancelled.”
  - No crash, commands toggle correctly.
  - No partial files written (unless product explicitly allows partial export; current design suggests export is after run).

**C2. Cancel during parallel page fetch**

- Delay multiple pages; cancel mid-run.
- Pass:
  - All outstanding tasks observe cancellation.
  - No deadlocks; run ends quickly.
  - `_cts` disposed and set null (no leaks).

**C3. Cancel while rate-limit waiting**

- While waiting due to Retry-After delay, cancel.
- Pass:
  - Cancel interrupts waiting (i.e., `Task.Delay(..., ct)` in retry logic).
  - Run ends as canceled, not failed.

**C4. Cancel spam / double cancel**

- Click Cancel multiple times rapidly.
- Pass:
  - No exceptions; status remains “Cancelling…” then “Audit cancelled.”

---

### D. Export correctness (CSV)

**D1. Files created (one per report + Summary)**

- After a run with known fixture data generating at least one item in each section (or some empty sections).
- Pass:
  - Summary + each expected CSV exists:
    - Summary.csv
    - DuplicateProfileExtensions.csv
    - ProfileExtensionsNotAssigned.csv
    - DuplicateAssignedExtensions.csv
    - AssignedExtensionsMissingFromProfiles.csv
    - InvalidProfileExtensions.csv
    - InvalidAssignedExtensions.csv
  - Paths returned in `ExportResult.FilesByReport`.

**D2. CSV headers correct and stable**

- Verify first line of each file equals expected header (order and naming).

**D3. Row counts correct (flattening logic)**

- For hierarchical findings (extension -> users/assignments):
  - Expected rows = sum of `Users.Count` (or `Assignments.Count`) across findings.
- Pass:
  - Export rows match expected and include `ExtensionKey` repeated per detail row.

**D4. Excel-friendly encoding**

- With `IncludeUtf8Bom=true`, verify BOM present.
- Pass:
  - File begins with UTF-8 BOM bytes `EF BB BF`.

**D5. Proper CSV quoting**

- Use fixture data containing:
  - commas in user names: `"Doe, Jane"`
  - quotes: `Bob "The Builder"`
  - newlines: `Line1\nLine2`
- Pass:
  - CSV escapes per RFC-style rules (quote wrapping and `""` doubling).
  - Excel opens without column shifting.

> Note: If current `WriteCsv` implementation does not quote/escape correctly, this test will expose it. This is a common defect area.

**D6. Overwrite behavior**

- With `Overwrite=false` and existing files present:
  - Pass: Export throws `IOException` and UI surfaces error cleanly.
- With `Overwrite=true`:
  - Pass: files replaced.

---

### E. UI responsiveness & correctness (WPF MVVM)

**E1. Start/Cancel command enablement**

- Initial: Start enabled, Cancel disabled.
- On start: Start disabled, Cancel enabled.
- After completion/failure/cancel: Start enabled, Cancel disabled.
- Pass: consistent with `IsRunning` and `RaiseCanExecuteChanged()`.

**E2. Progress updates don’t freeze UI**

- While fetching many pages with artificial delays:
  - Move/resize window, interact with controls.
- Pass:
  - UI thread remains responsive.
  - ProgressPercent/Message update periodically.
  - No “Not Responding”.

**E3. Results rendering performance sanity**

- Large results (thousands of detail rows):
  - Expand/collapse some findings.
  - Scroll within DataGrids.
- Pass:
  - No extreme lag/hangs.

> Risk note: current XAML uses `ItemsControl` + many `DataGrid`s inside expanders; this can get heavy. If issues observed, consider virtualization strategies.

**E4. Error surfacing**

- Force API failure (e.g., 401/403/500).
- Pass:
  - `ErrorMessage` populated with actionable text.
  - `StatusMessage="Audit failed."`
  - App remains usable afterward.

---

## 3) Automation recommendations (what to add/extend)

### Infrastructure integration tests (recommended)

Extend `ApiClientIntegrationTests` / add `PagingOrchestrator` tests with a mock handler that can:

- Simulate `pageCount` up to 200+
- Introduce per-route delays
- Emit 429 with/without Retry-After
- Track max concurrent requests

Key assertions:

- Call counts per page
- Peak concurrency <= configured
- Total duration roughly matches expected with Retry-After
- Cancellation causes fewer total calls and terminates quickly

### Application layer tests (recommended)

- A test `IAuditRunner.RunAsync` that uses orchestrator and verifies `CancellationToken` propagation.
- Ensure cancellation is surfaced as canceled (not failed).

### UI tests (optional)

- Manual smoke checklist (above) is probably sufficient initially.
- If automating: WinAppDriver / FlaUI to verify Start/Cancel toggles and the window stays responsive during simulated runs.

---

## 4) Acceptance criteria summary (exit conditions)

- Pagination correctness: no missing pages; correct `state` query behavior.
- Rate-limits: retries obey Retry-After; bounded retries; no infinite loops.
- Cancellation: cancels within a short bound (target: <1s after Cancel for mock delays, excluding any unavoidable synchronous segments).
- Export: all files generated; correct headers/rows; BOM present; quoting correct.
- UI: no blocking; commands + status/progress consistent; results view usable for large outputs.
