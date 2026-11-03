# Web administrative task inventory

Reviewed against source on 2026-11-03, starting at `dev` revision `d83d797`.
This replaces the October 7 route inventory. It records source-level call paths,
not proof of current deployment, complete auditing or successful delivery.
Database prerequisites and missing historical evidence are listed in
[README.md](README.md).

## Common capture contract

Intentional module opening, record/media viewing, applied search/filter/sort,
paging, refresh, drafts/cancellation and analytical controls are potential admin
tasks. Client-observed actions are distinct from database-confirmed changes and
provider outcomes. Automatic polling, Realtime updates and rerenders are not new
intentional tasks. Do not record keystrokes, report contents, passwords, recipient
messages or raw location data as generic audit metadata.

Use an operation ID per intentional mutation; retain it only for an identical
retry. A success event requires a confirmed outcome. Atomic audit requirements,
durable failures/denials and retry reconciliation must be verified in the owning
backend, not inferred from a frontend success message.

## Current tasks and owners

Paths are relative to `RescuAR.Admin/`. Complete cross-module capture is not
implemented or established by this inventory.

| ID | Task | Current path and capture boundary |
| --- | --- | --- |
| A01 | Sign in, restore/check session, sign out | `src/AuthWrapper.jsx`, `src/components/Auth.jsx`, `src/rbac/access.js`, `src/components/Header.jsx`: Supabase Auth, `rbac_my_access`, `rbac_password_setup_status`, local-scope logout. Durable access-decision capture is not established. |
| A02 | Complete first-login password setup | `src/components/FirstLoginPasswordChange.jsx`, `supabase/functions/rbac-admin-provision/index.ts`, migration 005: server-managed setup; temporary passwords must never enter generic audit metadata. |
| D01 | View/export dashboard and refresh | `src/components/Dashboard.jsx`: static datasets/activities, browser CSV export and refresh timer. Export generation does not prove a saved download. |
| M01 | List/refresh/sort stations and change summary | `src/components/MonitoringStations.jsx`: Supabase reads, Realtime and local controls. Age comes from `observed_at`; no general admin audit writer is integrated here. |
| M02 | View river history and select chart controls | `src/components/RiverLevel.jsx`: station/source reads and `pagasa_river_chart`. Worker/source health and measurement freshness are distinct. |
| P01 | Choose automatic/manual hazard exposure and request scenario | `src/components/HazardExposure.jsx`, `src/services/prepExposureApi.js`: PREP requests using telemetry context or manual inputs. Missing rules endpoint causes provisional fallback; this is reference exposure, not a live inundation forecast. |
| R01-R03 | Approve, reject or resolve report | `src/services/reportsModerationController.js`, `src/services/reportsModerationService.js`, migration 002: `admin_moderate_report`, validated receipt and refetch. SQL appends the corresponding `report.approved`/`report.rejected`/`report.resolved` event atomically when prerequisites exist. Live acceptance is unverified here. |
| R04 | Open moderation, select report/media, apply search or refresh | Same controller/service and `src/services/webAuditService.js`: acknowledged `record_web_audit_event` calls; automatic updates/default selection excluded. |
| U01 | List/refresh users and select profile | `src/components/UserManagement.jsx`: `get_all_users_with_roles`, Realtime and local selection. The RPC's full baseline is absent; read/audit coverage cannot be inferred. |
| U02-U04 | Edit, suspend/reactivate or delete user | Same component: `admin_update_user`, `admin_toggle_suspend_user`, `admin_delete_user`. Deployed authorization, suspension enforcement and atomic auditing need backend evidence. |
| U05 | Notify from user detail form | Same component: browser alert only; no dispatch. |
| V01-V07 | Create/edit/archive/reactivate/duplicate/delete advisory; copy text | `src/components/Advisories.jsx`, `src/components/Modals.jsx`: direct Supabase writes and local clipboard action; generic atomic audit capture is not established. |
| E01-E04 | Create/edit centers, upload/attach images, synchronize cache | `src/components/EvacuationCenters.jsx`, `src/utils/cloudinary.js`: local/cache state, Supabase writes and Cloudinary upload. Upload alone does not prove attachment to a persisted center. |
| H01 | Add hotline | `src/components/EmergencyHotlines.jsx`: direct `emergency_hotlines` insert; additional capture is not established. |
| S01 | Save system settings | `src/components/SystemSettings.jsx`: local state/success timer; no persisted configuration. |
| N01 | Run Notify Residents broadcast | `src/components/Modals.jsx`: local progress simulation; SMS/push/siren choices do not dispatch. |
| L01 | Open System Logs or Documentation | `src/components/SystemLogs.jsx` contains static sample logs; `src/components/Documentation.jsx` describes operation. Neither is an audit-history viewer. |
| L02 | Open Residents Directory | `src/App.jsx`: placeholder; distinct from active User Management. |
| B01 | Ingest telemetry | `scraper/syncWorker.js`: initial sync and five-minute schedule, station/history/source-status writes; no admin initiator. |
| F01 | Read/filter audit history | `src/services/webAuditService.js` retains a `get_web_audit_logs` client, but no active App route renders its viewer. System Logs is not this integration. |
| F02 | List/queue outbound SMS; handset claim/report | `src/App.jsx` routes SMS Parser to `src/components/SmsOutboundConsole.jsx`; `supabase/functions/sms-gateway/index.ts` and `004_sms_outbound.sql` own the queue. Queuing/carrier submission is not recipient delivery; broader audit capture is unverified. |
| F03 | Assign/revoke roles, create custom roles | `src/components/RbacAdmins.jsx`: `rbac_set_assignment`, `rbac_create_custom_role`; `004_rbac_foundation.sql` records RBAC changes in `rbac_audit_log`, separate from legacy `audit_private` storage. |
| F04 | Provision verified administrator | Same component, `rbac-admin-provision` function and migration 005: new account/role and mandatory password setup. Superadmin identity approval and `email_confirm: true` do not prove mailbox ownership. |
| F05 | Record testing inputs | `src/components/RbacTesting.jsx`: `rbac_log_test`/`rbac_list_tests`, stored test-only inputs; no real river/advisory change. |

## Access and acceptance boundaries

The UI permission map is `src/rbac/access.js`. SMS Parser has no dedicated entry
there and currently uses the `system.all` fallback. Its backend separately checks
legacy `admin_roles`; newer RBAC assignments are not equivalent evidence of that
membership. Reports/user RPCs require their own deployed review.

Before claiming complete audit coverage, establish current deployed bodies,
each task's capture owner, explicit requested/succeeded/failed/denied/cancelled/
unknown/simulated outcomes, atomic mutation/history behavior, and retry/privacy
rules. A static log screen, database fixture or old catalogue report cannot prove
authenticated acceptance of those contracts.
