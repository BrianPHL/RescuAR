# RescuAR RBAC UI Navigation and Styling Patch

## What this patch changes

**Administrator Management** now appears as an indented, smoothly expanding child under **Community Management → User Management**. **Testing & Simulation** now appears under **Monitoring → Flood Hazard Exposure**. The old independent links at the foot of the sidebar are removed.

Both pages now use the same RescuAR design tokens and page layout as Dashboard/Monitoring: consistent page headers, cards, form fields, buttons, responsive tables, status messages, and mobile layouts. Testing remains clearly marked TEST ONLY.

## Scope and safety

- 3 modified JSX components (`Sidebar.jsx`, `RbacAdmins.jsx`, `RbacTesting.jsx`).
- 2 new scoped CSS files (`SidebarRbac.css`, `RbacPages.css`).
- 1 new UI regression test (Node built-in test runner).
- No modifications to `App.jsx`, `Auth.jsx`, `AuthWrapper.jsx`, authentication, Supabase functions/SQL, production advisories, or river telemetry.
- The original RPCs (`rbac_list_accounts`, `rbac_list_roles`, `rbac_set_assignment`, `rbac_create_custom_role`, `rbac_list_tests`, `rbac_log_test`) are retained.
- Navigation continues to use `canView` RBAC gating. Real enforcement must remain in Supabase RLS/RPCs.

## Installation (Windows / PowerShell)

1. Back up or commit existing changes first. Extract this ZIP **into the root of your existing `RescuAR.Admin` folder**, preserving the `RescuAR_RBAC_UI_PATCH` folder.
2. From a PowerShell terminal opened in `RescuAR.Admin`, run:

```powershell
& .\RescuAR_RBAC_UI_PATCH\Apply-RBAC-UI.ps1
```

The installer verifies source-file SHA256 checksums against your uploaded `RescuAR.Admin(20261026-045113).zip`, backs up the current files, and refuses to overwrite unexpected local edits. If your files have changed since that ZIP, review differences first and **only if you intentionally want to replace those local changes**, rerun:

```powershell
& .\RescuAR_RBAC_UI_PATCH\Apply-RBAC-UI.ps1 -Force
```

Using `-Force` still creates backups. Do not run it without reviewing the files it will replace.

3. Verify:

```powershell
node --test .\tests\rbac-ui-regression.test.mjs
npm run build
```

4. Run `npm run dev` and sign in as Superadmin. Check both nested sidebars, role-assignment UI, and test-only page. Test desktop and mobile widths.
5. Stage **only the files actually modified**, avoiding unrelated work:

```powershell
git add src/components/Sidebar.jsx src/components/RbacAdmins.jsx src/components/RbacTesting.jsx src/components/SidebarRbac.css src/components/RbacPages.css tests/rbac-ui-regression.test.mjs
git commit -m "style(rbac): align admin pages and nest links in sidebar"
git push
```

Note: The full Vite production build was not verified in the authoring Linux container because the uploaded node_modules contains a Windows-only Rolldown native binding. JSX syntax parsing and the five focused regression tests passed. Run `npm run build` on Windows before deployment.

## Expected sidebar

```text
Monitoring
  Monitoring Stations
  Marikina River Level
  Flood Hazard Exposure
    Testing & Simulation          (Superadmin only)
Community Management
  Reports Moderation
  User Management
    Administrator Management      (Superadmin only)
Content Management
System Settings
```

The parent rows continue to open their original pages; a chevron beside each parent toggles its new child submenu. The child submenus auto-expand when their views are active.

## No database migration

This UI-only patch requires no new Supabase migrations, no credentials, and no permission changes. It does not implement additional RBAC hardening.
