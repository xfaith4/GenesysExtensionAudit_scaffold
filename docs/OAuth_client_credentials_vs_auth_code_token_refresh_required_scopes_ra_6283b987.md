## Genesys Cloud API integration approach (desktop audit app)

### 1) OAuth grant choice

#### Recommended: **OAuth Client Credentials** (non-interactive “service account”)

Use **client credentials** when the app is an audit tool that runs unattended or should not depend on an end-user login session.

- **Pros**
  - No user sign-in UI required.
  - Simple token handling (no refresh token).
  - Works well for scheduled/automated audits and least operational friction.
- **Cons**
  - Access is limited to what the OAuth client is allowed to do (scopes/roles).
  - Requires creating an OAuth client in Genesys Cloud and governing its credentials.

**How**

- Create an OAuth client of type “Client Credentials” in Genesys Cloud.
- Store `client_id` and `client_secret` securely (Windows Credential Manager/DPAPI; never plain text).
- Token endpoint (region-specific):
  `POST https://login.<region>.pure.cloud/oauth/token` with `grant_type=client_credentials`.

#### Alternative: **Authorization Code (PKCE)** (interactive user-run)

Use **auth code with PKCE** if the audit must run “as the signed-in user” and respect that user’s permissions, or you cannot provision a service client.

- **Pros**
  - Least privileged per user; no shared secret (PKCE).
  - Aligns with “run as me” security/audit trails.
- **Cons**
  - Requires browser-based login and redirect handling.
  - Requires refresh token storage/rotation and more complex lifecycle.

**How**

- Use system browser + loopback redirect URI (recommended for desktop).
- Use PKCE (avoid client secret in desktop apps).

---

### 2) Token lifecycle & refresh strategy

#### Client Credentials token handling

Genesys Cloud issues an **access token** with an expiry (commonly ~24h but do not assume; read `expires_in`).

- Cache the token in memory and reuse for all calls until near expiry.
- Proactively renew when within a small skew window (e.g., renew if `now > (acquired_at + expires_in - 60s)`).
- If an API call returns **401/403**:
  - **401 Unauthorized**: reacquire a token once and retry the request once.
  - **403 Forbidden**: treat as a permission/scopes/roles issue; do not loop retry.

No refresh token is used/available for client credentials; you just request a new access token.

#### Auth code (PKCE) token handling (if used)

- Store refresh token securely (DPAPI-protected at minimum).
- Refresh when near expiry; if refresh fails, require re-login.
- On **401**, attempt refresh once, then re-auth.

---

### 3) Required scopes (minimum set)

Genesys Cloud uses OAuth scopes that map to platform permissions; exact needs can vary by org configuration. For the two target endpoints:

1) `GET /api/v2/users?...`

- **Likely required scope**: `users:read`
- Note: If you need additional user profile fields and your org restricts PII, you may need admin role grants in addition to the scope.

2) `GET /api/v2/telephony/providers/edges/extensions?...`

- **Likely required scope**: `telephony:read` (and/or a more specific telephony edges read scope depending on tenant)
- In some orgs, telephony/edge resources require elevated roles; plan for an operator/admin service account if using client credentials.

**Implementation recommendation**

- Start with: `users:read` + `telephony:read`.
- Validate by calling each endpoint in a test tenant; if 403 occurs, inspect Genesys Cloud error details and adjust scopes/roles accordingly.
- Document the exact OAuth scopes and required roles in deployment notes for the customer tenant.

---

### 4) Rate limiting strategy

Genesys Cloud enforces rate limits; when exceeded you typically receive **HTTP 429**.

**Policy**

- On **429 Too Many Requests**:
  - If `Retry-After` header is present, wait that duration (seconds) + small jitter, then retry.
  - If absent, apply exponential backoff starting at e.g. 1s.
- Avoid high concurrency for pagination. For this audit, **sequential paging** per endpoint is usually sufficient and safer. If parallelizing, cap to a small degree (e.g., 2–4 concurrent requests total) and implement a shared rate limiter.

**Client-side throttling (recommended)**

- Implement a simple token-bucket/leaky-bucket limiter per process (e.g., max N requests/sec), with N configurable.
- Keep one limiter shared across both endpoints to prevent aggregate bursts.

---

### 5) Retry / backoff strategy (transient fault handling)

These are **GET** requests and are safe to retry (idempotent) as long as you accept that underlying data may change mid-run.

#### Retryable conditions

- **429**: retry (respect `Retry-After`).
- **408 Request Timeout**: retry.
- **5xx** (500, 502, 503, 504): retry.
- Network exceptions (DNS, connection reset, TLS handshake issues): retry.

#### Non-retryable conditions

- **400/404**: treat as coding/config issue (bad URL/params).
- **401**: refresh/reacquire token once then retry once; if still 401, fail.
- **403**: fail with “insufficient permissions/scopes”.
- **409**: not expected for these GETs; treat as non-retry unless Genesys docs indicate otherwise.

#### Backoff algorithm

- Exponential backoff with jitter, e.g.:
  - baseDelay = 1s, maxDelay = 30s, maxAttempts = 6 (tunable)
  - delay = min(maxDelay, baseDelay * 2^(attempt-1)) + random(0..250ms)
- For 429 with Retry-After: delay = Retry-After + random(0..250ms)

#### Pagination retry nuance

- If a page request fails after partial progress:
  - Retry the **same pageNumber** (do not increment).
  - After max attempts, fail the run (or mark endpoint incomplete and clearly label audit output as partial/unreliable).

---

### 6) Practical integration notes for the Windows desktop audit tool

- **Region base URLs**
  - Auth: `https://login.<region>.pure.cloud`
  - API: `https://api.<region>.pure.cloud`
  - Region must match the org (e.g., `mypurecloud.com`, `euw2.pure.cloud`, etc.).

- **HTTP headers**
  - `Authorization: Bearer <token>`
  - `Accept: application/json`
  - Optional: `User-Agent` with app name/version for supportability.

- **Observability**
  - Log request start/end, status code, retry count, elapsed time.
  - Capture Genesys correlation/request ids if returned in headers for troubleshooting.
  - Include run timestamp, pageSize, IncludeInactive setting, and counts in report metadata.

- **Concurrency recommendation**
  - Keep it simple: fetch all pages from `/users`, then all pages from `/extensions`.
  - If performance becomes an issue, consider limited parallelization but keep a global rate limiter and preserve retry semantics.

This approach gives a secure, low-friction integration (client credentials), robust handling of token expiry, and predictable behavior under Genesys Cloud rate limits while paging through `/api/v2/users` and `/api/v2/telephony/providers/edges/extensions`.
