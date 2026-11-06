# Secure emergency hotline management — 2026-11-05

## Reason

The Admin Emergency Hotlines page could insert new rows directly from a client-side form, while Edit and Archive controls had no functioning handlers. The uploaded database migration set did not include hotline-specific row security. Client-side page permissions alone did not establish database authorization.

## Changes

- `src/components/EmergencyHotlines.jsx`: retain the current UI appearance, show edit and delete actions with confirmation, disable writes for users without `hotline.manage`, render failure/success feedback and refresh the list after writes. The parent `src/App.jsx` supplies session and access properties.
- `src/services/hotlineManagement.js`: validate contact data and recheck the live Supabase Auth identity and the `rbac_can('hotline.manage')` permission for each insert/update/delete. Treat permission denial and zero-row mutations as errors.
- `database/migrations/008_secure_emergency_hotlines.sql`: enable row-level security (RLS); revoke write privileges from anonymous sessions; add restrictive authenticated insert/update/delete policies evaluating the live RBAC assignment; preserve existing read visibility for mobile/public readers when RLS was previously disabled.
- `tests/hotlineManagement.test.mjs`: focused permission, input, write-result, mutation and wiring regression checks.
- Database guide: append deployment prerequisite and authenticated verification steps using the installer (avoids overwriting independent database README changes).

## Checks and limitations

- Focused JavaScript tests can be run with `node --test tests/hotlineManagement.test.mjs`.
- JavaScript syntax checked with `node --check src/services/hotlineManagement.js`.
- Source ZIP included only the Admin subtree, not the root `docs/README.md`, full design guide, project Git repository or documentation verification script. Canonical documentation routing and catalog verification must run in the complete repository after application.
- Existing Supabase schema, grants and row policies have not been inspected in the deployed environment; the migration must be reviewed against the existing database before execution. Live authorized and unauthorized user acceptance tests have not been performed. Does not implement historical hotline auditing, restore deleted rows or alter the mobile app.

## Installation / rollback

1. Back up the existing `emergency_hotlines` table and inspect its policies and grants. Apply migration `008` to the existing Supabase project before deploying the page; do not rerun RBAC bootstrap migration `004`.
2. Apply patch source files after verifying baseline hashes; keep installer backups. Deploy the frontend, and test create/update/delete as a signed-in Operations Admin and a signed-in user without `hotline.manage`, then confirm anonymous writes are denied and mobile hotline reads still work.
3. On failure, restore frontend files from installer backups and redeploy the previous UI. DB policy rollback must be designed from the **actual pre-migration** `pg_policies`/grants backup: simply disabling RLS would reopen unauthorized writes. Do not drop policies without restoring an equivalent secure baseline.

## Guide impact

Database guide gains migration and role/access notes through a non-destructive append; no changed design appearance or new frontend environment variables. Existing README and root documentation contracts otherwise remain unchanged.

**Proposed commit:** `fix(hotlines): enforce authenticated role-based hotline management`
