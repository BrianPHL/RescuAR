# Documentation alignment

Date: 2026-11-03 (Asia/Manila)
Starting source: `dev` at `d83d797`
Committed result: `15222e5` (`docs: align project guidance with current implementation`)

This retrospective record was added when the change-documentation workflow was
introduced. It records the earlier task's results; its application checks were
not rerun when writing this record.

## Changes and purpose

- Replaced the root and Admin patch-only READMEs with current project/service
  setup. Added the [documentation index](../README.md) and
  [environment example](../../RescuAR.Admin/.env.example).
- Reworked the [database guide](../../RescuAR.Admin/database/README.md),
  [task inventory](../../RescuAR.Admin/database/audit-task-inventory.md) and
  moderation report to identify missing foundation files, current RPC paths,
  inactive audit viewing and historical deployment/test evidence.
- Aligned PREP API, calibration/promotion and peer-validation guidance with
  implemented endpoints. Documented the missing rules endpoint and provisional
  frontend fallback; completed the documented event-registry inventory.
- Corrected the in-app System Documentation copy for telemetry age/thresholds,
  reference exposure, SMS versus simulated broadcast, settings and sample logs.
- Added the [current field procedure](../field-testing/README.md); aligned routing,
  compatibility/finding matrices and agent instructions. Marked older two-fix
  startup, batch and ZIP instructions as historical. Preserved candidate evidence.
- Updated SMS setup to describe the integrated source and actual CI directory,
  triggers, artifact and delivery limits.

## Verification recorded by that task

- 104 local inline links across 74 Markdown files: no missing targets.
- `git diff --check`: passed.
- Targeted Oxlint check of `Documentation.jsx`: passed.
- Default npm launch failed because its launcher targeted a missing npm CLI.
  Running the installed CLI reached Vite, which rejected pre-existing conflict
  markers in `RescuAR.Admin/src/components/Sidebar.jsx`. The markers were also
  present in `d83d797`; the sidebar was not changed by that documentation task.
- Application unit suites, browser/app interaction, live database/services,
  integration/E2E and physical-device checks were not run for that task.

## Remaining gaps

The missing database foundation/fixture artifacts, PREP rules endpoint and admin
sidebar build blocker remained unresolved implementation issues. Documenting them
did not establish hosted acceptance, carrier delivery or physical AR accuracy.
Current status belongs in the owning guides and later task evidence.
