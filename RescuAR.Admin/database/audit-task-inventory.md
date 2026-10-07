# Web administrative task inventory

Inspected: 2026-10-07. Scope: RescuAR.Admin and its supporting web operations.
Status: capture catalogue; application acceptance is pending. The deployed
foundation installs capture in the four user RPCs; its catalog verification is
recorded in [foundation-deployment-review.md](foundation-deployment-review.md).
The original schema snapshot is reviewed in
[deployed-schema-review.md](deployed-schema-review.md).
The [step 3 application integration](application-integration.md) installs the
administrator session gate and real audit viewer. Initial live acceptance of
those features was reported completed and validated by the user on 2026-10-07.

## Common task coverage

These tasks apply to each available module. A task has one operation ID, with
related request/outcome events sharing that ID. Every intentional repetition is
a new task. A retried request keeps its ID and must not repeat a completed change.

| ID | Task | Proposed event | Existing path / evidence required |
| --- | --- | --- | --- |
| C01 | Open or switch a module | `module.opened` | App/Sidebar navigation; client interaction acknowledged by an authenticated backend, not proof a user read its contents |
| C02 | Request/list records | `records.listed` | Initial load or explicit request; identify module, query scope and returned count |
| C03 | Open a record or report media | `record.viewed`, `report.media_opened` | Current selections often use already-loaded browser data; distinguish client-observed viewing from server-provided data access |
| C04 | Search, filter, sort, or paginate | `records.queried` | Current operations are mainly local; capture applied query state after debounce, without storing sensitive search text |
| C05 | Manually refresh a module | `module.refreshed` | Link the click to its fetch and confirmed outcome; a visual-only refresh is identified as local |
| C06 | Begin a draft or apply a template | `draft.opened`, `draft.template_applied` | Current browser state; record draft ID/template ID, not every input keystroke or draft contents |
| C07 | Cancel a draft, confirmation, or running task | `task.cancelled` | Record cancellation against an operation/draft ID; distinguish abandoning a form from cancelling submitted backend work |
| C08 | Change analytical/display controls | `analysis.controls_changed` | Station/date range, simulation mode/input, map style, station summary; group a completed interaction and allowlist its parameters |

Client-observed events must be labelled as such. Only verified backend/provider
outcomes may assert a persisted change, access decision, calculation, or delivery.
If a browser never reaches the server, the server cannot independently prove that
interaction. Later integration must define retry/reconciliation behavior rather
than claim complete capture during a network outage.

Automatic polling, Realtime updates, component rerenders, default first-record
selection, and the audit writer are not additional intentional admin tasks.
Classify automatic work by source; link work caused by an admin task to its parent
operation. Viewing an audit event must not recursively generate more view events.

## Module-specific tasks

The source column names repository-relative paths under `src/`, unless specified.
The common C01-C08 rows apply where the corresponding controls are available.

| ID | Module / task | Proposed event | Current execution path | Source |
| --- | --- | --- | --- | --- |
| A01 | Attempt administrator login | `auth.login` | Supabase Auth; server/provider outcome needed, unknown actor on failed authentication | `components/Auth.jsx:16` |
| A02 | Admit or deny administrator access | `admin.access_checked` | Wrapper verifies membership through `web_admin_access`; user/audit RPCs enforce it. Durable access-decision events remain pending | `AuthWrapper.jsx`; `services/adminSessionGate.js` |
| A03 | Restore/revalidate a session | `admin.session_checked` | Shared gate verifies restored sessions, Auth events and window focus; initial gate acceptance reported completed, durable decision capture remains pending | `AuthWrapper.jsx`; `services/adminSessionGate.js` |
| A04 | Sign out | `auth.logout` | Supabase Auth; distinguish explicit user logout from rejection during login | `components/Header.jsx:6` |
| D01 | Export dashboard analytics | `dashboard.export_requested`, `dashboard.export_generated` | Browser CSV from static data; generation is not proof of a saved download | `components/Dashboard.jsx:539` |
| D02 | Refresh dashboard | `module.refreshed` | Timer updates displayed time; no data fetch | `components/Dashboard.jsx:530` |
| M01 | List/refresh monitoring stations; sort or change summary | C02, C04, C05, C08 | Supabase read plus local display controls | `components/MonitoringStations.jsx:42` |
| M02 | Select river station/date range; refresh readings | C02, C05, C08 | Reads `monitoring_stations` and `river_level_history` | `components/RiverLevel.jsx:98` |
| P01 | Select live/simulation input and request a prediction | `prediction.requested`, `prediction.completed` | Browser calls configured external endpoint; link input change to request/outcome | `components/InundationPrediction.jsx:157`; `services/prepInundationApi.js:4` |
| P02 | Run the prediction modal | `prediction.simulation_run` | Local timer and hardcoded output, explicitly simulation | `components/Modals.jsx:433` |
| R01 | Approve report | `report.approved` | Direct `community_reports.status` update | `components/ReportsModeration.jsx:130` |
| R02 | Reject report | `report.rejected` | Same update path; decision reason is not currently collected | `components/ReportsModeration.jsx:130` |
| R03 | Resolve report | `report.resolved` | Same update path | `components/ReportsModeration.jsx:130` |
| U01 | List/refresh users and review a profile | `users.listed`, C03, C05 | Guarded directory RPC records read/count; local profile selection and intentional refresh still need client capture | `components/UserManagement.jsx:48` |
| U02 | Edit a user | `user.updated` | Guarded RPC records changed field names and outcome atomically; omitted fields are preserved | `components/UserManagement.jsx:153` |
| U03 | Suspend/reactivate a user | `user.status_changed` | Guarded RPC audits the profile flag; directory still omits status and enforcement is not established | `components/UserManagement.jsx:119` |
| U04 | Delete a user | `user.deleted` | Guarded RPC audits successful account deletion atomically; existing membership FK restrictions remain | `components/UserManagement.jsx:137` |
| U05 | Notify a user | `notification.simulation_run` | Browser alert only; cannot assert actual dispatch | `components/UserManagement.jsx:170` |
| V01 | Create an advisory | `advisory.created` | Direct insert into `advisories` | `components/Advisories.jsx:319` |
| V02 | Publish from the advisory modal | `advisory.created` | Separate direct insert; needs the same capture contract as V01 | `components/Modals.jsx:156` |
| V03 | Edit an advisory | `advisory.updated` | Direct update; capture database before/after | `components/Advisories.jsx:354` |
| V04 | Archive/reactivate an advisory | `advisory.archived`, `advisory.reactivated` | Direct status update | `components/Advisories.jsx:268` |
| V05 | Duplicate an advisory | `advisory.duplicated` | Direct insert; record originating advisory ID | `components/Advisories.jsx:282` |
| V06 | Delete an advisory | `advisory.deleted` | Direct delete after confirmation | `components/Advisories.jsx:257` |
| V07 | Copy generated advisory text | `advisory.copy_requested` | Clipboard request; current code does not await its result; never log the copied text | `components/Modals.jsx:150` |
| E01 | Create/edit an evacuation center | `evacuation_center.created`, `evacuation_center.updated` | Local state/cache precedes Supabase write; confirmed outcome must be corrected | `components/EvacuationCenters.jsx:1062` |
| E02 | Upload an evacuation image | `asset.upload_requested`, `asset.upload_completed` | Cloudinary upload; form upload alone does not attach/publish the image | `components/EvacuationCenters.jsx:756`; `utils/cloudinary.js:12` |
| E03 | Attach/replace a center image | `evacuation_center.image_changed` | Direct-card upload can issue multiple writes; group under one operation ID | `components/EvacuationCenters.jsx:780` |
| E04 | Synchronize cached center data | `evacuation_center.cache_sync` | Implicit writes during fetching; original author of cached data is unknown | `components/EvacuationCenters.jsx:898` |
| H01 | Add an emergency hotline | `hotline.created` | Direct `emergency_hotlines` insert | `components/EmergencyHotlines.jsx:91` |
| S01 | Submit system configuration | `settings.save_requested` | Local state and success timer; no persisted update or effective configuration evidence | `components/SystemSettings.jsx:9` |
| N01 | Run broadcast modal | `notification.simulation_run` | Local progress timer; no provider dispatch | `components/Modals.jsx:305` |
| L01 | Open System Logs or Documentation | `module.opened` | Static screens; report the module interaction accurately | `App.jsx:82` |
| L02 | Open residents/SMS placeholder | `module.opened` | Active routes display placeholders; no directory access or SMS processing occurs | `App.jsx:67` |
| B01 | Scheduled telemetry ingestion | `telemetry.sync_completed`, `telemetry.sync_failed` | Service source; history inserts and station upserts, no admin initiator in current code | `scraper/syncWorker.js:332` (outside `src/`) |

## Reserved integration points

The [step 2 database foundation](web-audit-foundation.md) is deployed, its supplied
catalog report is verified, and local tests passed. Its guarded user RPCs capture U01-U04 as
`users.listed`, `user.updated`, `user.status_changed`, and `user.deleted`.
The client writer is integrated with audit viewing, details, filters, paging and
refresh. Wiring and acceptance of other task rows remain pending. Other mutation
paths and durable failed/denied capture are not implemented by this foundation.

These rows track the audit viewer and capabilities that still require integration.

| ID | Capability | Proposed event | Current status |
| --- | --- | --- | --- |
| F01 | Read/filter/refresh/export audit history | `audit.viewed`, `audit.queried`, `audit.refreshed`, `audit.export_generated` | Viewer has observation capture, server filters, cursor paging and a right-side selection panel; initial viewer acceptance reported completed, export remains pending |
| F02 | Use the full SMS console | `sms.parsed`, `notification.dispatch_requested`, provider outcome events | Component exists with native/provider/simulation paths, but active route is a placeholder |
| F03 | Assign/revoke administrative permission | `user.permissions_changed` | No current role-management action found; schema review must identify deployed permission writers |
| F04 | Edit/delete hotlines; persist settings; real notifications | Domain action and confirmed outcome | No active backend path for these operations was found |
| F05 | Self-registration or alternate login | Auth/provider event | Login/Register components exist but AuthWrapper uses Auth; registration is not an active admin route |

## Verification gates

- [x] Supplied deployed table shapes and status/membership contracts reviewed.
- [x] Supplied RLS, effective grants and privileged RPC bodies reviewed; access gaps documented.
- [x] Original snapshot had no application capture; foundation v1 deployed catalog verified.
- [ ] Live authenticated acceptance of user RPC capture and application access verified.
- [ ] Deployed permission-writer paths and Auth log storage configuration confirmed.
- [ ] Each C/A/D/M/P/R/U/V/E/H/S/N/L/B/F task row assigned a capture owner and test, as applicable.
- [ ] Client-observed, backend-confirmed, simulated and service events distinguishable.
- [ ] Common outcome vocabulary agreed: requested, succeeded, failed, denied, cancelled, no-change, pending/unknown, simulated.
- [ ] Failed/denied capture can survive a rolled-back business transaction.
- [ ] Shared database contracts, sensitive fields, deleted identities and retries accounted for.

Successful database mutations must commit with their audit evidence. All related
events share an operation ID; an operation may contain multiple affected records.
Keep requested/outcome phases explicit without duplicating the same success event.
