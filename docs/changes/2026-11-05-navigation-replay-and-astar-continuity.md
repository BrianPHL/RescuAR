# A* handoff continuity, vicinity guidance and exact local replay

Date: 2026-11-05 (Asia/Manila)
Starting source: clean `dev`, `60986f0`.

## Evidence and decisions

The supplied A54/API 34 capture identifies `60986f0` and the same installed APK
at start/collection. Source/log/video review found that the five-minute A*
replacement followed the same mapped corridor as saved MLD, but failed AR
registration transfer then immediately reseeded from the same disputed fix.
The logged seed pairs reconstruct 25.302 m of placement translation. Original
accepted progress was about 68.8 m while the disputed GPS match was about 36.9 m.
A* preview freshness also expired at 5.317 s before the next observation.

Both recorded vicinity overlays followed selection of a different nearby
destination; neither established arrival at the original distant target. Current
vicinity handling automatically stopped guidance, and could count a geographic
sample held by navigation's precision checks.

The user authorized implementation of the audit recommendations and machine
replay before more field testing. The user then explicitly requested exact GPS
in ordinary logs without a capture consent prompt. Diagnostic builds now do
that automatically; production builds retain their existing redaction boundary.
Real private coordinates are not checked into this task's fixtures or report.

## Changes and purpose

- Added A*-only directed-corridor continuity and preview ownership policies.
  Same-corridor replacements remap accepted progress and GPS/PDR evidence while
  retaining the original registration, support age and pending corrections. A
  failed transfer no longer becomes single-fix startup. If no registration
  exists, replacement acquisition requires two later independent observations.
  Valid geographic geometry can be accepted while AR remains held. Changed
  corridors, frames, hazards and deviations retain their normal checks.
- A* preview retention now uses explicit trip/geometry/window/session/ground/
  epoch/world/calibration ownership and the existing eight-second ceiling.
  The existing MLD generation, projection, service and ownership decisions
  remain unchanged. Its shared publication helper only gains reset logging.
  Shared arrival/logging behavior is intentionally changed below.
- GPS vicinity is informational and no longer suppresses route startup, replans,
  turn cards or voice. Explicit eligible user-reported arrival completes guidance.
  A separate geographic evidence policy rejects stale, weak, held, impossible or
  conflicting provider observations without depending on AR tracking, and allows
  plausible walking between provider updates. Existing destination/version,
  crossing, freshness and explicit-action boundaries remain.
- Exact raw/selected location callbacks, original timing/accuracy/provider data,
  route batches, paired motion/frame ownership, startup/reset/transfer events,
  policy-stage inputs/results in evaluation order,
  replacement decisions and user arrival events are automatically logged in
  diagnostic builds. Versioned bounded chunks carry SHA-256, sequence and drop
  evidence; collection exports JSONL and quality metadata without private-file
  access or an extra capture switch. Removed the old in-app location prompt and
  updated build-profile logging metadata.
- Added a local policy-stage runner, export checks, capture/decision/arrival/
  replacement regressions and a clearly labelled synthetic relative November 5
  reconstruction. It recreates the 25.302 m disagreement and handoff state from
  relative evidence; the old bundle's redacted absolute GPS cannot be recovered.
- Updated the [routing guide](../../RescuAR/Navigation/Routing/README.md),
  [field procedure](../field-testing/README.md),
  [local replay guide](../field-testing/navigation-replay.md), finding/status/index
  references and catalog. UI changes reuse the existing Camera light panels,
  controls and explicit completion overlay. Static reference inspection used the
  Camera/nearby/green completion screens in the original visual board; no new
  visual language or material appearance departure was introduced.

Dependency pins, routing datasets, native Vulkan buffer ownership and heading
calibration implementation remain unchanged. Local replay does not start the app,
simulate native camera/tracking, certify GPS truth or establish device acceptance.

## Verification

- **Passed:** 328 focused `--field-replay` checks using restored dependencies and
  the actual root `.weproj`, including MLD preservation checks. Two independent
  new fixture assertions initially subtracted rounded route lengths instead of
  using the common-point geometric rebase (about 6 cm endpoint difference); each
  was corrected and its retry passed. No production threshold was relaxed.
- **Passed:** eight pure PowerShell replay-export checks for chunk reconstruction,
  precision, missing/corrupt records and sequence integrity.
- **Passed:** local CLI replay of 13 synthetic relative reconstruction records,
  zero input issues, contiguous sequence. The report labels the synthetic origin;
  it contains registration/handoff/PDR stages rather than a complete field trip.
- Final replay review found missing startup initialization, scoped recovery resets
  and ordinary observation/registration ordering. These recording/replay gaps were
  corrected before completion, with focused startup/recovery regressions added;
  they do not change MLD policy behavior. Replay also retains distinct preceding
  route objects when replacement geometry fingerprints are identical, covered
  for both transfer and A* rebind.
- **Passed:** static ARCore compatibility/manifest/GC-setting check.
- **Passed:** Android diagnostic Release build (zero errors, one existing
  `SafetyCircleService.cs:274` nullable-assignment warning). Build profile
  `60986f0-gps-replay-20261105`, diagnostics on, normal scene
  code generation retained, existing content reused (`GenerateEvergineContent=false`).
- **Passed:** documentation catalog regeneration and final link/import/catalog
  check (92 Markdown files, 379 local links), plus `git diff --check`.
- App/browser/device interaction, integration/E2E checks, new physical walks,
  live services/deployment, measured FPS gains and physical alignment acceptance
  were not performed. No testing dependencies/frameworks were installed.

## Follow-up

Use the [local replay guide](../field-testing/navigation-replay.md) and inspect
quality metadata before interpreting future exact captures. After local policy
review, run the same route through expiry, a safe deviation, AR/2D recovery and a
nearby destination. Expect stable corridor/progress ownership or a truthful AR
hold, continued guidance on vicinity, and terminal guidance only after an eligible
explicit action. Actual device results are required before further tuning.

Camera conversion/scheduling stalls, absolute heading/GPS bias, terrain handling
and general app/server dataset parity still require device or owning-data
evidence. No native caching or broad heading rewrite was bundled into these fixes.
