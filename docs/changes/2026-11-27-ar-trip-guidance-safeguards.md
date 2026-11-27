# AR trip guidance safeguards

Recorded on November 27, 2026 (Asia/Manila), starting from `dev` at `11fa1bc`.

## Changes and purpose

- A fresh held or conflicting fix invalidates explicit arrival eligibility and
  any open confirmation. A later qualified fix can enable a new confirmation.
- Emergency guidance checks live shelter status, capacity, and walking route
  safety before selecting a destination. Stale results cannot replace a manual
  selection. The existing 2D chooser handles cases needing user review.
- New hazard snapshots reassess the active route independently of GPS polls.
  Detours still require qualified positioning and final route validation. Expired
  coverage is reported while known exclusions stay in effect.
- Hazard refresh uses a four-second request budget and accepts up to 1,000 rows.
  Incomplete responses keep the previous snapshot. Coverage expires after two
  hours, and a complete newer response can replace the saved restrictions.
- Offline provider selection and the saved-route expiry and retry deadlines
  survive leaving Camera and reconnecting within the same trip after a handoff.
- The [routing contract](../../RescuAR/Navigation/Routing/README.md) and
  [Android field procedure](../field-testing/README.md) cover the new behavior
  and acceptance cases. The changes reuse existing Camera and 2D UI surfaces.

## Verification

- Passed the Release host build with no warnings or errors. Focused suites
  passed `--field-replay` (345), `--offline-handoff` (297),
  `--placement-hazards` (390), and `--field-cadence` (570). The suites share
  some assertions.
- Passed the ARM64 Android Release build with `RescuArDiagnosticBuild=false`
  and the existing Evergine content. Zero errors and the existing nullable
  assignment warning in `SafetyCircleService.cs:274`. The host and Android
  builds used the installed Evergine add-on cache in the Windows user profile.
- The documentation catalog was regenerated. The repository checker reported ten
  broken links in an existing tracked hotline backup. The reported failures were
  confined to that backup.
- Android device behavior and the live hazard feed remain unverified.

## Follow-up

Use the [field procedure](../field-testing/README.md#manual-device-matrix) to
verify arrival conflicts during an open dialog, hazard updates during a GPS
gap, offline re-entry, and emergency shelter selection. Record the actual
results with the candidate package identity before making behavior-dependent
changes.
