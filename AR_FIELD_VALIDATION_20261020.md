# Navigation stability candidate — 20 October 2026

Implementation authorized by the supplied Navigation Stability, Adaptive Route Correction,
AR UI Optimization, and Performance request. Changes remain uncommitted.

## Evidence and scope

Source baseline: `30096e7`. Investigation: `artifacts/field-review-20261020-1415/Findings-and-proposed-plan.txt`.
Capture: `Logs/RescuAR_FieldTest_20261020_141532.zip`.

- A* kept geometry `38d4645e…` for the main trip. At 14:23:39–43 its registration
  changed by 5.86 m, including a 5.04 m road-normal displacement, with unchanged
  floor reference, yaw, tracking epoch and explicit world frame. Support expiry
  also allowed single-fix reseeding at 14:21:44.993. These are confirmed shared
  placement defects. Their absolute physical error cannot be surveyed from these logs.
- After MLD resumed, the retained yaw was +116.79° while fresh independent
  observations clustered about 122° away. Heading trust stayed true, and a GPS
  innovation gate using that yaw could suppress observations needed for recovery.
  Sensor disturbance versus an unreported physical AR frame correction remains unresolved.
- The selected MLD destination intersected an approved 40 m flood exclusion:
  14:25:35.145 assessment, 14:25:35.157 `DestinationExcluded`. Changing the route
  cannot make an endpoint inside the exclusion safe. Conflicting forward/recovery
  messages were an application defect; the exclusion itself was not disproven.
- Both routers use the same MAUI Best-accuracy location adapter and shared GPS/PDR,
  map matching, arrival, placement and rendering policies. The captured source
  paths match embedded C. Benitez Street geometry; no geographic data, radius,
  coordinate convention, destination or provider preference was altered.
- Latest missing-safe-zone scope means the **selected-destination near/arrival notice**.
  Its existing distinct-fix automatic confirmation, separate user confirmation,
  destination versioning and panel priority remain in place.

## Implemented behavior

### Geographic confidence, position and rerouting

`NavigationStabilityPolicy` coordinates existing policies for both providers; it does
not implement another router or change the geographic polyline. Options are immutable,
validated and registered in `MauiProgram`; `CameraPage` receives the same configuration
for A* and MLD. Defaults use existing confidence/continuity budgets where available:

| Setting | Candidate default | Purpose |
| --- | --- | --- |
| Assessment interval | 3 s | At the normal 2 s polling cadence, assess roughly every other fresh fix. |
| Maximum reliable fix age / accuracy | 3 s / 20 m | Acquisition-paired, accurate measurements only. |
| Prolonged uncertainty | 15 s | Separate a brief hold from bounded recovery. |
| Recovery lifetime | 30 s | Stop estimating when continuity evidence becomes too old. |
| Maximum estimated displacement | 8 m | Retain the existing bounded display advance budget. |
| Maximum walking speed | 3 m/s | Configurable conservative pedestrian plausibility ceiling. |

The new cadence and timeout combination is a candidate setting, not a field-calibrated
accuracy claim. Fresh fixes still update positioning at the existing acquisition rate;
only deviation assessment is scheduled. Existing repeated off-route/backtracking
confirmation and cooldowns determine whether a same-destination replan is justified.
On-track measurements keep the route. No estimate can trigger rerouting or arrival.

Weak, stale, duplicate, implausible or upstream-held measurements do not replace reliable
progress or add rerouting evidence. Movement-distance checks do not depend on the yaw
being repaired. Returning from prolonged uncertainty requires three spaced plausible
reliable observations before resuming authoritative corrections.

Existing GPS/PDR fusion and its eight-second/eight-metre display bridge remain.
After that bridge expires, continuous, heading-qualified AR motion may retain the fixed
mapped reference up to the recovery lifetime. Before the uncertainty timeout it does
not advance the estimated window. After the timeout it can estimate at most eight metres
on the same mapped segment: no lateral displacement, reverse extrapolation, turn,
branch, session/epoch/world transition, hazard, or suspended route identity is permitted.
This display-only estimate never changes GPS, committed progress, a route origin or
arrival evidence. Unrecoverable positioning retains the geographic trip and map access,
with an explicit reduced-confidence message.

### Registration and heading

`RouteDisplayProgressPolicy` retains the acquisition-time geographic-to-world transform
through ordinary floor renewal. Expired support in the same frame no longer reseeds it
from a single fix. Overlapping uncertainty renews support without moving the transform.
Two similar GPS fixes no longer replace its full translation.

A bounded translation correction requires at least three coherent fixes, eight seconds
and eight metres of tracked movement, accuracy at most 5 m and at least twice as good
as the seed, and innovation outside combined uncertainty. Each accepted correction is
limited to the existing 2 m motion allowance and logged explicitly. Ambiguous evidence
cannot prove an initial absolute bias; it leaves the transform recovering rather than
claiming new alignment. A known new frame requires three spaced observations before
requalification. A new heading/trip still invalidates the old registration.

`HeadingContinuityPolicy` checks fresh independent orientation observations at a bounded
cadence. A healthy pause can requalify a retained yaw; sustained severe contradiction
withholds stable trust. It never applies a compass yaw to geometry. Movement recovery
continues using acquisition-paired, read-only route matches even while the position gate
holds GPS. A greater-than-90° correction requires agreement with an independent heading
observation in addition to the existing repeated GPS/course/route/AR movement checks.
Existing bounded correction steps and cooldowns remain. Unusable course or inconsistent
movement still cannot establish trusted directional geometry.

Supplemental access cues carry trip, geometry, calibration revision, tracking epoch,
world frame and target role. A mapped-target arrow uses the retained world registration
and current camera position, like the ribbon. An initial access estimate remains labeled
as access; it cannot establish road alignment. Real context changes invalidate stale cues.
`ARRoutePlacement.IsFor` now also checks epoch/world ownership. Small chevrons still use
the same route window as the solid pathway; an access cue never erases a qualified ribbon.

### Hazards and selected destination

Safety refresh, eligibility filters, exclusion radii, accepted outward escapes and selected
destination are preserved. A reliable origin is required before reserving a hazard reroute.
Ordinary hazards still request/validate a safe same-destination alternative through the
existing hybrid router. Unchanged failed/excluded assessments do not cause retry storms.

An unsafe outcome has one prioritized notice instead of competing forward/placement
instructions. A qualified mapped prefix may remain while finding an alternative or after
no safe alternative is found. It ends 2 m **before actual circle entry**, calculated against
all applicable exclusions. The old closest-point `distanceAhead` diagnostic is not used
as that boundary. No prefix is shown when already at/inside the boundary or its position,
heading, tracking, geometry or registration is unqualified. Direction-only access and
destination arrows and ordinary forward turn instructions remain withheld for the unsafe
route. The selected destination stays selected; a destination inside an exclusion is
explicitly blocked rather than silently changed or presented as a safe route.

### Compact AR interface

- Remove the persistent no-emergency information card; real advisories/hazards remain.
- Reduce the turn card minimum from 94 to 64 device-independent pixels and its main
  text from 19 to 16; retain readable turn, distance and selected-destination information.
- Remove the noninteractive chevron. Use measured card heights to stack visible
  emergency, tracking, low-light, turn and arrival information without fixed overlapping offsets.
- Limit card width and use a compact side column in landscape; adapt primary button
  width on narrow screens. Keep camera/map/flashlight touch controls at least 48 pixels.
- Avoid duplicating a hazard notice in both the top banner and route locator.
  Arrival notice priority and manual confirmation remain independent.

## Resource investigation and optimizations

The capture records 35.1°C at 14:17:01 and 39.3°C at 14:25:21. At 39.1°C the existing
thermal policy entered Warm and reduced target processing from 30 to 20 FPS; actual
pipeline telemetry reported approximately 19–20 FPS afterward. CPU/GPU energy profiles,
battery discharge slopes and a controlled after-build comparison were not supplied.
Wall-clock conversion latency includes scheduling/pause/fence waiting, not CPU time.

Confirmed repetitive work reduced:

- Full route visibility/bounds telemetry previously walked pooled segments every draw.
  Sample it at most every 250 ms while preserving immediate route/floor/root visibility
  transitions. Actual frame coherence, pose, drawing and depth safety checks remain per frame.
- Reuse an unchanged qualified registered window within the existing 3 m publication
  budget. Route, transform, ground, epoch, world, calibration, source or window-length
  changes bypass that reuse. Hazard clipping also changes the window-length key.
- Hash unchanged immutable service hazard snapshots once, instead of copying and hashing
  them on each progress poll. Public monitor calls still hash caller-owned inputs unless
  an immutable snapshot fingerprint is explicitly supplied.
- Coalesce unchanged hazard UI dispatches and unchanged persistent-cache serialization/
  writes. Cache persistence has a one-minute heartbeat; live remote refresh remains at its
  existing cadence, and changed hazard snapshots persist immediately.
- Correct a telemetry denominator defect: a thermal-mode change reset the FPS window but
  retained conversion counters, producing impossible converted FPS. Reset all counters
  together, including forced activation samples.

Already bounded paths inspected and retained: one serialized location loop; process-wide
leased orientation/accelerometer subscriptions; timer/GPS/PDR cancellation and sensor release
on camera exit; pooled route/arrow entities; newest-only camera buffers; asynchronous GPU
conversion; conditional route-only depth at 5 Hz/flood depth at 10 Hz; five-second thermal
sampling and existing heat-based frame/depth/probe limits. The native importer, camera
configuration, flood compositor, engine dependencies and scene generation are unchanged.

`NAV_WORKLOAD` records published/reused windows per accepted route. `NAV_STABILITY`,
`NAV_HEADING_TRUST`, `NAV_PUBLICATION_REJECTED`, registration correction vectors and
`NAV_HAZARD_STATE` explain decisions without a visibility override. Existing privacy
controls and geographic relative-frame logs remain. Host call-path reductions are not
proof of lower physical battery use or temperature.

## Verification status

Android Release diagnostic build succeeded: **0 warnings, 0 errors**, 85.42 seconds.
The first attempt exposed a missing diagnostics import and an overly broad command-line
scene-generation override; the import was fixed and the one permitted rebuild used the
repository's existing scene settings. Shared compiled-scene generation remains enabled.
Logs: `artifacts/field-test-20261020-stability/android-release-build.log` and
`artifacts/field-test-20261020-stability/android-release-rebuild.log`.

Build command:

```powershell
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False -p:RescuArDiagnosticBuild=true -p:AndroidPackageFormats=apk
```

Candidate: `artifacts/field-test-20261020-stability/RescuAR-20261020-Stability.apk`.
`source-manifest.json` records the base revision, uncommitted product source hashes,
APK hash, packaged build profile and native bridge identity. This is build provenance,
not device acceptance or an executed package/readiness suite.

Regression fixtures were added/updated
for expiry/reseeding, correlated bias, bounded precision correction, scheduling, weak fixes,
prolonged estimates, topology/identity bounds, read-only recovery, heading continuity and
true hazard entry. **No automated test suite, readiness script, integration/E2E test,
browser/app interaction or device test is run under this authorization.** The latest request
permits the application build only. Fixture execution/compilation is not claimed.

## Manual field validation

Use the candidate on the same phone, street and selected center for both algorithms.
Record the video and diagnostic collection start time together. Keep the build fingerprint.

1. Walk a straight section, a pedestrian lane and a turn; briefly stop and move sideways.
   Expect fixed mapped world points, matching ribbon/chevrons, stable selected destination,
   and no replan on a normal stop or on-track fix. Compare `NAV_GEO_ROUTE`,
   `NAV_REGISTRATION_DECISION`, `NAV_REGISTRATION` and `NAV_WORKLOAD` at visible changes.
2. Walk in the opposite direction on the same street for long enough to exceed reported
   GPS uncertainty. Expect both providers to confirm reverse walking and replan from a
   fresh reliable origin, with cooldowns; phone rotation alone must not trigger rerouting.
3. Pass under the establishment and resume open-sky walking. Expect bounded, explicitly
   uncertain guidance while continuous evidence qualifies; no sideways reseed on expiry,
   no estimated branch/turn, and honest map fallback if tracking or localization cannot be
   recovered. Log exact times at 8, 15 and 30 seconds of GPS degradation. Three reliable
   fixes must requalify geographic correction; independent direction recovery must remain reachable.
4. Repeat a camera-tab pause/resume, a floor renewal and a brief tracking interruption.
   A healthy retained yaw requalifies without rotating; contradicted yaw requires movement
   evidence. Verify near/arrival notice and separate confirmation still appear at the selected
   entrance. Capture `NAV_HEADING_TRUST`, `NAV_HEADING_PAIR`, `NAV_UI` and acquisition pairing.
5. With the existing hazard data, verify an ordinary avoidable hazard requests the same
   selected center, a prefix stops before the reported exclusion, and an excluded destination
   displays a clear blocked result without a forward arrow, unsafe ribbon or repeated identical
   requests. A resolved/changed remote snapshot must clear/reassess the notice without changing selection.
6. Check narrow portrait/landscape layouts and repeat comparable 15-minute runs with matching
   brightness, charge state, ambient conditions and route. Record battery start/end and thermal
   telemetry, FPS/conversion counters, window reuse and actual route requests. Compare navigation
   continuity as well as energy; improvement is pending these device results.

Remaining evidence: synchronized physical landmarks for absolute alignment, provider/course
accuracy and exact acquisition diagnostics where the OS adapter does not expose them, deployed
MLD dataset/profile identity, and controlled physical power/thermal measurements. Report the
actual outcomes before proposing further behavior-dependent changes.
