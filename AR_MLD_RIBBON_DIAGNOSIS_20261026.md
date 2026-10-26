# MLD ribbon and registration review — October 26, 2026

Reviewed checkout: `55089d8c73518fe038dfc448e7a5f3b5c2a0f1c6`, with the focused changes below left uncommitted. No logs were supplied for the photographed test. Earlier captures are not evidence of this run's GPS accuracy, heading, tracking state, route coordinates or installed APK hash.

## What the photos establish

- The instruction card persists and its remaining distance changes. Ordering by the displayed distance gives 259, 255, 250, 238, then 219 m; the last two supplied images are therefore reversed relative to that progression. Changing distance alone cannot prove a fresh GPS update: GPS/PDR can both advance accepted progress.
- Cyan geometry is visible, but its relationship to the street is wrong in several views. The later views show it crossing toward the opposite frontage rather than following the street ahead. Different camera viewpoints prevent a numerical measurement of drift from these photos alone.
- Every photo displays `Approximate route placement — GPS accuracy applies`. In the current source this message is selected when an MLD path is permitted without a currently qualified visual road registration. It does not report an established physical road alignment.
- The pictures cannot distinguish a wrong initial heading, biased GPS registration, accumulating AR drift, a frame/anchor change, or incorrect geographic route geometry. They do not establish that GPS/A-GPS stopped updating.

## What the source confirms

**Line versus arrow.** `ARRouteRenderer` already constructed a continuous cyan ribbon from joined cuboid spans, previously 0.55 m wide, then added two arrowhead wings at the final endpoint. An incomplete set of spans is rejected; depth-prefix blockage also disables its arrowhead. Computed MLD blocks the independent road-approach arrow. Thus there was no intentional arrows-only MLD mode or disabled line switch. The photos are consistent with the ribbon and endpoint cap looking like one giant arrow, and later a displaced ribbon seen across the view. Exact rendered entities, clipping and depth decisions cannot be reconstructed without telemetry.

**Location versus progress versus placement.** MAUI location supplies actual coordinate/accuracy/acquisition time. The Camera loop checks freshness, paired pose, position consistency and navigation stability. `RouteProgressTracker` matches the accepted geographic route; GPS/PDR fusion and the stationary filter determine committed trip progress and remaining distance. Before this change, the GPS-backed AR window still started at the raw GPS match retained by `RouteDisplayProgressPolicy`, while the instruction card used filtered committed progress. Stabilizing the number therefore did not establish an accurate AR transform.

**Why fresh GPS does not automatically fix alignment.** The geographic-to-AR registration is intentionally retained to avoid dragging the road around with GPS noise. Same-frame geographic corrections require stronger evidence. The heading service owns north-to-AR yaw, and native AR tracking supplies local motion; neither a new OSRM response nor a new location fix supplies physical road alignment. A fixed yaw error can create increasing lateral displacement as the pedestrian moves: 10 degrees over 40 m is approximately 7 m. This can look like growing drift even with a fixed erroneous transform. This is an explanation of the mechanism, not a measurement from these photos.

**Additional confirmed weaknesses.** Independent heading continuity previously left repeated disagreement between 8 and 45 degrees unresolved without withdrawing an already trusted heading. A contradicted visual road reference could also fall through to the weaker GPS registration. Both are source-level confidence gaps; the photos do not prove that either particular branch caused this test's failure. Depth occlusion hides some geometry behind surfaces; it does not validate a route's physical alignment or make a road walkable.

## Preferred approach and implementation

Keep the existing routing and AR stack and use **one retained map-to-AR registration with a nearby solid ribbon**. GPS supports geographic location, route identity and registration quality. Current AR pose advances the displayed window inside that registered frame. Physical road evidence can refine the transform automatically. Contradictory or unavailable evidence changes confidence instead of inventing another navigation arrow or moving the entire route to each fix. This is a focused extension of the existing architecture; no new service, package, manual alignment step or required walking calibration was added.

Implemented:

1. **Solid presentation:** normal computed route windows use a constant 0.25 m ribbon and no endpoint arrowhead. Verified access connectors retain their directional arrow. Joined segments and existing depth handling are preserved. This presentation change applies to computed AStar windows as well.
2. **Registered motion for MLD:** both GPS-backed and visual-backed MLD windows derive their display progress from current AR displacement in the retained transform. The existing camera-status refresh can publish a window after tracked movement without waiting for the next GPS poll. It requires an already accepted GPS observation or qualified visual registration; observe-only matching does not commit another GPS sample, step, arrival observation or reroute request. The AStar Camera publication path remains otherwise unchanged.
3. **One physical publication identity:** publication reuse compares the actual registration source rather than the name of the trigger. GPS polling, visual support and camera refresh cannot rebuild identical geometry solely because different callbacks requested it.
4. **Bounded approximation:** GPS/compass-only MLD geometry is limited to the existing 7.5 m local-window budget, rather than extrapolating a 40 m corridor. The short ribbon advances as the user moves. Visual registration may show a longer section within the existing observed-corridor and turn-uncertainty limits. This limits extrapolation; it cannot remove an unknown initial GPS bias.
5. **Alignment confidence:** three coherent, spaced independent observations differing by at least 15 degrees now withdraw heading trust. They never directly rotate the route. Confirmed contradictory road evidence blocks both the old visual reference and same-frame GPS fallback, with text guidance retained. Consistent physical evidence can restore its retained reference. Recovery and tracking/session/world checks remain in place.
6. **Traceability:** `NAV_ALIGNMENT` identifies registration source, visual contradiction, ribbon style/width, raw GPS progress, committed progress, displayed progress, horizon and frame identity. Projection diagnostics use the actual verified GPS coordinate/acquisition pair; AR-derived coordinates remain display-only.

A normal accepted fix advances the window along the **existing** OSRM polyline. It does not request a new route. Rerouting remains behind the existing geographic off-route policy: three trustworthy, spaced confirmations, a confirmation window and cooldown, plus existing identity, proximity, barrier and hazard checks. A misaligned drawing alone must not trigger a different geographic route.

[Google's anchor guidance](https://developers.google.com/ar/develop/anchors) describes how changing world estimates can produce apparent drift and recommends nearby content. The short approximate window reduces extrapolation risk; this change does not claim that all content is within 8 m of an anchor, nor that local tracking supplies surveyed absolute road alignment.

## Verification and remaining uncertainty

- Android Release diagnostic build: passed with zero warnings/errors. Final build log: `artifacts/mld-ribbon-registration-20261026/android-build-final.log`.
- Focused alignment/recovery runner: **836 checks passed**. New cases cover raw-GPS movement without matching AR displacement, a fixed future world point, walking, turns, floor handoff, expired support, route/frame/yaw changes, moderate heading contradictions and visual-to-GPS fallback refusal. The numerical registration cases run for both algorithms and three yaw values.
- ARCore release-readiness and whitespace checks passed. No dependency pins or native bridge changes.
- No application interaction, browser/E2E test, physical installation or new device acceptance performed. These checks do not prove that road classification will succeed in the photographed environment, that the original geographic route is correct, or that physical alignment is fixed. A GPS-only ribbon remains approximate; when confidence fails, explicit text guidance takes priority over misleading geometry.

## Smallest useful logged device test

Use one affected phone first, with the new candidate and the same MLD destination/street; repeat on the second phone after the first run is assessed.

1. With only that phone connected, start capture before opening guidance: `./RescuAR-FieldLog.ps1 -Start -AppOnly`. Capture continues on the phone after USB is disconnected.
2. Stand for 20 seconds and slowly pan, walk 30–40 m, then stop and pan for another 20 seconds. Take a photo at the start and immediately if the ribbon leaves the road; retain the approximate clock time. Take one map screenshot of the selected geographic route to distinguish a wrong route from a wrong AR transform.
3. Reconnect and collect: `./RescuAR-FieldLog.ps1 -Collect`. Expected: a narrow solid nearby line, no main-route arrowhead, stable scene attachment through panning, advancing window during walking, and persistent text with an explicit status when alignment is withheld. A line pointing into a building is a failed physical-alignment result even if all software checks pass.

The archive should contain `NAV_GEO_FRAME`/route chunks, `NAV_POSITION_PIPELINE`, `NAV_ORIENTATION`, `NAV_REGISTRATION_DECISION`, `NAV_ALIGNMENT`, `NAV_RENDER`, tracking epochs and native road-surface decisions. Together with the timestamped photo/map, these separate route geometry, raw positioning, yaw, retained registration, tracking/frame transitions, and actual ribbon visibility. No logs from the unlogged photo test are assumed recoverable.
