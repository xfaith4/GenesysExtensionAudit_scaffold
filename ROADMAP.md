# GenesysCloudAuditor — Roadmap

This document tracks planned and potential future audit checks. Checks marked **Platform Bug Detection** are specifically intended to surface known or suspected Genesys Cloud platform synchronization issues.

---

## Implemented

| Check | Sheet | Severity | Description |
|---|---|---|---|
| Duplicate profile extensions | `Ext_Duplicates_Profile` | Critical | Multiple users share the same work-phone extension |
| Extension ownership mismatch | `Ext_Ownership_Mismatch` | Critical | User profile claims an extension that the telephony assignment system assigns to a different entity (platform bug) |
| Extension assignment vs profile | `Ext_Assign_vs_Profile` | Warning | Extensions present in assignments but missing from profiles, or on profiles but not in any assignment |
| Invalid extension values | `Invalid_Extensions` | Warning | Non-numeric or malformed extension values on profiles or assignments |
| Duplicate assigned extensions | _(included in `Ext_Duplicates_Profile`)_ | Critical | Same extension key appears on multiple telephony assignments |
| Empty/single-member groups | `Empty_Groups` | Warning | Groups with zero or one member |
| Empty/duplicate queues | `Empty_Queues` | Warning | Queues with zero members or duplicate names |
| Stale/unpublished flows | `Stale_Flows` | Warning | Architect flows not republished within a configurable threshold |
| Stale token users | `Stale_Tokens` | Warning | Users whose OAuth token has not been refreshed recently (potential inactive accounts) |
| Users missing location | `Users_No_Location` | Warning | User accounts with no location configured |
| DID mismatches | `DID_Mismatches` | Warning | DIDs unassigned, orphaned, or assigned to inactive users |
| Audit log events | `Audit_Logs` | Info | Genesys Cloud audit transaction log query results |
| Operational events | `Operational_Events` | Info | Operational event logs |
| Outbound events | `Outbound_Events` | Info | Outbound event logs |

---

## Planned / Under Consideration

### Platform Bug Detection

These checks target known or suspected Genesys Cloud platform synchronization issues.

| Planned Check | Priority | Description |
|---|---|---|
| **DID ownership mismatch** | High | A user's profile DID/phone number exists in the DID assignment list but is assigned to a different user or entity. Mirrors the extension ownership mismatch check. |
| **Station–user assignment conflict** | High | A station is assigned to a user in telephony, but the user's profile does not reflect that station (or the station is assigned to multiple users simultaneously). |
| **Trunk–edge assignment orphan** | Medium | An Edge device references a trunk that no longer exists or has been deprovisioned. |
| **Site–edge mismatch** | Medium | An Edge device's configured site does not match the site listed on the Edge resource itself — a known platform sync gap. |
| **IVR–flow binding stale** | Medium | An IVR number is bound to an Architect flow that has been deleted or is in an error state. |

### Configuration Audit

Checks for common misconfigurations that can cause silent failures or routing problems.

| Planned Check | Priority | Description |
|---|---|---|
| **Queue with no routing rules** | High | Queues that have members but no routing configuration — calls will queue indefinitely. |
| **Skill with no assignments** | Medium | Skills that exist in the org but are not assigned to any agent or queue — potential orphaned configuration. |
| **Wrap-up codes with no queue assignment** | Medium | Wrap-up codes not attached to any queue, which can result in agents being unable to complete interactions. |
| **Schedules referencing deleted flows** | Medium | Architect schedules that reference flows that no longer exist, causing routing failures. |
| **Emergency number not configured** | High | Locations or sites with no emergency (e.g., E911) number configured. |
| **Outbound campaign with inactive contact list** | Medium | Active outbound campaigns pointing to contact lists that have been deactivated or deleted. |
| **Recording policy gap** | Medium | Queues or flows with no recording policy configured, potentially violating compliance requirements. |

### Security & Access Audit

Checks for access control misconfigurations or potential privilege issues.

| Planned Check | Priority | Description |
|---|---|---|
| **Users with admin roles but inactive accounts** | High | Users assigned administrative roles whose accounts have not been active recently. |
| **OAuth clients with admin permissions** | High | OAuth client credentials configured with admin-level permissions beyond what is required. |
| **Users with no MFA (if detectable)** | Medium | Users without multi-factor authentication configured (subject to API availability). |
| **Roles assigned to deleted/inactive users** | Medium | Permission role assignments that reference users who are inactive or deleted. |

### Data Quality

Checks for data integrity issues that do not necessarily indicate a platform bug but may affect reporting accuracy.

| Planned Check | Priority | Description |
|---|---|---|
| **Users with no email address** | Low | User accounts missing an email address — may impact notifications and reporting. |
| **Duplicate queue names** | Medium | Queues with identical names (different IDs) — causes confusion in reporting and routing. |
| **Groups with duplicate names** | Low | Groups sharing the same name but different IDs. |
| **Extensions outside the org's configured range** | Medium | Profile or assignment extensions that fall outside the extension range configured for the org's telephony site. |
| **Users with extension format inconsistent with org standard** | Low | Extension values that don't follow the org's expected digit length (e.g., mixing 3-digit and 4-digit extensions). |

---

## Contributing

To suggest a new audit check, open an issue describing:
1. What data is compared or validated
2. Which Genesys Cloud API endpoints provide the data
3. Whether this is a platform bug, misconfiguration, or data quality check
4. The expected severity (Critical / Warning / Info)
