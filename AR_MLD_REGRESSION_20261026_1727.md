# MLD regression: alignment and stop/change-destination

Reviewed `5933e11` against preceding `55089d8`. Focused changes remain uncommitted. This report supersedes speculation made before the new capture arrived.

## Evidence

- Archive: `Logs/RescuAR_FieldTest_20261026_172703.zip`; extracted under `artifacts/field-review-20261026-1727/`.
- Complete A54/Android 14 capture, 17:27:06–17:37:33, one application process. Installed APK hash at both start and collection: `cf8e770fee2afdb3c9a1053c7b99872e1515ed3fe682d79828eda396ab92908e`, matching the latest solid-ribbon candidate. Native bridge matches the existing pin.
- The supplied screenshots show cyan geometry over buildings/plants and one held-position instruction. That proves a physical placement problem; it does not by itself distinguish location bias from heading, floor or tracking error.
- Actual logged GPS accuracy spans **6.0–49.45 m**, median **11.2 m** across 84 position-pipeline observations; 26 exceed 20 m. Initial displayed registration used a **7.5 m** accuracy estimate. Enabled GPS/A-GPS, Wi-Fi and 5G therefore did not supply curb-level positioning in this run. Provider/course-accuracy identification remains unavailable in the adapter telemetry.

## Confirmed findings

### Displaced line

The first MLD route was accepted at 17:29:32.515: six geographic points, 260.384 m. Its local EN trace is nearly straight, from `(0.592,5.415)` to `(-258.682,29.433)`. Route retrieval succeeds and the renderer draws the requested solid ribbon. This is not evidence of an OSRM request failure or missing cyan material.

At 17:29:45.263, `logcat-001.txt:2849` seeds the GPS registration. The corresponding published placement at line 2888 uses GPS EN `(5.276,0.743)`, acquisition camera `(0.089,-0.152)`, yaw `-90.669` degrees and maps the first route point to approximately `(4.816,-4.781)` in camera-world X/Z. Its displacement from the acquisition camera is approximately **4.73 m right and 4.63 m forward**. A compiled replay of the actual shared positioning code reproduces these values within 1.5 cm. That validates the numerical diagnosis, not the physical road placement.

No physical-road registration qualified: all 99 `NAV_ALIGNMENT` samples report `visual=False`. All 40 native road-surface samples report zero qualified fits. Semantic images were acquired successfully at `256x144` and transformed to the `1080x1798` viewport; this capture is beyond the earlier missing/mismatched-image failure. There is no evidence supporting an arbitrary leftward translation or a guessed compass correction.

The initial heading was magnetically repeatable (six samples, 1.14-degree spread), which is not a measurement of absolute heading accuracy. The GPS-backed transform deliberately retains its initial origin to avoid following noisy fixes. The latest release additionally derived GPS-backed window progress from AR motion in that retained transform. In this capture the displayed window lagged geographic progress: for example, raw/committed progress approximately 50.4 m versus display approximately 45.5 m at 17:31:30.455. That newer display-source change can make a short ribbon lag or leave the current view; it cannot correct the initial lateral bias.

**Physical alignment remains unresolved by the available measurements.** The capture proves coarse GPS-backed placement and a missing physical-road correction. It does not establish the exact true GPS error, absolute magnetic error, surveyed road centerline or contribution of gradual tracking drift. The coordinate replay does not reveal a sign/axis conversion error. Replacing the initial transform from the inconsistent raw fixes would risk additional wandering.

### Line missing after stop and destination change

The stop at 17:31:35.246 clears the destination; the old progress loop exits at 17:31:35.264. At 17:31:58.263 a new destination is published. A new route is accepted at 17:32:01.588 and a new progress loop starts. Subsequent routes are also accepted for trips 7, 9, 11 and 13. Cancellation, route request completion and new-route retrieval are working in this capture; no competing old process or orphaned old GPS loop explains the failure.

Tracking changes from epoch 4 to epoch 7. Independent yaw after recovery is roughly `-52` degrees versus the retained `-90.7` degrees. At 17:32:04.350 (`logcat-002.txt:321`), the cached magnetic heading is marked unstable and requires movement validation. The capture method then refuses fresh magnetic acquisition, and the automatic recovery timer also skips a heading in that state. Destination changes preserve the session cache, so later successful route requests inherit this blocked condition. `NAV_RENDER` subsequently reports zero active geometry; the renderer is not rejecting an otherwise published new path.

The latest change lowered the disagreement threshold from 45 to 15 degrees, making this recovery path easier to enter. The response should remain conservative about a contradicted heading; permanently carrying that block into a new tracked frame/trip was the defect. A second startup dependency had also been introduced: publication required a verified AR registration before permitting the heading-ready callback that previously established it.

Some short destinations legitimately finish guidance: the capture records `destination vicinity confirmed` at 17:32:20, 17:32:49 and 17:35:12. Those stops must not be removed to force an AR line to remain visible. Long-route trips remain blocked independently of those arrival events.

## Focused changes

1. Restore the preceding **GPS-backed display-position/PDR behavior**. AR-derived progression remains for a qualified visual-road registration. Retain the narrow solid ribbon, mapped geometry, registration-source cache, tracking/heading/hazard guards and the short approximate MLD horizon.
2. Retain an immutable **geographically accepted observation before AR readiness**. Heading completion can bind that exact observation when fresh; a newly received unclassified fix cannot bypass the acceptance gate. Changed heading/frame registration is invalidated before rebinding. This does not commit an extra progress, step, reroute or arrival observation.
3. Reacquire a **fresh magnetic calibration after a tracking-epoch change** in the same AR session/world. Fresh paired stationary samples are still required; the old yaw is not copied into the recovered frame. The recovery timer now permits that acquisition.
4. On a new trip, discard an **already invalid magnetic cache**, while preserving healthy same-frame alignment and stronger movement/visual-road evidence. Reset the trip's visual bootstrap key, surface-classification cache and native road request. Do not restart ARCore, duplicate subscriptions or recreate all render objects.
5. When geographic position is held and no physical-road registration exists, withhold the unverified MLD ribbon instead of retaining it alongside an uncertain-position message. Text remains available. Add native image-edge/ground-edge counts, expected direction and camera height to explain future failed road fits.

These changes repair the identified restart/current-window regressions and improve uncertainty handling. They do **not** make a 7.5 m GPS estimate into a precise physical-road reference or prove that the initial wall offset is corrected. Automatic visual alignment still needs usable road evidence; guessing a fixed offset from one photograph would not be justified.

## Verification

- Android Release diagnostic build: passed, zero warnings/errors (`android-build.log`).
- Focused alignment/recovery runner: **845 passed**, including nine new tests exercising stationary magnetic acquisition, changed tracking epoch, healthy trip reuse, invalid-cache discard, reacquisition after a new trip, and protection of partial movement calibration.
- After adding six captured-placement cases across AStar/MLD, the focused registration runner: **100 passed** (`placement-replay.log`). These reproduce the measured offset and ensure a trip reset removes its reference; they are not physical acceptance.
- ARCore release-readiness and whitespace checks: passed. Dependency pins, native bridge and arrival rules are unchanged. No integration/E2E tests, app interaction or physical acceptance run performed.
- APK and source hashes are recorded in `artifacts/field-test-20261026-mld-restart-recovery/source-manifest.json`.

## Short device validation

Use one connected phone to start `./RescuAR-FieldLog.ps1 -Start -AppOnly`, then disconnect USB if needed.

1. **Alignment:** choose a destination well outside the arrival radius. Aim down the actual road while standing for 20 seconds; walk 20–30 m and stop. Take a road-facing screenshot if the line is displaced. Expected: current route progress drives the GPS-backed window; uncertain unverified placement is hidden with persistent text. A line through a building is still a failed physical alignment result, even with an approximate-placement banner. New road-fit metrics distinguish absent boundaries from rejected ground fitting.
2. **Restart:** stop guidance, select a different center more than 100 m away, and reopen Camera without restarting the application. Repeat twice, including a map/Camera transition. Expected: the old route clears, fresh heading/position can bind the new trip automatically, and geometry reappears once its ordinary confidence gates qualify. A stale heading-recovery state must not permanently block the new trip. User walking is not required to reacquire the magnetic frame.
3. Reconnect and run `./RescuAR-FieldLog.ps1 -Collect`. Report which scenario failed and its approximate time. Repeat on the second phone after the first run is assessed. A map screenshot plus the road-facing image is needed if geographic route correctness remains in question.
