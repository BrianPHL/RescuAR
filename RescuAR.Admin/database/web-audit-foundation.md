# Web audit foundation: step 2

Status: deployed catalog verification passed on 2026-10-07. All nine checks and
all ten function entries match the tested foundation. See
[foundation-deployment-review.md](foundation-deployment-review.md).
Live authenticated application acceptance remains pending.

## Apply and verify

The migration is already installed in the inspected project. Do not rerun it.
These instructions are retained as a deployment reference.

1. In the inspected project's Supabase SQL Editor, select the `postgres` role.
2. Run all of [001_web_audit_foundation.sql](migrations/001_web_audit_foundation.sql)
   once. It creates storage and replaces the four inspected user RPC bodies in
   one transaction. Conflicting names or changed contracts cause an error.
3. After successful completion, run
   [verify-web-audit-foundation.sql](verify-web-audit-foundation.sql).
4. Send the complete `audit_foundation_report` to the implementation chat. All
   nine checks should be true. Verification reads catalogs only, not user or
   audit records, and does not invoke application functions.

If the migration reports an error, share it before continuing. Do not remove its
precondition checks or rerun it after successful installation. Definition
fingerprints identify the reviewed function version; they are not claims of
cryptographic protection against database owners. Keep `audit_private` out of
Supabase's exposed schemas; use the controlled functions in `public`.

## Behavior after deployment

| Entry point | Behavior |
| --- | --- |
| `web_admin_access()` | Reports whether the authenticated caller has membership in `admin_roles` |
| `record_web_audit_event(...)` | Records allowlisted browser observations using server-derived actor, time, and source; identical retries return the original event ID |
| `get_web_audit_logs(...)` | Administrator-only history, newest first, with cursor pagination, module/actor/event filters, and a maximum of 200 records per request |
| `get_all_users_with_roles()` | Requires membership and records a directory read/count; preserves return columns and avoids duplicate directory rows from duplicate memberships |
| `admin_update_user(...)` | Requires membership, allows four profile fields, preserves omitted fields, and records changed field names without their values |
| `admin_toggle_suspend_user(...)` | Requires membership, accepts `active`/`suspended`, and records a profile flag change; it does not establish an Auth ban |
| `admin_delete_user(...)` | Requires membership and records successful account deletion; existing foreign-key restrictions remain in force |

Existing RPC parameter names and return types are preserved. Missing targets
return an error; unchanged updates are recorded as `no_change`. The directory
still omits status, an existing issue reserved for user-management integration.

Anonymous and service-role execution is revoked. Authenticated non-admins fail
the server guard. Existing membership row policies remain unchanged; ordinary
API roles lose `TRUNCATE`, `REFERENCES`, and `TRIGGER` privileges on `admin_roles`.

Private storage has RLS, no application policies, and no direct application
grants. Triggers reject ordinary updates, deletes, and truncation, including
accidental owner writes. Database owners can still alter those objects. Actor
and target IDs have no cascading account references, preserving history after
deletion. Server-written payloads use redacted field names/statuses, not full
profiles or sensitive values.

## Contract for the next application integration

The client writer accepts a fresh UUID `p_operation_id` per intentional task,
`p_event_type`, the route ID as `p_module`, optional `p_target_type`/`p_target_id`,
and optional `p_details`. Reuse the operation ID only for identical retries;
distinct phases use distinct event names. Different data for an existing
actor/operation/event/source combination is rejected.

Allowed context keys are `returned_count`, `selected_count`, `page`, `page_size`,
`filter_applied`, `mode`, `template_id`, and `sort_key`, with scalar validation.
Do not submit raw search terms, form contents, JWTs, or provider payloads.
Browser events are always `client_observed`, including reported completions;
they cannot assert a trusted `user.updated`/`user.deleted` event or override
actor/time/source. Allowed event types and route IDs are enumerated in the SQL.

Pass the final record's unchanged `recorded_at` string and `id` as the reader's
`p_before_at`/`p_before_id` cursor. Preserve fractional seconds. Reading history
does not log recursively; the future viewer records an intentional view once
through the client writer.

## Remaining boundaries

- User RPCs record confirmed database outcomes after deployment. The viewer and
  browser events still need wiring; other modules' mutations/policies are unchanged.
- Failed/denied operations roll back. Durable failure capture, failed login,
  and provider outcomes require a separate trusted server path in a later step.
- User RPCs currently generate operation IDs on the server. Browser correlation
  and mutation retry IDs remain for integration; current deduplication applies
  to the client event writer.
- Directory logs describe backend requests, including automatic refetches.
  Integration must connect these to intentional tasks without double counting.
- Other public write policies, suspension enforcement, restored-session gating,
  and retention remain tracked in the schema review/task inventory. Preserve
  resident/mobile and ingestion requirements when changing shared write policies.

The implementation follows [PostgreSQL security-definer guidance](https://www.postgresql.org/docs/17/sql-createfunction.html)
and [Supabase function guidance](https://supabase.com/docs/guides/database/functions).
Local validation commands are in [README.md](README.md).
