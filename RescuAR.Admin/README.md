# RescuAR Admin

React/Vite dashboard backed by Supabase. The PAGASA worker, PREP exposure API,
Supabase Edge Functions and Android SIM gateway run separately from the frontend.
Telemetry station-switch source updated on 2026-11-05; deployment and live acceptance have not been verified.

Current build blocker: `src/components/Sidebar.jsx` contains conflict markers
already present in `d83d797`, including lines 2 and 196. Vite rejects the file.
The commands below remain the setup/check procedure, but a complete frontend
build requires resolving that source conflict. This documentation update does
not change the sidebar or establish a working hosted interface.

## Local frontend

Requires Node.js 22.12 or newer. In `RescuAR.Admin/`, create a local configuration
from [.env.example](.env.example) if you do not already have `.env`, then replace
the placeholder values:

```powershell
Copy-Item .env.example .env
npm ci
npm run dev
```

Preserve any existing `.env`. It is ignored by Git. Vite reads `VITE_*` values at
startup/build time; restart the dev server or rebuild after changing them.

| Variable | Consumer | Purpose |
| --- | --- | --- |
| `VITE_SUPABASE_URL` | Frontend | Supabase project URL; also constructs the SMS function URL |
| `VITE_SUPABASE_ANON_KEY` | Frontend | Public anonymous/publishable API key for that project |
| `VITE_PREP_EXPOSURE_API_URL` | Frontend | PREP base URL, e.g. `http://localhost:8000` |
| `SUPABASE_URL` | Worker only | Supabase project URL |
| `SUPABASE_SERVICE_ROLE_KEY` | Worker only | Privileged server key; never put it in a `VITE_*` variable |

Set frontend values explicitly even though `src/supabaseClient.js` has legacy
fallbacks: the SMS console reads Vite variables directly. The frontend and worker
can read the same local parent `.env`, but only public `VITE_*` variables belong
in the browser bundle. Keep privileged keys out of those variables and source.

## Authentication and database prerequisites

`AuthWrapper.jsx` restores Supabase Auth sessions, loads `rbac_my_access`, and
checks `rbac_password_setup_status`. Accounts awaiting first-login password
setup see the password-change screen before the application. View permissions
come from `src/rbac/access.js`; hiding a view does not prove backend authorization.

The current gate requires the RBAC foundation and provisioning-status RPCs.
Reports Moderation and user operations also depend on older database contracts.
Read [database/README.md](database/README.md) before provisioning another project.
The SQL directory is incomplete as a database bootstrap; do not apply every file
in filename order or rerun deployed migrations to resolve a frontend error.

Administrator provisioning uses the `rbac-admin-provision` Edge Function and
`RESCUAR_ADMIN_ALLOWED_ORIGINS` in its server environment. Its retained
[provisioning notes](README_PATCH.md) describe the function/SQL contract; the old
ZIP installer instructions are historical.

## Telemetry worker

The worker reads `SUPABASE_URL` and `SUPABASE_SERVICE_ROLE_KEY` from
`RescuAR.Admin/.env` locally, not `scraper/.env`. A hosted worker reads its own
injected environment variables. Starting it writes to the configured database:

```powershell
cd scraper
npm ci
npm start
```

Before deploying this station-recovery change, review and apply
[`database/migrations/007_pagasa_station_auto_switch.sql`](database/migrations/007_pagasa_station_auto_switch.sql)
to the **existing** Supabase project. It provides an atomic, service-role-only
catalog switch. The worker cannot update station codes automatically until the
RPC is installed; it reports `STATION_MAPPING_REVIEW` instead of silently
switching when persistence is unavailable. No new worker environment values
are required. Never expose the service-role key in a Vite variable.

The worker reads current PAGASA `table_list.do` station codes and names,
checks `detail_list.do` for a recent timestamped water level and complete
source warning thresholds, and only then switches a catalog mapping. Exact
normalized names can follow a new unique code. The verified Sto. Niño
11104201/11104204 alternates are separate gauges: switching enables the
available code, keeps both gauge identities and their historical observations,
and does not rewrite past readings. Unclear matches, missing catalog entries,
old readings, incomplete thresholds, and API errors leave the existing mapping
unchanged and report a warning. Source-name changes on an unchanged verified
code are also reflected in the catalog.

`syncWorker.js` performs an initial sync and schedules checks every five minutes.
PAGASA observation age is separate from worker check time. The UI uses the source
observation timestamp: Fresh up to 15 minutes, Delayed over 15 through 30, Stale
over 30 through 60, and Unavailable over 60 or without a valid timestamp. Only
Fresh observations are eligible as live data in the freshness policy.
Monitoring Stations and River Level select enabled catalog station codes,
refresh periodically when Realtime messages are unavailable, and withhold
live warning labels for stale readings or unverified thresholds. A 30-minute
trend is derived from historical readings only when valid observations bracket
that interval; otherwise Unknown is intentional.
Historical research utilities are described
[separately](scraper/data/historical/README.md).

## PREP exposure API

Follow [prep_api/README_IMPLEMENTATION.md](prep_api/README_IMPLEMENTATION.md).
Set `VITE_PREP_EXPOSURE_API_URL` on the frontend and `PREP_ALLOWED_ORIGINS` on
the separate R service. An unset frontend URL leaves exposure requests unavailable.

The API estimates exposure to reference hazard polygons; it does not predict
live inundation or observe affected-person counts. Automatic Alert/Alarm/Critical
mapping currently falls back to provisional rules because the frontend requests
`GET /v1/exposure/rules`, which is absent from the checked-in API. The R metadata
includes its rule configuration, but the frontend's dedicated rules loader does
not consume that metadata as a replacement endpoint. File promotion alone does
not complete this connection.

## SMS and interface limitations

The SMS Parser navigation entry routes to `SmsOutboundConsole`, which queues
outbound SMS through a separately deployed function and SIM handset. Follow
[SMS_GATEWAY_SETUP.md](SMS_GATEWAY_SETUP.md); queued/sent states do not prove
recipient delivery. The separate Notify Residents broadcast modal is a local
simulation, including its siren and push choices.

System Settings changes are local state with a success timer, not persisted
configuration. System Logs displays static sample entries, not an audit-history
viewer. Residents Directory remains a placeholder. The
[task inventory](database/audit-task-inventory.md) distinguishes active service
calls, simulations and unverified audit capture.

## Checks and hosted frontend

From `RescuAR.Admin/`, run checks appropriate to the change:

```powershell
npm run lint
npm run build
npm run test:option-b
node --test tests/rbac-ui-regression.test.mjs tests/rbac-auth-regression.test.mjs tests/rbac-admin-provisioning.test.mjs tests/rbacAccess.test.mjs tests/reportsModeration.test.mjs
```

These are local checks, not authenticated Supabase, SMS-delivery or field tests.
Database fixture tests have missing prerequisites documented in the database
guide. `npm run preview` serves a local build. The hosted `npm start` script uses
the hosting shell's `$PORT`; it is not a portable Windows PowerShell preview
command. Configure frontend `VITE_*` values before the hosted build. Deploying
the frontend does not deploy SQL, Edge Functions, the worker, PREP or the handset.

`APPLY_PATCH.md`, `PATCH_README.md`, `README_PATCH.md` and the `Apply-*.ps1`
files are retained patch history. Their original ZIP layouts are absent; do not
run the installers against this integrated checkout.
