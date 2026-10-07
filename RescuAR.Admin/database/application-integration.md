# Step 3: administrator access and live audit viewer

Implemented locally on 2026-10-07. Uses the deployed foundation v1 RPCs;
no additional SQL migration is needed. The user reported the initial live
authenticated acceptance completed and validated on 2026-10-07.

## Implemented behavior

- `AuthWrapper.jsx` verifies `web_admin_access()` before mounting the application
  for a new sign-in or restored session. A matching `admin_roles.user_id` is the
  membership contract. False, failed, or malformed verification blocks access.
  Switching accounts, signing out, or losing membership invalidates access;
  stale responses cannot admit an earlier account. Retry also handles an initial
  session-read failure. Verified sessions are rechecked on Auth events and window
  focus without remounting the application while checking.
- **System Settings → Audit Logs** opens the real viewer. `System Logs` remains
  a separate existing screen. Audit rows come from `get_web_audit_logs()`;
  sample entries, pretend checksums and integrity assertions have been removed.
- Filters cover module, action, and administrator UUID. Applying filters queries
  the database, rather than filtering just the visible page. Invalid UUIDs stop
  before a request. Actor IDs remain available after an account is deleted;
  displaying them does not require fetching the resident directory.
- Pages contain up to 25 records. The timestamp and UUID cursor retains the
  database timestamp's precision. Cached newer pages stay stable as viewing the
  history creates new events; Refresh and applied filters start a fresh sequence.
- Record details show the actor, target, operation, timestamp, outcome and
  allowlisted metadata. Times use the device's timezone. **Database confirmed**
  and **Browser observed** identify the evidence source; an observed interaction
  does not establish a successful business change.
- The page fits the available viewport. The results table and right-hand details
  content scroll independently; headers, filters and pagination stay visible.
  The site font is used throughout, including IDs. Clicking a row, or using Enter
  or Space, selects it after the view event is acknowledged. A highlight and
  checkmark identify the selection. The details panel always remains on the right,
  with a click-an-entry placeholder when nothing is selected. Clear selection
  returns to that placeholder; there are no per-row View details buttons.
- Opening history or record details records `audit.viewed`; applying/clearing
  filters and paging records `audit.queried`; Refresh records `audit.refreshed`.
  The viewer waits for the event acknowledgement before performing that action.
  Metadata contains only page number and whether filters were applied, with the
  selected event ID for details. Raw filter values and resident content are excluded.
- The development StrictMode effect retry keeps the opening operation ID, using
  the foundation's retry key. Audit reads, rerenders, and clearing a selection
  do not recursively generate events.
- A recording failure prevents the requested read. Ordinary failures retain the
  previous page with a clear message. Permission/token failures clear history and
  ask the session gate to recheck access. The database still authorizes every RPC.

The session gate schedules RPC work outside the Auth callback, following the
[Supabase Auth callback guidance](https://supabase.com/docs/reference/javascript/auth-onauthstatechange).
Cleanup and stale-result handling follow the
[React effect guidance](https://react.dev/reference/react/useEffect).

## Validation

Application tests exercise access decisions, restored sessions, account changes,
late responses, retry, disposal, RPC parameters, timestamp precision, cached paging,
filter privacy, write acknowledgement and access-denial cleanup. Run from
`RescuAR.Admin`:

```powershell
node --test tests/adminSessionGate.test.mjs tests/webAuditClient.test.mjs tests/prepInundationApi.test.mjs
npm run lint -- src tests
npm run build
```

Final validation: all 21 application tests passed, source/test lint completed
with no errors, and the production build passed. A final lint of the changed
files reports only the two pre-existing Sidebar hook warnings. The build also
reports its existing large application bundle warning.

Browser checks used the real viewer and sidebar with synthetic RPC responses in
an ignored local fixture. Desktop layout, StrictMode opening deduplication,
three-page navigation, cached return navigation, record details, module/action
filters, invalid administrator IDs, empty results, write/read failures, denial
cleanup and recovery passed. This is separate from live Supabase acceptance.
The UI revision additionally passed default, 1024×600 and 768×720 layout checks:
document and page heights match the viewport, only table/details content scroll,
the details panel stays to the right, typography matches the site font, and click,
Enter/Space selection, clearing and error layouts work. Revised selection handling
also rejects additional row selections while an audit acknowledgement is pending.

## Live acceptance checklist (reported completed)

1. Sign in with an existing administrator account. Open **System Settings →
   Audit Logs**. Confirm records load and opening the screen appears as a
   **Browser observed** event for your administrator ID.
2. Refresh, apply an Audit logs module filter, and select a row to open its details.
   Refresh again to confirm the corresponding view/query/refresh events appear.
   Clear filters; check Older/Newer navigation if enough history exists.
3. Reload the application while signed in. Confirm access is verified before
   the dashboard opens and Audit Logs still loads.
4. If an existing account without administrator membership is available, sign
   in with it and reload. Both paths should show **Administrator access required**.
   Do not create or change memberships just for this check.

Share only the result or a redacted error; credentials and resident records are
not needed. Do not rerun migration 001 for this step.

## Remaining coverage

This step integrates the viewer and access gate. Successful user operations are
already captured by the four deployed user RPCs. Other module observations,
advisory/moderation/evacuation/hotline mutations, exports, Auth decisions and
durable failed/denied outcomes still require their own bounded integration steps.
The boolean access RPC does not write an access-decision event. Profile suspension
enforcement, other broad database policies and service/provider events also remain
as recorded in the task inventory and deployed-schema review. Whole-application
coverage is not yet complete.
