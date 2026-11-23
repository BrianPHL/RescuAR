# RescuAR Admin — Phase 1 Safety + Phase 2 Audit Foundation

**Source baseline:** `RescuAR.Admin(20261123-124354).zip` (November 23, 2026).

Two *sequential* patches, **not** an entire repository replacement. Work on your own development branch, not directly on production.

## 0. Back up and verify branch

```powershell
git status --short
git branch --show-current
```

Commit or stash your existing local changes to the affected files before applying. Extract this ZIP OUTSIDE your repository, then copy the two `.patch` files to the RescuAR.Admin repository root. Do not copy an old version of the source tree over your current code.

## 1. Install Phase 1 FIRST — safety corrections (no SQL)

```powershell
git apply --check .\01_Safety_Corrections.patch
git apply .\01_Safety_Corrections.patch
node --test tests/operationalSafety.test.mjs
npm.cmd run build
npm.cmd run dev
```

**Behavior changes:**

- Notify Residents can no longer fake SMS/push/siren transmissions, a recipient count, or a success result. River Level no longer offers it as an enabled dispatch action.
- System Settings is read-only. Fake global flood thresholds, fake receiver endpoint, fake SMS key, and non-persistent Save button are removed. Existing worker cron remains **every five minutes**, as configured in `scraper/syncWorker.js`.
- Quick Generate Advisory never invents 16.2m, 2nd Alarm, affected barangays, an MCDRRMO signature, or a mandatory evacuation. It creates **Draft** only.
- Main Advisories creation and duplication default to Draft. Manual publishing/reactivation requires an explicit confirmation. Date of publication is only shown when a timestamp exists.
- Legacy Inundation Simulation dialog no longer claims to have computed a prediction.

### Required live checks before Phase 2

1. **Local UI:** Open Dashboard → New Advisory with no station data. Confirm reading is *Unavailable*, areas are blank, and no evacuation order appears. Save and inspect that the Supabase row is `status='Draft'`, and `published_at` is NULL. An actual SMS must not be sent.
2. **River Level:** With a recent verified observation, use Generate Advisory and confirm it is a **draft**. With old/unverified readings, auto-dispatch must remain unavailable; no false warnings should be published.
3. **Notify Residents:** Check that River Level's direct dispatch action is disabled, and any other place that opens Notify Residents clearly says broadcasting is disabled. No fake progress, delivered count, SMS row, push message, or siren should occur.
4. **System Settings:** Confirm no Save button or editable global thresholds; no false confirmation. Check Railway telemetry worker still polls every five minutes. Check Monitoring Stations still accurately shows PAGASA freshness/threshold verification.
5. **Manual Advisory publishing:** Create/duplicate a draft; changing it to Active requires explicit confirmation. Only authorized personnel should be able to perform this action; **client confirmation alone is not a security control**. Verify Supabase policies for advisories before production use.
6. **Integration regression:** Confirm Reports, RBAC, analytics dashboard, historical river level and station status still render. The independent SMS Composer is NOT disabled by Phase 1 and needs separate authorization/delivery checks.
7. **Production safety review:** Check that older published advisories, mobile app consumption, existing SMS queues and Railway configuration have not been changed by the patch. Roll out only after a dry run using non-emergency draft data.

If your deployed Supabase advisory schema rejects the `Draft` status or a NULL `published_at`, **stop and inspect its schema/constraints**. Do not change the DB blindly to make tests pass.

## 2. After Phase 1 is validated: audit foundation code

```powershell
git apply --check .\02_System_Audit_Foundation.patch
git apply .\02_System_Audit_Foundation.patch
node --test tests/systemAuditFoundation.test.mjs
node --test tests/*.test.mjs
npm.cmd run build
```

### Database setup is mandatory for System Logs

**Before applying SQL**, run in Supabase SQL Editor (as a project owner):

```sql
SELECT
  to_regclass('public.advisories') AS advisories,
  to_regclass('public.rbac_audit_log') AS rbac_audit_log,
  to_regprocedure('public.rbac_can(text)') AS rbac_permission_check;
```

All three must be present. Then inspect and run **`database/migrations/009_web_admin_audit_foundation.sql`** from the extracted patch source or the standalone file included in this ZIP. Run against the **correct Supabase project** (local/test first, then production after verification).

The migration creates:

- `public.web_admin_audit_events`: protected, indexed table; browser roles have no direct read or write permissions.
- Server-side triggers for committed `advisories` INSERT/UPDATE/DELETE and `rbac_audit_log` INSERT. Captures actor UUID where present, module, action, record ID, status changes or changed role ID — not full advisory content.
- `public.web_admin_audit_list(...)`: read-only, paginated RPC restricted to authenticated users with `system.all` permission.
- System Logs page with actual records, server-side filters, paging, and export of the **currently loaded page only**. No fictitious diagnostic entries or claims of cryptographic signing.

### Audit validation after SQL migration

1. Refresh Supabase API schema cache if needed (`NOTIFY pgrst, 'reload schema'` is in the migration).
2. In the local admin, sign in as an account with `system.all`, open **System → System Logs**. Initially, an empty list is normal.
3. Create a **non-emergency test advisory as Draft**. Refresh System Logs: expect an `advisory.created` event, actual authenticated actor UUID, and `outcome='succeeded'`.
4. Update the draft, check `advisory.updated`. Test a legitimate role assignment through the existing RBAC UI, and check `rbac.assignment_changed` (where the existing RPC inserts its RBAC audit row).
5. Use the module, event type, and optional actor filters. Test page navigation and current-page export.
6. Sign in as a role **without** `system.all`. Verify the System Logs page is inaccessible, and a direct call to `web_admin_audit_list` rejects access. Verify no direct INSERT/UPDATE/DELETE/SELECT permissions on the audit table for browser roles.
7. Confirm no passwords, contact numbers, message bodies, or sensitive personal records appear in the log rows. Unknown actors should be shown as *Not attributable*, not fabricated names.
8. Monitor Supabase errors during authorized advisory saves; if the migration fails, it is wrapped in a transaction. Do not simply bypass the audit trigger to hide write failures.

### Read-only audit check (project owner SQL editor)

```sql
SELECT id, recorded_at, actor_id, module, event_type, outcome, target_id, source
FROM public.web_admin_audit_events
ORDER BY id DESC LIMIT 15;
```

**Limitations to disclose during defense:** Events start when the SQL migration is installed; historical entries are not backfilled. This foundation does not yet capture login attempts, failed/denied operations, report moderation events in the legacy private audit store, hotline/evacuation/user edits, or gateway delivery results. Existing report moderation has a separate audit mechanism and should be integrated deliberately after verifying its deployed schema. This audit history is database-backed, **not cryptographically signed** or fully tamper-proof.

## Commits

Phase 1:

```powershell
git add src/components/Modals.jsx src/components/Advisories.jsx src/components/RiverLevel.jsx src/components/SystemSettings.jsx src/components/Documentation.jsx src/App.jsx src/utils/advisorySafety.js tests/operationalSafety.test.mjs
git commit -m "fix(admin): prevent simulated alerts, unsafe advisory defaults and false settings saves"
```

Phase 2:

```powershell
git add database/migrations/009_web_admin_audit_foundation.sql src/components/SystemLogs.jsx src/components/AuditLogs.jsx src/services/systemAuditService.js tests/systemAuditFoundation.test.mjs
git commit -m "feat(admin): establish protected audit event storage and real system logs"
```

**Validation performed in the patch environment:** Both patches applied sequentially to the exact source baseline; 105/105 Node tests passed. A live Supabase migration, production build/browser render, and recipient-delivery tests were not executed. Do not claim the feature is deployed until those are complete.

**Secrets:** This patch ZIP intentionally excludes `.env`, Supabase keys, credentials, `node_modules`, generated assets and your full project archive.

## Direct-copy fallback

If `git apply --check` fails because your repository contains additional changes, **do not force it**. This package includes `phase1_direct_copy/` and `phase2_direct_copy/` with the changed files in matching relative folders. Compare each file with your current version and merge carefully; otherwise you could overwrite newer work. Apply Phase 1 files first, validate them, and only then merge Phase 2 files. The Phase 2 SQL migration remains necessary and must be run separately in the correct Supabase project.
