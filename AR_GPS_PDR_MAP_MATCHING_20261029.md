# GPS, PDR and map-matched AR guidance — 29 October 2026

This change removes the application-added machine-learning road-alignment system and addresses the positioning/visibility failures diagnosed in `artifacts/field-review-20261026-1858/DIAGNOSIS.md`. Earlier visual-road reports describe historical implementations, not current dependencies or readiness requirements.

## Removed

- ARCore Scene Semantics enablement, semantic-image acquisition, mask reprojection, road/sidewalk classification, road-boundary fitting and its frame bridge.
- Camera integration, physical-road registration/yaw overrides and their obsolete diagnostics and tests.

The feature is removed rather than retained behind an option. No application ML model, inference package, cloud service or new dependency is introduced. Normal ARCore camera tracking, existing ground/depth behavior and flood rendering remain. SDK-internal implementations are outside this removal's scope.

## Navigation changes

1. **One MLD placement transform.** Actual accepted GPS fixes, paired with acquisition-time AR poses, support the geographic-to-AR registration. GPS raw progress no longer independently selects a competing visible subsection. Tracked movement is projected onto the retained route, with an eight-metre continuity window preventing a nearby parallel/return leg from stealing the match. Discontinuous tracked jumps are refused.
2. **Bounded correction of a poor initial fix.** The original precise-moving correction remains. A lightweight deterministic GPS/pose residual filter can also update the origin while stationary: three distinct observations over at least eight seconds, within a thirty-second window, better reported accuracy than the seed, and at most three metres of residual scatter. Single outliers cannot vote outside overlapping uncertainty. Each accepted group moves the origin by at most two metres; filtered corrections remain within the seed uncertainty. The filter does not rotate the route, reduce reported GPS uncertainty, publish a synthetic location or alter arrival evidence. It runs at GPS-update cadence over three values, without image processing.
3. **PDR follows walking direction.** Qualified tracked walking displacement owns step direction independently of camera-forward yaw. Phone turns alone cannot establish walking evidence. Initial alignment and stationary rendering do not require steps. Existing eight-second/eight-metre GPS support limits remain.
4. **PDR updates the preview too.** Accepted steps may refresh an access-checked mapped preview before confirmed road entry. Their access/accuracy checks use the last accepted real GPS observation, rather than treating a predicted step coordinate as a location fix. A* retains its existing geographic display anchoring; the shared PDR heading and registration protections apply to both providers.
5. **Publication and visibility share eligibility.** The same mapped-preview policy permits a qualified existing preview through the five-to-eight-second polling gap. Route identity, access, accuracy, heading, tracking, destination ownership and expiry still apply. A new or invalid frame cannot borrow retention. Stop/change-destination continues to clear the old trip's registration and matching context.

Map matching remains interpolation on the selected route's segments, not snapping to a distant discrete vertex. Actual route retrieval and genuine-deviation rerouting policies remain; step predictions never become routing origins.

## Evidence and verification

- The new replay uses actual EN/GPS/pose readings from the 18:58 capture. It reproduces the original approximately 17.4 m virtual placement gap and accepts two corrections of at most 2 m each while retaining zero stationary trip progress, original yaw and 11 m reported uncertainty. This demonstrates recovery behavior, not surveyed physical road accuracy.
- **863** focused alignment/recovery checks passed, including **26** new cases covering the captured corrections, duplicate/outlier rejection, phone direction versus walking direction, parallel-leg continuity and shared preview decisions at 4.9/5.1/6.2/8.0 seconds. Existing routing, arrival, hazards, ownership, recovery and A* checks in that selection passed.
- The initial test run exposed an ambiguous parallel-route fixture: two roads 2 m apart cannot be distinguished by GPS alone. The corrected fixture supplies a reliable walking course to establish the outgoing leg before testing AR matching continuity. One fix/retest cycle passed.
- Android Release diagnostic APK build passed with **0 warnings and 0 errors**, 1 minute 54.32 seconds. The initial compilation identified an obsolete ML diagnostic reference; it was removed before the single successful rebuild.
- Release-readiness declarations and scoped whitespace checks passed. The final APK's stripped ARM64 native bridge, required symbols, alignment and build profile passed inspection; its native hash is unchanged. The complete package-test script could not finish because its `apkanalyzer` Java launcher failed first for missing Java configuration and then for a path containing spaces. No further automatic retry was made. Independent `aapt2` inspection of the final manifest passed package ID, camera/AR requirements, Vulkan version/level and required ARCore metadata. This is not reported as a full package-script pass.
- Source/APK hashes and inspection evidence are recorded with the candidate under `artifacts/field-test-20261029-pdr-mapmatching/`. The APK is approximately 156 MB because the current checkout already contains a 93.7 MB offline-map archive; this change does not add that asset.

No interactive app/browser test, integration/E2E run or new physical-device validation was performed. A successful build and captured-case replay do not establish correct road placement, device temperature, or improved battery life.

## Two-device field check

Start logging before navigation. On each phone, test MLD first, then A* on the same short route:

1. Stand for 20–30 seconds and turn the phone between road and gate. Expected: distance remains stable, text persists, turning does not manufacture steps, and qualified guidance does not blink merely at a five-second GPS poll boundary. The line must not pass through the house; record its time if it does.
2. Walk about 20 m, including a few steps with the phone pointing sideways, then stop. Expected: qualified walking steps advance distance and the mapped ribbon; a nearby return leg must not capture it. A genuinely poor fix or lost tracking may withhold geometry with persistent text/status.
3. Stop guidance and select another evacuation center. Expected: the old line clears and the new route returns after positioning/tracking recovery, without restarting the app. Confirm it is the selected route and road, not merely a visible cyan segment.
4. Near the actual evacuation center, check the proximity notice and confirmation behavior. Expected: real location evidence still controls proximity/arrival; PDR steps alone do not confirm arrival.

Report the actual results with the archive and timestamps before further behavior-dependent changes. GPS/compass-only AR remains approximate: this change does not claim precise curb/lane alignment from an enabled location service or map match alone.
