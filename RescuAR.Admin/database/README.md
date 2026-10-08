# Audit Logs database implementation

Status: the supplied deployed schema report has been reviewed. The task inventory
and [deployed-schema-review.md](deployed-schema-review.md) document the confirmed
contracts and access gaps. The step 2 migration is installed and the supplied
deployed verification report passed all nine checks. See
[foundation-deployment-review.md](foundation-deployment-review.md) for the result and
[web-audit-foundation.md](web-audit-foundation.md) for its scope and instructions.
The step 3 administrator gate and real audit viewer are now integrated locally;
[application-integration.md](application-integration.md) records behavior,
validation and the live acceptance reported completed by the user. Its UI revision
keeps details on the right and scrolling within the table/details content.
No additional SQL is needed for the gate and audit viewer.

## Reports Moderation integration

The supplied result of [the focused inspection](inspect-report-moderation-prerequisites.sql)
has been reviewed. The report confirms text IDs, nullable text status, broad
public insert/update policies, no report triggers, and matching audit helpers.
[report-moderation-audit.md](report-moderation-audit.md) records the deployed
findings, the tested RPC contract, frontend behavior, limitations, and live checklist.

Migration [002_report_moderation_audit.sql](migrations/002_report_moderation_audit.sql)
adds administrator-only approve/reject/resolve with atomic audit evidence,
retry receipts, and stale-status rejection. Shared policies and mobile writes are
preserved. The supplied deployment report passed all 13 checks and matched all
11 function bodies/access contracts. The web moderator now uses this RPC and
records deliberate opening, selection, media, filtering, and refresh observations.
All 33 application tests and synthetic browser checks passed; authenticated live
acceptance remains pending. Direct report writes can still bypass capture.
Migrations 001 and 002 are already installed; do not rerun them.

## Run the inspection

1. Open the Supabase project used by RescuAR.Admin, then **SQL Editor**.
2. Paste all of `inspect-audit-prerequisites.sql` into a new query and run it.
3. Copy the complete `audit_schema_report` result, or export the result as CSV.
4. Review definitions and defaults for credential literals, redact credential
   values if present, and share the result in the implementation chat.

The query reads PostgreSQL catalogs only. It returns table shapes, constraints,
indexes, RLS policies, grants, triggers, and relevant function definitions. It
does not read application rows, invoke application RPCs, or change the database.
Missing expected tables are reported without making the query fail.

Raw grants and effective role privileges are both included. Effective privileges
account for role inheritance but do not establish which rows an RLS policy permits.
The SQL Editor result is a schema review, not a test of actual administrator access.

The supplied report confirms that `admin_roles` has no role column: a matching
`user_id` establishes membership in the current application. No role assignments
or resident records are requested.
The presence of `auth.audit_log_entries` does not establish whether Supabase is
configured to store Auth logs there. Functions referenced indirectly inside
PL/pgSQL bodies may require a follow-up inspection after the first report.

## Review task coverage

`audit-task-inventory.md` maps the current routes and operations to proposed
events and their existing execution paths. Capture/acceptance for the remaining
modules is pending; the foundation has local tests. Local UI interactions, simulations, database changes, and external
requests require different evidence; the inventory records those distinctions.

The supplied report completes the deployed-schema review in step 1. The task
inventory records the available execution paths; capture ownership and acceptance
tests will be assigned during integration. The protected database foundation is
installed and verified against the supplied catalog report, with remaining access
gaps tracked in the schema review. The user reported authenticated application
acceptance of the integrated viewer/access gate completed and validated.

## Validate locally

Tests use Node's test runner and PGlite 0.5.8 in an ignored validation folder,
with no changes to the application package or lockfile. From the repository root
in PowerShell:

```powershell
$auditValidationRoot = Join-Path (Get-Location) 'RescuAR.Admin/node_modules/.cache/rescuar-audit-validation'
npm install --prefix $auditValidationRoot --cache (Join-Path $auditValidationRoot 'npm-cache') --no-save --ignore-scripts --no-audit --no-fund @electric-sql/pglite@0.5.8
$env:RESCUAR_AUDIT_VALIDATION_ROOT = $auditValidationRoot
node --test --test-concurrency=1 RescuAR.Admin/database/tests/inspect-audit-prerequisites.test.mjs RescuAR.Admin/database/tests/web-audit-foundation.test.mjs RescuAR.Admin/database/tests/inspect-report-moderation-prerequisites.test.mjs RescuAR.Admin/database/tests/report-moderation-audit.test.mjs
```

The tests execute the exact inspection query in read-only transactions against
empty and representative PostgreSQL fixtures. They verify missing-table handling,
RLS and privilege discovery, policy/trigger function discovery, overloaded RPCs,
and preservation of fixture data. This is local query validation; the supplied
Supabase report separately verifies the deployed catalogs. Foundation tests additionally
cover real role permissions, handling of broad default grants, atomic mutation
and audit commits, redaction, retries, history retention, cursor pagination, and
the metadata verification query. Fixtures contain synthetic records only.

Catalog inspection functions follow the [PostgreSQL documentation](https://www.postgresql.org/docs/current/functions-info.html).
The local fixture runtime follows the [PGlite documentation](https://pglite.dev/docs/).
