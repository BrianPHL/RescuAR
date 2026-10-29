# AR / 2D map routing audit — 29 October 2026

## Evidence

- Reviewed `Logs/RescuAR_FieldTest_20261029_192031.zip` and the 6m22s `LTqMHF1A.mp4`: 10-second samples across the recording plus larger frames around route starts, disappearing markers and AR entry. Review artifacts are under `artifacts/field-review-20261029-1920/`.
- Complete SM-A546E / Android 14 capture, 19:20:35.862–19:28:47.877 PHT. The installed APK hash remained `c920655899be53e4a29588fc1cec5ac5d3b0e8e5cd1522a85a0affc471db36e8`, matching the previous location-acquisition candidate.
- Current source baseline: `7b544e85c94efcea930d34462851b6cda4fbd1f1`; corrections below are uncommitted.

## How the views differ

The recording's routing map is the **Camera module's 2D Map sub-tab**. The separate bottom Map tab is a different page and is not the route renderer shown in this recording.

| Stage | Camera 2D Map | AR Camera |
| --- | --- | --- |
| Destination | Directory/map selection publishes `NavigationDestinationBridge` | Same destination bridge |
| Route calculation | CameraPage → MLDARIntegrationService → HybridRoutingService | Same request and accepted RouteResult |
| Algorithm | Online MLD; embedded A* on the offline path | Same provider decision; entering AR does not select a different algorithm |
| Position/progress | Same CameraPage GPS polling, matching and fusion | Same GPS pipeline; PDR additionally needs qualified motion/direction |
| Drawing | Projects geographic route points directly onto the map | Requires heading, acquisition-paired AR pose, geographic registration, ground/continuity and confidence before publishing/drawing geometry |
| AR lifecycle | ARCore intentionally paused | ARCore resumed; tracking/alignment independently reacquired |

`CameraPage.Map2D.cs` consumes `activeRoute` and `_routeProgressTracker`; it does not calculate a parallel walking route. A blue map route therefore proves geographic routing succeeded, but does not prove progress, physical road alignment or AR readiness.

## Confirmed failures

1. **Destination/reset race blocks both algorithms.** At 19:23:43.254 AR resume starts a request; at 43.276 the pending destination event cancels it; replacement starts are skipped as busy. Android reports this cancellation as WebException/Java IOException at 43.523. The finalizer only restarted for a different destination version, so this same-trip cancellation left no active route or progress polling. The same ordering repeats at 19:25:37 and 19:27:12. `ObserveCameraMapWindow` could consume a pending AR view request before OnAppearing reconciled the trip. This path predates the latest acquisition commit; it is not evidence that the native GPS replacement caused the cancellations.
2. **A* fallback overwrites usable instructions.** `TryShowNavigationFallback` excluded accepted MLD routes but not accepted A* routes. Whenever AR geometry was withheld, it could replace the accepted route's instructions with a straight-line destination summary / “Waiting for accurate GPS,” then hide the instruction panel. That message did not uniquely identify a GPS or routing-service failure.
3. **Map route-start pin is mistaken for live position.** Frames at 00:12 and 01:38 show a blue measured-location pin; later frames show only the fixed grey “Route start” pin. The live pin was removed when matching/stability was held or the fix expired. The grey pin is a route endpoint and never follows the user. Available GPS observations and accepted route progress were unnecessarily coupled for marker visibility.
4. **Empty polls can obstruct GPS recovery.** NavigationStabilityPolicy advanced its last-observation timestamp even for a timeout with no coordinate/accuracy. A later-delivered, still-fresh native fix acquired before that poll timestamp could be rejected as older. Targeted tests reproduce recovery starvation for both algorithm labels; the old capture lacks null-poll diagnostics to attribute each individual hold to this defect.
5. **Initial matching bypassed course qualification.** The startup match still used raw CourseDegrees, although subsequent matching used QualifiedCourseDegrees. Recorded GPS courses had reported direction uncertainty of 180 degrees and should not influence the initial match.

## What the capture does not establish

- Routes were accepted twice from MLD (302.9 m and 267.1 m) and once from A* (260.8 m). Retrieval was not universally unavailable.
- All 103 sampled renderer states were disabled; no registration decision was logged. This run broke before qualified geographic-to-AR placement, rather than demonstrating an invisible but correctly positioned cyan mesh.
- The first MLD trip's later GPS observations were about 39–43 m from the selected route. This is disagreement between two data sources, not proof that the physical phone, road map or route alone was wrong.
- Nineteen returned current fixes reported accuracy from 9.0 to 116.24 m (median 15.39 m); enabled Wi-Fi/5G/GPS did not guarantee precise positioning. Raw callback cadence is absent, so the reason for every acquisition gap remains uncertain.
- AR heading recovered for the accepted A* route at 19:26:52, while geographic confidence remained held. Low light/features caused additional tracking failures later. Fixing startup cannot guarantee precise alignment under those conditions.

## Focused corrections

- Reconcile the destination before applying pending view requests/resuming routing; guard every automatic route start against a pending trip reset.
- Restart explicitly cancelled work after the busy request unwinds, including same-trip quick re-entry. Treat wrapped cancellation as cancellation instead of a route-service error.
- Keep accepted A* and MLD instruction ownership during AR recovery. The destination-only fallback is reserved for navigation without an accepted route.
- Display fresh GPS observations with a labelled uncertainty circle independently of held route progress. Stale/missing/over-30m fixes remain hidden; held matching still withholds turn distances and nearby-distance claims. Neither the marker nor its circle modifies routing origins, arrival or AR placement.
- Publish current measured coordinates to the map from the no-route arrival/access polling branch too.
- Stop empty polls from advancing the real-observation timestamp; retain existing age, accuracy, jump and three-fix recovery requirements.
- Use qualified course for the initial match. Add timeout diagnostics with actual GPS/network callback counts and last acquisition times. No ML, dependency, routing-data or native-bridge change.

## Verification and candidate

- Release diagnostic APK build: passed, 0 warnings / 0 errors. Final incremental build after cancellation/no-route updates: 1m15.61s.
- `--alignment-recovery`: **880 passed**, including new timeout/recovery cases for both algorithms and existing PDR/registration/arrival safeguards.
- `--camera-map`: **43 passed**, including fresh observation versus held progress and expiry/accuracy exclusions.
- `Verify-ARCoreReleaseReadiness.ps1`, full `Test-ARCorePackage.ps1`, and `git diff --check`: passed. No UI automation, integration or physical-device test was performed.
- Candidate: `artifacts/field-test-20261029-route-lifecycle/RescuAR-20261029-Route-Lifecycle.apk`.
- APK SHA-256: `bbad4d0c9c80a9857ea2dcefcc3da21ea60540fd0e2cb0ba81a24c9bb04646b3`.
- Native bridge unchanged: `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`. Package evidence and source manifest accompany the APK.

## Required logged device check

Run on each available phone, once online for MLD and once on the offline A* path, at the same well-lit outdoor route. Start capture before opening the directory.

1. Start **AR directly from the directory**. Stand for 30 seconds, turn the phone, then walk 20–30 m. Expected: an accepted route retains instructions; AR recovery does not erase it or report a cancelled request as route unavailability. Cyan geometry requires qualified position, heading and tracking; no precise placement claim is justified while these are uncertain.
2. Switch to **2D Map**, then back to AR. Expected: the same route is retained; fresh blue GPS estimates update with uncertainty circles. The grey route-start endpoint stays fixed. Poor/expired fixes are explicitly unavailable, and held progress does not masquerade as measured movement.
3. Stop, select another center, start in 2D, then switch to AR without restarting. Repeat with a quick directory/back transition. Expected: new-trip route is accepted; any cancelled predecessor unwinds and restarts once; no permanent busy/unavailable state. Near the selected center, existing repeated arrival confirmation remains required.

Report actual results and upload the new capture before any further behavior-dependent alignment changes. A build and unit checks do not establish road-aligned device behavior.
