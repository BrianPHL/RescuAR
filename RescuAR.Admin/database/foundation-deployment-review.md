# Audit foundation deployment verification

Reviewed: 2026-10-07. Evidence: the supplied `audit_foundation_report` for
foundation version 1 on PostgreSQL 17.6. All nine checks passed. Each of the ten
function entries was also checked independently for existence, matching body,
owner, fixed search path, security mode, and expected execution privileges.

| Area | Confirmed by the supplied report |
| --- | --- |
| Storage | Private schema, RLS enabled, no direct application-role access |
| Event contract | Eleven columns, server-generated event ID/time, required actor/operation IDs, bounded details and identifiers, expected source/outcome constraints |
| History protection | Update/delete and truncation prevention triggers; no account foreign keys that could cascade away history |
| Requests and retrieval | Retry uniqueness constraint and five expected indexes |
| Public entry points | Authenticated execution only; anonymous and service-role execution denied; administrator guards match the locally tested bodies |
| Private helpers | No execution rights for anonymous, authenticated, or service-role callers |
| Membership | Ordinary API roles cannot truncate the membership table or exercise its REFERENCES/TRIGGER privileges |

The inspected deployment matches the tested foundation. The migration has
already been applied to this project and should not be rerun.

This is deployed catalog verification, not a live authenticated application
test. No user records or audit event rows were supplied or inspected. The report
does not establish whether any administrative operations have occurred since
deployment or whether the browser can display their records yet.

The next bounded step is application integration: validate administrator access
through `web_admin_access()` before admitting a session, connect a real Audit
Logs screen to `get_web_audit_logs()`, and record its intentional interactions
through the client writer. Verify authorized/denied access, real history,
filtering, refresh, and pagination through that flow.

Other modules' mutation coverage, durable failed/denied events, provider outcomes,
suspension enforcement, and shared write-policy restrictions remain pending.
The [task inventory](audit-task-inventory.md) and
[foundation notes](web-audit-foundation.md) track those boundaries.
