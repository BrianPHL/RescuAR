# Reports Moderation: database audit entry point

Status: migration 002 is deployed. The supplied `report_moderation_audit_report`
passed all 13 checks; all 11 function bodies and access contracts match. The web
moderator now uses the guarded RPC and acknowledged interaction capture locally.
Live authenticated acceptance of this frontend integration remains pending.

## Deployed schema review

Evidence: the supplied `report_moderation_schema_report`, version 1, captured on
2026-10-07 at 22:59:49 +08:00 from PostgreSQL 17.6. The JSON parsed completely.

- `community_reports` has 14 columns. Its primary key is `id text`, not UUID;
  `status` is nullable text with a `Pending` default. No status enum/check,
  foreign key, or application trigger was reported on the table.
- Three permissive PUBLIC policies allow reading, inserting, and updating
  any report. Anonymous/authenticated/service roles retain broad table grants,
  including truncation. Delete grants do not establish row-delete access under
  RLS: there is no DELETE policy for ordinary callers.
- `posted_by` is display text, and no authenticated author-ID column was
  reported. An ownership policy cannot safely identify residents from that field.
- All six RescuAR audit-related function bodies match the reviewed foundation
  after whitespace normalization. Owners, security modes, fixed search paths,
  execution permissions, private storage access, retry constraint, and history
  protection also match the supplied contracts. `auth.uid()` remains Supabase's
  server-side identity accessor.

The mobile service inserts `Pending` reports and sends whole report objects when
changing likes/comments. Migration 002 adds a separate guarded RPC without
changing those policies, grants, columns, or triggers. The inspected telemetry
worker does not reference `community_reports`.

## Apply and verify

Migration 002 is already installed in the reviewed project. Do not rerun either
migration 001 or 002. The supplied PostgreSQL 17.6 verification result confirms
foundation version 1, moderation version 1, protected storage, administrator
authorization, retry handling, RPC security, and preserved shared-table contracts.
The following sequence is retained for a separate project installing this step:

1. In the same Supabase project's SQL Editor, select the `postgres` role and run
   all of [002_report_moderation_audit.sql](migrations/002_report_moderation_audit.sql)
   once. Foundation 001 is already installed; do not rerun it.
2. If the migration fails, share its redacted error before proceeding. Its
   transaction rejects repeat installation, name conflicts, report contract/trigger
   drift, or changed audit helpers; it leaves no partial RPC installation.
3. After successful installation, run
   [verify-report-moderation-audit.sql](verify-report-moderation-audit.sql).
4. Share the complete `report_moderation_audit_report`. All 13 checks should
   be true. It reads catalogs only, not report/user/audit rows, and does not
   invoke application RPCs. The report explicitly identifies its RPC-only coverage.

Definition fingerprints identify reviewed code; they do not prove cryptographic
protection against database owners. Shared-grant/policy checks confirm preservation
of the existing access, not that the broad access is secure.

## RPC contract

`admin_moderate_report(p_operation_id uuid, p_report_id text,
p_expected_status text, p_new_status text)` requires the caller's current
administrator membership. Only `authenticated` can execute it; anonymous,
non-admin, missing-identity, service, and revoked-admin callers are denied.

- Supply a fresh operation UUID per intentional decision, and reuse it only for
  an identical retry. Report IDs use the foundation's bounded identifier format;
  UUID-shaped IDs are supported but not required.
- `p_expected_status` must be the exact loaded status, including explicit null
  when the stored value is null. Recognized values are `Pending`, `Approved`,
  `Rejected`, and `Resolved`. Unrecognized legacy status values need a separate
  repair step; never copy arbitrary status text into history.
- `p_new_status` accepts `Approved`, `Rejected`, or `Resolved`. This preserves
  the current screen's available actions without imposing a new transition matrix.
- The RPC locks the report, rejects a stale expected status with `40001`, changes
  only status, and appends `report.approved`, `report.rejected`, or `report.resolved`
  in the same transaction. A recording failure rolls back the mutation.
- Evidence uses the server-derived administrator, timestamp, `database` source,
  report ID, and actual before/after status. Outcomes are `succeeded` or `no_change`.
  Report titles, descriptions, locations, authors, media, and comments are excluded.
- A receipt returns `report_id`, `before_status`, `after_status`, `outcome`,
  `audit_event_id`, and `recorded_at`. Identical retries return that original
  receipt even after a later decision or report deletion, without reapplying the
  change. It describes the recorded task; refetch to establish the current state.
- Reusing an operation ID for a different report, expected status, or decision
  returns `22023`. Missing reports return `P0002`. Requests that fail or are denied
  leave no success event; durable failure/denial evidence is a separate requirement.

Operation-scoped advisory locking serializes conflicting retries, and row locking
plus the expected status protects separate decisions. Local fixtures test stale
requests and retries serially; live multi-session contention is not yet verified.

## Web integration

`ReportsModeration.jsx` delegates loading, observations, and decisions to the
report service/controller. It retains the existing queue, map, details, and three
decisions. There are no direct status updates in the web moderator.

- Opening, explicit table/keyboard/map selection, media opening, applied Search
  or Clear, and manual Refresh record acknowledged client observations under
  `community-reports-moderation`. Search records only `filter_applied`; report
  contents, search text, media URLs, and coordinates are excluded from payloads.
- Typing, default selection, rerenders, and automatic Realtime refetches add no
  intentional-task observations. StrictMode opening retries keep the same UUID.
  An unacknowledged interaction leaves the requested selection/filter/media
  navigation unapplied and displays a sanitized error.
- Decisions wait for a validated database receipt, then refetch current rows.
  They do not emit browser success events. An uncertain receipt retains the exact
  operation UUID, report ID, expected status, and decision for **Retry decision**,
  even if selection or Realtime data changes. New decisions are blocked until
  that uncertainty is resolved. Retry context lasts while the module is mounted.
- A stale decision reloads the report for review. A failed read retains previous
  rows and reports the failure; confirmed receipts remain acknowledged even if
  the following read fails. An access denial clears rows, selection, and pending
  retry state and requests the shared administrator gate to recheck access.
- Media navigation follows its acknowledged observation; unsupported attachment
  URLs are not navigable. Automatic refetch and request cleanup ignore obsolete
  responses after a newer read or module disposal.

## Validation and remaining coverage

Eight local PostgreSQL tests cover preconditions, administrator authorization,
all three decisions, null/unchanged status, privacy, retries, conflicting operation
reuse, stale decisions, atomic rollback, suppressed updates, historical retention,
shared mobile-style writes, preserved foundation verification, and metadata-only
deployment checks. Run from `RescuAR.Admin`:

```powershell
node --test database/tests/report-moderation-audit.test.mjs
npm run lint -- database/tests/report-moderation-audit.test.mjs
```

The full database suite is documented in [README.md](README.md): all 22 tests
passed. The application suite passed all 33 tests, including 12 report service/
controller tests for receipt validation, observation/privacy contracts, selection,
filtering, access denial, media ordering, stale reads, retries, and disposal.

```powershell
node --test tests/adminSessionGate.test.mjs tests/webAuditClient.test.mjs tests/prepInundationApi.test.mjs tests/reportsModeration.test.mjs
npm run lint -- src/App.jsx src/components/ReportsModeration.jsx src/services/webAuditService.js src/services/reportsModerationClient.js src/services/reportsModerationController.js src/services/reportsModerationService.js tests/reportsModeration.test.mjs
npm run build
```

Targeted lint, build, whitespace, and documentation-link checks passed. Broader
lint has existing warnings in other modules; the build retains the existing
large-bundle warning. Browser checks used the real
moderator with injected synthetic reports and RPC responses: explicit search and
keyboard/map selection, all three decisions, an identical retry after a committed
but lost acknowledgment, stale status, read failure, observation failure, access
denial cleanup, automatic refetch, and acknowledged media navigation passed.
These checks did not access or moderate live Supabase records.

## Live acceptance remaining

1. Sign in as an existing administrator and open Reports Moderation. Confirm
   current rows, map, and details load; test explicit selection, Search/Clear,
   Refresh, and an attachment.
2. For an intended moderation decision on an appropriate existing report, confirm
   the saved status survives refresh. In Audit Logs, filter the module to
   `community-reports-moderation`; confirm its decision has `database` source,
   the matching report ID, actual before/after status, and the correct outcome.
   Repeat the other decision types using controlled records as appropriate.
3. Confirm deliberate interactions appear as `client_observed` with `observed`
   outcome and without report content or raw search text. Automatic updates and
   default selection should not add extra deliberate-task entries.
4. Verify resident report submission, likes/comments, and worker ingestion still
   behave normally. Do not create artificial production decisions to test errors;
   the local fixtures cover denied, stale, rollback, and uncertain-response cases.

Live authenticated web/mobile acceptance and multi-session contention are not
established by the supplied metadata report or the synthetic checks above.

Existing public inserts/updates can still bypass moderation and its audit capture;
they can also overwrite a moderated status with a stale mobile object. Closing
that gap requires a separately validated shared-write compatibility change.
Other modules, durable failed/denied outcomes, and provider events remain pending.
