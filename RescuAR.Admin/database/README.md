# Database contracts and setup limits

Reviewed against repository source on 2026-11-03. This directory contains
incremental changes for an existing Supabase project, not a complete database
bootstrap. Historical deployment results are reported evidence from 2026-10-07;
the current hosted schema has not been reinspected by this update.

## Available migrations

| File | Purpose and prerequisite |
| --- | --- |
| [002_report_moderation_audit.sql](migrations/002_report_moderation_audit.sql) | Guarded report decisions and atomic audit receipts. Requires the exact earlier audit foundation and report-table contract; its preflight rejects drift and repeat installation. |
| [003_pagasa_river_chart.sql](migrations/003_pagasa_river_chart.sql) | Chart RPC over existing PAGASA station/history tables; not a telemetry-schema bootstrap. |
| [004_rbac_foundation.sql](migrations/004_rbac_foundation.sql) | Role-based access control (RBAC), assignments, audit log and test records. Contains an explicit bootstrap-UUID step for an existing Auth account. |
| [004_sms_outbound.sql](migrations/004_sms_outbound.sql) | SMS queue and device claim/report functions over an existing application database. |
| [005_rbac_admin_provisioning.sql](migrations/005_rbac_admin_provisioning.sql) | Server-managed onboarding and password-setup status; install after `004_rbac_foundation.sql`. |

The two `004_*` files are separate features, not interchangeable migrations.
Review feature prerequisites and the actual deployed schema; do not run the
directory blindly in lexical order. Existing deployments must be inspected before
applying anything again. Do not rerun the RBAC bootstrap to repair sign-in.

## Missing prerequisites

The following referenced artifacts are absent from this checkout:

- `migrations/001_web_audit_foundation.sql`
- `inspect-audit-prerequisites.sql` and `verify-web-audit-foundation.sql`
- `tests/fixtures/step1-baseline.sql`
- `tests/inspect-audit-prerequisites.test.mjs` and `tests/web-audit-foundation.test.mjs`
- `deployed-schema-review.md`, `foundation-deployment-review.md`,
  `web-audit-foundation.md` and `application-integration.md`

Recover the reviewed originals or obtain a documented schema baseline before
bootstrapping another project. Migration 002 requires `audit_private.events`,
`audit_private.require_admin()`, `audit_private.append_event(...)` and
`public.web_admin_access()` with specific contracts/fingerprints. Reconstructing
them from guesses would not restore the original implementation or its evidence.
Other existing tables and user-management RPCs also lack a complete bootstrap here.

## Current application contracts

The frontend gate uses `rbac_my_access` and `rbac_password_setup_status`; it no
longer uses the historical `adminSessionGate.js`/`web_admin_access` client gate.
The older `admin_roles`-based audit/SMS contracts and newer RBAC assignments are
distinct. A role assignment alone does not prove access through every legacy RPC.

Reports Moderation calls `record_web_audit_event` for deliberate observations and
`admin_moderate_report` for decisions. The decision RPC changes status and appends
its audit event atomically when its database prerequisites exist. User operations
call `get_all_users_with_roles`, `admin_update_user`,
`admin_toggle_suspend_user` and `admin_delete_user`; their deployed bodies must be
reviewed before asserting authorization or audit coverage.

The `webAuditService.js` client remains in source, but the active System Logs view
shows static sample entries. There is no active audit-history viewer in `App.jsx`.
See [audit-task-inventory.md](audit-task-inventory.md) for current execution paths.

## Existing-project inspection

The available [report inspection](inspect-report-moderation-prerequisites.sql)
and [moderation verification](verify-report-moderation-audit.sql) read catalog
metadata. They do not establish authenticated application behavior or authorize
schema changes. Review function definitions for credential literals before sharing
output, and redact any secret values.

The retained [moderation report](report-moderation-audit.md) records the October 7
schema/result snapshot and the RPC contract. Its old deployment assertions and
test counts are historical; do not infer that every environment has migrations
001/002 installed or still matches those bodies.

## Local verification

Both remaining database test files require missing `step1-baseline.sql` and
foundation 001; the moderation tests also require the missing foundation
verification SQL. The suite cannot currently run from this checkout. Installing
PGlite alone does not fix those missing inputs. Restore and review the complete
fixture set before documenting a runnable database-test command.

The available frontend service tests are independent of those PostgreSQL fixtures.
From `RescuAR.Admin/`:

```powershell
node --test tests/reportsModeration.test.mjs
```

This tests synthetic service/controller behavior, not live Supabase permissions,
deployed RPCs, concurrent sessions or persistent audit history. Authenticated
acceptance remains separate from source and catalog verification.
