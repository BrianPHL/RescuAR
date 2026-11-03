# AR route visibility regression — November 2, 2026

This records the earlier `af8826eb` candidate. The subsequent field capture confirms remaining failures on that APK; see [the Samsung/Honor follow-up](2026-11-02-samsung-honor-cadence-recovery.md) for the later diagnosis and replacement candidate.

The two-observation startup requirement and install/retest instructions below
belong to that earlier APK. They are not the current Camera policy or candidate
selection procedure. Use the [current field procedure](README.md).

Base: `dev` at `9a48b90`. Changes affect shared AR heading recovery and the Camera 2D position marker. Neither routing algorithm nor its route geometry is changed.

## Recording evidence

- `MLD.mp4` (314 s): the remaining distance updates throughout the walk. A cyan segment is briefly visible around 00:48. By 01:48 the banner reports route alignment recovery; from roughly 02:36 onward it reports “Direction not confirmed” while geographic guidance continues.
- `A_STAR.mp4` (253 s): the same direction warning persists while the remaining distance decreases. The 2D view around 02:00 and 02:48–03:00 displays the accepted route and measured position. Returning to AR does not produce sustained ribbon visibility.
- Reviewed sampled frames at twelve-second intervals, including full-resolution source frames. A recording does not expose calibration source, pose timestamps, APK identity, or the complete native state; the exact device transition that triggered suppression remains unconfirmed without logs.

## Reproduced defects and corrections

1. **A compass offset revoked a walking-validated heading.** Continuity compared an independent magnetic yaw directly with the geographic yaw corrected by GPS/AR walking measurements. An unchanged 40-degree sensor offset suspended the correction after three observations. The new failing regression check reproduces this using the actual heading service. Continuity now monitors changes against the sensor's own synchronized reference. It preserves the corrected geographic yaw and never rotates a route from the compass.
2. **Recovery could latch a stable movement calibration off.** Recovery previously restored only magnetic calibrations. Repeated restored sensor continuity can now restore the exact previously stable walking calibration in its original frame. An unfinished movement correction cannot gain trust this way. After a pause, a healthy calibration requires repeated support in the same world before adopting the new tracking epoch.
3. **An unstable initial compass result disappeared from recovery state.** Capture returned a best-effort estimate but did not retain it. The walking observer consequently had no heading candidate and returned before collecting evidence. The estimate is now retained as untrusted; fresh capture retries and independent walking recovery remain available. Walking recovery runs before position rejection for any unstable candidate, and acquisition motion uses that candidate's yaw rather than a previous trip's published yaw.
4. **AR uncertainty hid the measured map position.** The Camera map marker now consumes measured location separately from accepted route progress. It remains above route/shelter layers, includes the reported GPS accuracy circle, and stays visible as a gray, explicitly aged last-known fix during location gaps. Stale coordinates never become routing, distance, speech, or arrival evidence.

The initial two-observation AR registration requirement, timestamp pairing, route ownership, tracking, ground, hazard, and arrival checks remain enabled. Both A-STAR and MLD use the correction.

## Local verification

- The initial focused reproduction failed when an unchanged biased compass revoked movement alignment.
- The first combined check exposed a fixture timing error: consecutive groups supplied an immediate duplicate rather than three spaced observations. The fixture now spaces every observation, including the first in each group.
- `dotnet run --no-restore -c Release --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --ar-route-recovery`: **383 passed**. Covers both registered publishers with production two-fix confirmation, compass offset, sustained disagreement/recovery, pause/resume, noisy startup, partial correction rejection, existing heading/registration/ownership/stability checks, and map presentation.
- `build/Verify-ARCoreReleaseReadiness.ps1`: passed.
- Release Android APK build passed with zero errors and one existing `CS8601` warning in `SafetyCircleService.cs:274`. Native bridge, manifest, and package checks passed.
- APK: `artifacts/ar-route-regression-20261102/RescuAR-ar-route-recovery-af8826eb.apk`; source revision `9a48b90-ar-route-recovery-20261102`; SHA-256 `af8826ebd186a4ca7a3fc0857c244befe1071e1c24a7cf5cac0551da3e5e3915`. Detailed logs and package evidence are in that directory.
- No application interaction, emulator, integration/E2E, or physical-device validation was performed.

## Required device retest

1. Install the APK from this evidence folder and confirm its source revision contains `9a48b90-ar-route-recovery-20261102`. Test A-STAR offline and MLD online separately. Hold the phone facing the street while initial tracking/heading acquisition completes, then walk a short safe section. Expected: the mapped ribbon becomes visible and remains available as voice/text distance advances; a constant compass bias does not repeatedly revoke a corrected heading.
2. Switch AR → 2D → AR; stop and restart guidance. Expected: AR requalifies the current frame and republishes the accepted route without restarting the app. In 2D the measured marker and uncertainty circle remain visible; a paused GPS feed produces a gray last-known marker with an age, not a fabricated live location.
3. If persistent suppression remains, collect existing navigation logs with `build/Capture-NavigationFieldEvidence.ps1` and record the approximate video time. Do not treat successful unit checks or packaging as proof of street-level alignment or reliable operation on all devices.
