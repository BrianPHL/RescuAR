# Audit Logs: deployed schema review

Reviewed: 2026-10-07. Evidence: the supplied `audit_schema_report`, version 1,
from PostgreSQL 17.6. The report parsed completely: 10 relations, 23 policies,
and 6 functions. Findings below describe the supplied snapshot; no live access
tests, database changes, or application changes were performed.

## Confirmed findings

| Finding | Evidence | Implementation consequence |
| --- | --- | --- |
| No application audit capture found in the inspected scope | No matching application audit relation; no triggers on the eight inspected public tables; the four user-management RPC bodies contain no audit writes | Build application audit storage and capture explicitly. Do not describe existing mock screens as real history |
| Administrator access means membership, not a role string | `public.admin_roles` has `id`, `created_at`, and `user_id`; current login and directory RPC identify administrators by a matching `user_id` | Use a server-side `EXISTS` membership check against the authenticated caller; no role-value clarification is needed |
| Four privileged RPCs allow anonymous execution without checking administrators | `get_all_users_with_roles`, `admin_update_user`, `admin_toggle_suspend_user`, and `admin_delete_user` are `SECURITY DEFINER`, owned by `postgres`, executable by `anon` and `authenticated`, with no authorization guard or fixed `search_path` | Protect these entry points before treating their callers as administrators; directory output includes contact and health information |
| Several business writes are publicly permitted | Unconditional public insert/update/delete policies on advisories and hotlines; insert/update on centers and community reports; an ALL policy on monitoring stations, combined with matching role grants | Adding an admin policy alone will not restrict existing permissive policies. Review and replace applicable write access deliberately, preserving necessary resident and worker paths |
| Broad grants also include table truncation | `anon` and `authenticated` have effective `TRUNCATE` privilege on all eight public tables | Revoke unnecessary table-wide privileges. RLS does not cover truncation; the report does not demonstrate an exposed HTTP truncation endpoint |
| Membership writes have a different current restriction | `admin_roles` has a public SELECT policy and no INSERT/UPDATE/DELETE policy, despite broad table grants | Ordinary row writes remain denied by RLS; do not claim anonymous callers can assign themselves membership merely from the grants |
| Auth history is separate | `auth.audit_log_entries` exists; inspected application roles have no table access | Preserve Supabase-managed Auth history. Its existence does not prove logging is enabled or provide business-action audit coverage |
| Settings have no deployed persistence table | `public.system_settings` is absent; the screen currently saves local state | Log an observed settings submission accurately; do not report a database configuration update |

PostgreSQL documents [RLS defaults, permissive-policy combination, and table-wide
operations](https://www.postgresql.org/docs/17/ddl-rowsecurity.html), and
[security-definer function protections](https://www.postgresql.org/docs/17/sql-createfunction.html).

## Existing behavior that affects audit accuracy

- `public.users.status` exists and defaults to `active`, but the directory RPC
  does not return it. UserManagement substitutes `active` when the field is
  missing, so a refetch loses the displayed suspension state.
- The suspension RPC only changes that profile flag. It does not ban the Auth
  account, revoke sessions, or establish suspension enforcement. Current self-
  update permissions also allow a user to update their own status column.
- Deleting a resident cascades to `public.users` and other referenced tables.
  `admin_roles.user_id` instead has a foreign key without a delete action, so
  deleting an account with membership can fail unless its membership is handled.
  Audit evidence must survive deletion of both the target and the administrator.
- `admin_roles.user_id` is nullable and has no unique constraint. An `EXISTS`
  check tolerates duplicate membership rows; current login's `maybeSingle()`
  can fail if duplicates exist. The metadata report does not establish whether
  duplicates actually exist.
- `AuthWrapper` renders the dashboard for any restored authenticated session.
  It also receives the sign-in event before Auth's separate membership check
  completes. Server authorization must protect operations independently, and
  the wrapper must validate administrator access before admitting a session.
- Profile records contain medical, contact, and location information. Avoid
  copying complete user/report records, credentials, JWTs, or arbitrary request
  bodies into audit payloads; use allowlisted fields and redacted change details.

## Next bounded implementation

The [step 2 foundation](web-audit-foundation.md) is now installed and its supplied
deployed catalog report is verified. See
[foundation-deployment-review.md](foundation-deployment-review.md).
The findings above still describe the original pre-migration snapshot.

Prepare the database audit foundation: protected event storage, a shared
server-side administrator check, and controlled read/write entry points. Derive
actor identity and recorded time on the server, and preserve historical actor
identifiers when accounts are deleted. Browser observations must be distinguishable
from database-confirmed changes and provider-confirmed outcomes.

Include local tests proving denied anonymous/non-admin access, administrator
access, protection against direct log edits/deletion, and preservation of event
history. Then supply the migration and a read-only verification query for the
Supabase SQL Editor; deployed verification remains a separate step.

The privileged RPC guard and shared-table write restrictions are required before
claiming complete, trustworthy administrative coverage. Track them explicitly
through integration; do not broadly change resident/mobile or ingestion policies
as a side effect of adding the audit table. Successful mutations need audit
evidence in the same transaction. Failed/denied operations need a separate path
whose evidence survives rollback.

The remaining task coverage is tracked in [audit-task-inventory.md](audit-task-inventory.md).
The foundation installs user-RPC audit capture; authenticated application
acceptance and other-module integration remain pending. This review records the
deployed-schema portion of step 1, not completion of the audit module itself.
