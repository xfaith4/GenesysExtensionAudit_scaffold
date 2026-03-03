# Data model + auditing algorithms for Genesys Cloud extension audit

### 1) Source endpoints and raw staging

You will ingest two paginated feeds:

1) **Users**

- `GET /api/v2/users?pageSize={N}&pageNumber={p}[&state=active]`
- Raw fields needed (minimum):
  - `id` (userId, GUID)
  - `name`
  - `state` (active/inactive/etc)
  - `primaryContactInfo` (or wherever “work phone extension” is exposed in your tenant; store the raw string exactly as returned)

1) **Edge extensions**

- `GET /api/v2/telephony/providers/edges/extensions?pageSize={N}&pageNumber={p}`
- Raw fields needed (minimum; actual response may vary by tenant):
  - `id` (extensionAssignmentId or extensionRecordId)
  - `extension` (the extension string/number)
  - “assigned-to” identity if present (often one of: `user.id`, `station.id`, `phoneBaseSettings.id`, `edgeGroup.id`, etc.)
  - any “type” discriminator (user/station/unknown) if present

**Staging tables (raw)**

- `UserRaw(userId PK, name, state, workPhoneExtRaw, retrievedAt)`
- `ExtAssignRaw(assignId PK, extensionRaw, targetType, targetId, payloadJson, retrievedAt)`

Keep the raw values for traceability in the UI/export.

---

### 2) Normalized core entities (canonical join keys)

To compare and dedupe correctly, introduce a canonical extension key.

#### 2.1 Extension normalization

Define a function:

`NormalizeExtension(raw: string) -> (extKey: string|null, status: enum, notes: string)`

Recommended pipeline (deterministic):

1. If null/empty/whitespace → `status=Empty`, `extKey=null`
2. `s = Trim(raw)`
3. Uppercase `s` (only matters if alphanumeric extensions are allowed)
4. Remove common separators: spaces, `-`, `.`, `(`, `)`, tabs
   (Do **not** remove `+` unless you explicitly decide to treat E.164 as invalid; see below.)
5. Strip common “ext” markers:
   - patterns like `^X`, `^EXT`, `^EXT\.`, `^EXTENSION` when followed by digits/letters
6. Validate:
   - If result is empty → `Empty`
   - If contains characters outside `[A-Z0-9]` → `InvalidFormat`
   - Optional length bounds (configure): e.g., min 2 max 10 → `InvalidLength`
7. Leading zeros policy: **configurable**
   - Default safest: **preserve** leading zeros (strict PBX semantics)
   - Optional: numeric-only and `TrimStart('0')` (lenient), but must be a stakeholder decision

Return:

- `extKey`: canonical string used for joins (e.g., `"1234"`, `"A12B"`)
- `status`: `Ok | Empty | InvalidFormat | InvalidLength | Ambiguous`
- `notes`: why (for report)

**Do not auto-extract last N digits from full phone numbers** unless explicitly required; treat as `InvalidFormat` to avoid false matches.

#### 2.2 Core normalized tables / in-memory structures

- `User(userId PK, name, state)`
- `UserExtension(userId FK->User, extKey, extRaw, normStatus, normNotes)`
  - Unique constraint suggestion: `(userId)` if you only store the single “work phone extension” field; otherwise `(userId, fieldName)`
- `ExtensionAssignment(assignId PK, extKey, extensionRaw, targetType, targetId)`
- `ExtensionIndex(extKey PK)` (optional convenience dimension)

**Join keys**

- Primary join across datasets: `extKey`
- Identity join (when assignment targets are users): `ExtensionAssignment.targetType == 'user' && targetId == userId`

This enables:

- set comparisons (profile vs assignment)
- collision detection (same extKey on multiple users or multiple assignments)
- mismatch detection (user profile extKey differs from assignment record that targets that user)

---

### 3) Derived indexes for efficient audit

Build these maps after normalization:

- `profileByExt: Dictionary<extKey, List<UserExtension+User>>`
  - include only `normStatus==Ok` and (optionally) only active users depending on toggle

- `assignByExt: Dictionary<extKey, List<ExtensionAssignment>>`
  - include only `extKey != null` and `normStatus==Ok` (apply normalization to assignment extensionRaw similarly)

- `assignByTargetUser: Dictionary<userId, List<ExtensionAssignment>>`
  - only where `targetType == 'user'`

---

### 4) Auditing algorithms

#### 4.1 Duplicate extensions on user profiles

**Definition:** same `extKey` appears on 2+ users’ profile work phone extension fields.

Algorithm:

1. For each `extKey` in `profileByExt`:
2. If `CountDistinct(userId) >= 2`:
   - emit a `DuplicateProfileExtension` record:
     - `extKey`
     - `userCount`
     - users: `(userId, name, state, extRaw)`

Output schema:

- `DuplicateProfileExtension(extKey, userCount, users[])`

Notes:

- If `$IncludeInactive=false`, exclude inactive users from `profileByExt` entirely so they don’t generate duplicates.
- Optionally also produce “duplicate among active only” vs “duplicate including inactive” as separate runs or flags.

#### 4.2 Duplicate extensions in assignment list

**Definition:** same `extKey` appears on 2+ assignment records from `/extensions`.

Algorithm:

1. For each `extKey` in `assignByExt`:
2. If `Count(assignments) >= 2`:
   - emit `DuplicateAssignedExtension`:
     - `extKey`
     - count
     - list: `(assignId, targetType, targetId, extensionRaw)`

Output schema:

- `DuplicateAssignedExtension(extKey, assignmentCount, assignments[])`

Notes:

- Some tenants may legitimately show multiple objects with same extension depending on object model; still useful to flag. If stakeholders later decide “duplicates across types are allowed”, add grouping by `(extKey, targetType)` and only flag duplicates within a type.

#### 4.3 Profile-only “unassigned” extensions (U \ A)

**Definition:** extension appears on user profile(s) (valid normalized) but does not appear anywhere in the assignment list.

Algorithm:

1. For each `extKey` in `profileByExt.Keys`:
2. If `extKey` not in `assignByExt`:
   - emit `UnassignedProfileExtension`:
     - `extKey`
     - users: list of profile holders

Output schema:

- `UnassignedProfileExtension(extKey, users[])`

Important:

- Do **not** include `normStatus!=Ok` here; those belong in an “Invalid profile extension format” report.

#### 4.4 Assigned-but-missing-on-profiles (optional but often requested)

**Definition:** `extKey` exists in assignments but no user profile contains it.

Algorithm:

- For each `extKey` in `assignByExt.Keys`:
  - if `extKey` not in `profileByExt`:
    - emit `AssignedExtensionMissingFromProfiles(extKey, assignments[])`

This helps find unused/legacy assignments.

#### 4.5 User mismatch: profile extension vs extension assigned to that user (when assignment targets users)

Only possible if assignment payload provides user target identity.

Definitions:

- For a given user:
  - `profileExtKey = UserExtension.extKey` (if Ok)
  - `assignedExtKeys = assignByTargetUser[userId].Select(extKey).Distinct()`

Flag conditions:

1. **Profile set but user has no assignment**:
   - `profileExtKey != null && assignedExtKeys empty`
2. **User has assignment(s) but profile empty**:
   - `profileExtKey null/Empty && assignedExtKeys not empty`
3. **Profile and assignment disagree**:
   - `profileExtKey != null && assignedExtKeys not empty && profileExtKey not in assignedExtKeys`

Output schema:

- `UserExtensionMismatch(userId, name, state, profileExtRaw, profileExtKey, assignedExtKeys[], assignments[])`

This is distinct from “unassigned” because the extension might exist in assignments, just not tied to that user.

---

### 5) Invalid/empty/ambiguous extension reporting

Produce a separate quality report from `UserExtension` rows:

- `InvalidUserProfileExtension(userId, name, state, extRaw, status, notes)`

Similarly for assignment rows if normalization fails:

- `InvalidAssignedExtension(assignId, extensionRaw, status, notes)`

This keeps “unassigned” strictly meaningful.

---

### 6) Normalization + identity considerations (key decisions captured as config)

Recommended app configuration fields:

- `IncludeInactiveUsers` (controls user query `state=active` omission and inclusion in all computations)
- `ExtensionNormalization`
  - `PreserveLeadingZeros: bool` (default true)
  - `AllowAlphanumeric: bool` (default true/false depending on tenant rules)
  - `MinLen`, `MaxLen`
  - `StripSeparators: set<char>`
  - `StripExtPrefixes: bool`
- `DuplicateAssignmentPolicy`
  - `CrossTypeDuplicatesAllowed: bool` (if true, only flag duplicates within same `targetType`)

Store these settings with the audit run metadata.

---

### 7) Recommended normalized storage (if using a local DB)

If you persist results (SQLite suggested for desktop apps), a normalized schema:

- `audit_run(run_id PK, started_at, include_inactive, page_size, normalize_config_json, notes)`
- `user(run_id FK, user_id, name, state, PRIMARY KEY(run_id, user_id))`
- `user_extension(run_id FK, user_id, ext_raw, ext_key, norm_status, norm_notes, PRIMARY KEY(run_id, user_id))`
- `ext_assignment(run_id FK, assign_id, extension_raw, ext_key, target_type, target_id, PRIMARY KEY(run_id, assign_id))`

Indexes:

- `IDX_user_extension_ext_key(run_id, ext_key)`
- `IDX_ext_assignment_ext_key(run_id, ext_key)`
- `IDX_ext_assignment_target_user(run_id, target_type, target_id)`

This supports fast grouping and UI filtering.

---

### 8) Summary of joins and set logic

- **Main join key:** `extKey = Normalize(extensionRaw/profileRaw)`
- **Duplicates (profiles):** group `user_extension` by `extKey`, count users > 1
- **Duplicates (assignments):** group `ext_assignment` by `extKey`, count rows > 1 (or >1 per targetType)
- **Unassigned (profile-only):** `ProfileExtKeys - AssignedExtKeys`
- **Mismatch (user-targeted):** compare `profileExtKey` to `assignedExtKeys` for same `userId`

This design keeps raw payloads auditable, comparisons deterministic via normalization, and outputs report-friendly for a Windows desktop application.
