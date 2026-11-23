# RescuAR Admin — PAGASA Telemetry Status Clarity Patch

Source baseline: `RescuAR.Admin(20261123-070940).zip`.

## Purpose

Make monitoring status understandable without mistaking a missing/old reading or unverified threshold for an active flood emergency.

## Changes

- **Monitoring Stations**: separate source check, worker check, observation age, and independently verified flood-risk assessment. The hydrological summary cannot report every station as Normal while any threshold is unverified. Hide zero-change trends when there is insufficient fresh evidence.
- **Marikina River Level**: show the connection and observation state separately. Use neutral pending/unknown assessments instead of red emergency indicators for unavailable data. Never enable flood-advisory dispatch based on stale readings or unverified thresholds.
- **Flood Hazard Exposure / PAGASA context**: follow the enabled Sto. Niño gauge in `station_catalog` rather than using the archived MMDA station unconditionally. Missing thresholds now suspend automatic reference-exposure scenario selection; the current gauge is shown explicitly.
- **Presentation and stylesheet**: avoid red badges for data gaps. Preserve red only for verified critical flood-risk alerts.
- **Tests**: add `tests/telemetryPresentation.test.mjs` with regression coverage for source timestamps, status labels, thresholds, gauge switching, risk assessment and automatic exposure gating.

## Observation age labels (underlying gates unchanged)

| Observation age | Label | Use for live flood-risk assessment? |
|---|---|---|
| 0–15 minutes | Updated | Yes, only if thresholds are verified |
| >15–30 minutes | Awaiting PAGASA Update | No |
| >30–60 minutes | Outdated Reading | No |
| >60 minutes or missing/invalid | Data Unavailable | No |

Connected means a successful worker/source check recorded within 15 minutes. A recorded `healthy` flag without a recent check is instead shown as **Connection Not Confirmed**.

Thresholds must have all three numeric values ordered Alert < Alarm < Critical. Without valid thresholds, the water-level measurement is displayed as **Thresholds Not Verified**, not Normal. **An old reading is never relabeled as live merely to avoid a warning.**

## Installation (PowerShell from your RescuAR.Admin repository root)

1. Back up or commit any unsaved local changes in the eight affected files listed below.
2. Extract this ZIP **outside** the repository (e.g. Downloads). Copy `RescuAR_Telemetry_Status_Clarity.patch` into the repository root.
3. Run:

```powershell
git apply --check .\RescuAR_Telemetry_Status_Clarity.patch
git apply .\RescuAR_Telemetry_Status_Clarity.patch
node --test tests/telemetryFreshness.test.mjs tests/telemetryPresentation.test.mjs tests/provisionalExposureScenario.test.mjs
npm.cmd run build
npm.cmd run dev
```

Run **`git apply` only once**. If it says patches are already applied, inspect `git diff` or run `git apply --reverse --check` first; don't force it.

If Git reports a context mismatch, **do not run `git apply --reject` automatically**. Compare your local edited files to `Direct_Copy/` and merge the relevant changes; the Direct_Copy folder includes complete replacements for the uploaded baseline.

## Files changed

- `src/components/MonitoringStations.jsx`
- `src/components/RiverLevel.jsx`
- `src/components/HazardExposure.jsx`
- `src/index.css`
- `src/utils/telemetryFreshness.js`
- `src/utils/telemetryPresentation.js` (new)
- `src/utils/provisionalExposureScenario.js`
- `tests/telemetryPresentation.test.mjs` (new)

## Manual acceptance checklist

1. With reachable PAGASA and a recent worker check, source shows `Connected`, even if the most recent measurement is >15 minutes old; the observation shows `Awaiting PAGASA Update` independently.
2. If the last check is older than 15 minutes, the connection is **not** shown as Connected even if Supabase has a stale `healthy` flag.
3. Without complete official thresholds, the level is visible but the risk shows `Thresholds Not Verified` (no confirmed Normal or Alert).
4. With a fresh valid reading and ordered thresholds, genuine `Normal`, `Alert`, `Alarm`, and `Critical` outcomes still appear.
5. For observations older than 15 minutes, trend and risk are not presented as live. On River Level, advisory and notification actions remain disabled.
6. Hazard Exposure references the enabled Sto. Niño gauge from `station_catalog`, not the archived one, and automatic reference-exposure mapping stays suspended when inputs are unverified.

## Verification limits

77 Node tests passed locally; the three edited JSX files were checked with a JSX parser. An end-to-end browser test against live Supabase and a full Vite production build must be run in your environment. The uploaded archive contains Windows-native build dependencies, so a Linux-native build was not available here. No SQL migration is required.

Suggested commit:

`fix(admin): clarify PAGASA observation, connection, and flood-risk statuses`
