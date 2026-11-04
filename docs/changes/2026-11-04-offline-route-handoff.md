# Offline route handoff and camera presentation

Date: 2026-11-04 (Asia/Manila)
Starting source: `dev`, `cb8c760`; runtime code unchanged from `d83d797`.

## Evidence and purpose

The supplied November 4 screen recording used the `d83d797` APK. Sampled frames
show downloaded MLD geometry surviving network loss, then an A* recalculation
increasing remaining distance and entering road-entry recovery. Separate sampled
frames show the camera background replaced briefly by the scene skybox. The
recording alone cannot establish the precise GPS/replan trigger or native reason
for missing camera frames; no matching device log was supplied for this trial.

Source review found that network loss intentionally retained MLD's algorithm
label, every replacement discarded registration/road-entry state, and the draw
loop could present the scene with no qualified camera texture. Offline graph
loading and A* searches already run in background tasks; they were not moved or
rewritten on an unsupported main-thread diagnosis.

## Changes

- `OfflineRouteHandoffPolicy` separates trip controller selection from immutable
  route provenance. Network loss selects A* and labels saved MLD geometry without
  cloning/relabeling the route or clearing progress/AR ownership. Five-minute
  expiry uses background local refresh with fresh measured position and a
  30-second retry interval. Existing confirmed deviation/hazard/destination
  invalidation remains available. Online restoration does not change this trip's
  provider. `HybridRoutingService` accepts explicit offline replacement ownership,
  including hazard planning, so retained MLD provenance cannot cause a remote
  request after reconnection.
- Replacement acceptance transfers a supported registration only across the
  same qualified AR frame with fresh paired position. It clears pending route
  evidence, initializes the new route's progress and attempts ready publication.
  Road entry is carried only for an already confirmed corridor with both the
  measured and tracked positions passing the existing five-metre entry limit;
  other replacements use existing recovery checks. Geographic acceptance also
  rechecks current position/provider before publication.
- `ARCameraPresentationPolicy` prevents draws without a fresh/current camera
  texture; the existing in-flight GPU conversion and teardown rules remain intact.
  Camera recovery reuses the existing overlay and styling, monitors completed
  presentation rather than only instantaneous texture availability, and provides
  recovery/map status for sustained gaps. It does not restart the camera session.
- Added focused policy/provider/registration/presentation regression checks and
  the `--offline-handoff` runner option. Updated the owning [routing guide](../../RescuAR/Navigation/Routing/README.md)
  and [field procedure](../field-testing/README.md); regenerated the catalog.
  Dependency pins, native bridge, routing datasets, arrival thresholds and Depth
  protections are unchanged. No appearance redesign was required.

## Verification

- 293 focused checks passed with `--offline-handoff`, including forced local
  ordinary/hazard planning after reconnection, expiry/retry timing, transactionally
  rejected registration transfers, neighboring-road entry rejection, camera
  freshness/generation gates, recovery health and existing MLD ownership/shared
  guidance checks. An initial new-fixture dictionary declaration failed compilation;
  it was corrected and the allowed fix/retest passed. Later camera-health and
  entry-boundary refinements were checked successfully on their final source.
- Final diagnostic Android Release APK build passed, zero errors; existing
  `CS8601` in `SafetyCircleService.cs:274` remains. Static ARCore release readiness
  and exact APK native/manifest/build-profile package checks passed. Native bridge
  SHA-256 remains `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- APK v2/v3 signatures passed. The default signer launcher initially lacked a
  configured Java path; invoking the installed Java executable directly resolved
  that tooling issue. Certificate SHA-256 is
  `2a4b597e3e629a4129445d71be767f2cd4c08f38c5d0c48d2124bc59861ed858`.
- Documentation catalog/link checks and `git diff --check` passed. No app/browser
  interaction, integration/E2E, live server change or physical-device verification
  was performed. No commit or push was created.

## Candidate identity

- Source revision profile: `cb8c760-offline-handoff-20261104` (uncommitted patch
  on `dev`, not an exact historical-commit APK).
- APK: ignored local artifact
  `artifacts/offline-handoff-20261104/packages/RescuAR-offline-handoff-20261104/RescuAR-offline-handoff-20261104.apk`.
- APK SHA-256: `5f070e48034e716e4e9e0c5076177fc984ce32b0350cfe5ba16041e328d1bf0b`.
- Build/profile, source-file hashes, checksums and field steps accompany the
  candidate package. These local artifacts are not checked into Git.

## Device acceptance

Pending on Honor X9B and Samsung Galaxy A54. Use the current field procedure's
online-to-offline trip, expiry, deviation, reconnect and AR/2D matrix. Expect
saved geometry continuity and qualified replacement placement, without scene
skybox flashes. A successful host check/build does not establish GPS accuracy,
camera stall freedom or physical street alignment. Obtain actual field results
before further behavior-dependent changes.
