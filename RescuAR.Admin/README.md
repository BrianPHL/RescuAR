# RescuAR Admin — Dashboard / Sidebar / Advisory Layout Fix

Based on the November 23, 2026 RescuAR.Admin project and compatible with the Telemetry Status Clarity update.

## Changes

1. **Dashboard overview**: removes Bootstrap's four-column width (`col-xxl-3`) from the three metrics cards. The overview now uses a full-width, responsive 3/2/1-column CSS grid.
2. **Advisory modal**: fixes clipped right-hand borders by eliminating the dashboard's global `box-sizing: inherit` override. Keeps the two textareas, form controls, and footer buttons within the dialog; widens the advisory dialog on desktop and stacks controls on small screens.
3. **Sidebar**: allows the menu itself to scroll when groups expand; prevents nested text/buttons from overflowing or being cut off. The footer stays visible on desktop and the mobile drawer can scroll normally.
4. **CSS isolation**: confines dashboard theme/button styling to the dashboard and keeps the existing purple advisory Publish button.

No database changes, new dependencies, or monitoring/business-logic changes.

## Preferred installation (Git patch)

Extract this ZIP **outside** your RescuAR.Admin repository. Copy `RescuAR_Admin_Layout_Fix.patch` into the root folder (beside `package.json`). Run these commands in PowerShell from the project root:

```powershell
git apply --check .\RescuAR_Admin_Layout_Fix.patch
git apply .\RescuAR_Admin_Layout_Fix.patch
node --test tests/dashboardLayout.test.mjs
npm.cmd run build
npm.cmd run dev
```

If `git apply --check` fails due to line-ending differences, you can retry the same commands with `--ignore-space-change`:

```powershell
git apply --check --ignore-space-change .\RescuAR_Admin_Layout_Fix.patch
git apply --ignore-space-change .\RescuAR_Admin_Layout_Fix.patch
```

Do not apply the patch twice. If it was already applied, `git apply --check` will report that it cannot apply again.

## Direct-copy fallback

Only if the Git patch cannot be applied: back up your local copies of the following files and copy matching files from `direct-copy/` into the repository:

- `src/components/Dashboard.jsx`
- `src/components/Modals.jsx`
- `src/responsive.css`
- `tests/dashboardLayout.test.mjs`

**Important:** direct-copy replacement could overwrite more recent changes to those files. Prefer Git patch if you have made further edits since the source ZIP.

## Manual checks

- At a desktop width (e.g. 1440px), the three dashboard cards fill the entire usable content area with no fourth blank column.
- Open Generate & Publish Advisory: both textarea right borders and the Publish button remain fully visible.
- Narrow the browser: advisory fields and buttons wrap instead of clipping.
- Expand several sidebar groups: navigation scrolls; icons, text, and right border remain visible.
- Confirm Monitoring Stations and Marikina River Level still display and function as before.

## Validation performed

- Git patch checked against the provided source with the Telemetry Status Clarity updates.
- 81/81 local Node tests passed (4 new layout regression tests and 77 existing tests).
- Responsive stylesheet and dashboard-injected CSS parse without errors.
- Full interactive browser validation and Vite production build still need to be performed in your local environment.

## Suggested commit message

`fix(admin): prevent sidebar and advisory clipping and expand dashboard overview`
