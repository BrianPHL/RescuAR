# RescuAR Admin — Unified Dashboard and Analytics

**Base archive:** `RescuAR.Admin(20261123-120955).zip` (November 23, 2026).

## Included improvements

The existing RescuAR Command Center now has five tabs:

- **Overview:** Existing live KPIs, last-24-hour community report chart, geolocated reports/evacuation centers and recent report/advisory activity. Unchanged Supabase data logic.
- **Flood:** Active PAGASA station levels/freshness and selected-gauge historical trend (24 hours, 7 days, 30 days or 6 months). Uses the existing `pagasa_river_chart` Supabase function; historical gaps are preserved. The historical MMDA gauge remains separate in the River Level module. There is a link to PREP Exposure Analysis, but no invented exposure totals.
- **Community:** Live report submission trends, moderation statuses and categories for 7/30/90 days.
- **Users:** Resident registrations and account statuses for 30/90/180 days, based on the existing authorized `get_all_users_with_roles` RPC. Only aggregate statistics are displayed or exported. Account status is not user engagement.
- **Resources:** Recorded evacuation center capacity/occupancy, available places, advisory statuses and active advisory records.

Detailed tab code is loaded only on opening an analytics tab (`React.lazy`), and its Supabase requests are made only while selected. Overview's preexisting 60-second polling and subscription continue to work. All tabs support manual refresh; detailed tabs include period controls when applicable and aggregated CSV exports.

Dashboard tabs are only shown to roles that can view their corresponding management modules. Access to the underlying tables and RPCs must still be enforced by Supabase/RLS.

The unused, mock-data-only `src/components/Analytics.jsx` file is removed by the patch (it was not routed in App.jsx).

## Installation (recommended: Git patch)

1. Extract this ZIP **outside** your RescuAR.Admin project.
2. Copy `RescuAR_Unified_Dashboard_Analytics.patch` into the RescuAR.Admin project root (alongside `package.json`).
3. From PowerShell in that project directory:

```powershell
git status --short
git apply --check .\RescuAR_Unified_Dashboard_Analytics.patch
git apply .\RescuAR_Unified_Dashboard_Analytics.patch
node --test tests/dashboardInsights.test.mjs
node --test tests/*.test.mjs
npm.cmd run build
npm.cmd run dev
```

4. Open **Dashboard** and test each tab using authorized admin roles. In the Flood tab, choose an enabled PAGASA station and observe that data gaps and outdated observations are shown honestly. Verify Users and Resources only appear when authorized.
5. After checking the local UI and production configuration, commit and push to the branch tracked by Railway.

**Commit message:**

```text
feat(admin): integrate live analytics into unified tabbed command dashboard
```

### Direct-copy alternative

If `git apply` fails because your local source has drifted from the base archive, review the differences before overwriting your changes. The `direct_copy/` directory has the complete changed/new files in their intended paths. Back up your existing `Dashboard.jsx` before copying. Delete `src/components/Analytics.jsx` only if it is still the same unused mock-data file from the archive. Do not mix partial versions of these files.

## Data sources / safeguards

| Tab | Existing source | Limitation |
| --- | --- | --- |
| Overview | Supabase reports, advisories, centers and PAGASA stations | Report map pins are **not** verified flood hazard polygons; recent activity is **not** the full audit trail |
| Flood | `monitoring_stations`, `station_catalog`, `pagasa_river_chart` RPC | No risk status inferred from levels without verified thresholds; archived gauge not merged |
| Community | `community_reports` | Latest 1,000 reports per selected window; truncation is disclosed |
| Users | `get_all_users_with_roles` RPC | No logins/session engagement or barangay distribution without verified fields |
| Resources | `evacuation_centers`, `advisories` | Limit 500 centers / 1,000 advisories; capacity totals include only complete records |

A denied or failed query is displayed as unavailable or a data limitation, not as a fake zero. The feature uses existing migrations; **no new SQL migration is required** for the patch itself. The existing `pagasa_river_chart` function must be installed and accessible for Flood history.

## Verification performed

- Git patch applied cleanly to a fresh copy of the exact uploaded project.
- **93/93 automated tests passed**, including 12 new dashboard analytics tests.
- Browser rendering and production build were **not** verified in the execution environment (the uploaded project contains Windows-only native dependencies). Run `npm.cmd run build` and manually test the UI on your development computer before deployment.

## Unfinished work intentionally kept separate

- Full administrative System Audit Logs (not replaced with a simulated activity feed)
- Persisted PREP exposure scenario analytics and verified model validation
- Accurate session engagement tracking and structured barangay-based user statistics
- Real notification/SMS dispatch testing and persisted system settings
