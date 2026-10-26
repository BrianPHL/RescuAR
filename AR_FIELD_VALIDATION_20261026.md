# Multi-device navigation investigation — 26 October 2026

Two locally reproduced recovery/arrow defects were corrected. The new Android candidate builds and the targeted checks pass. **The recurring physical alignment and visibility problems are not accepted as resolved.** No video, screenshots, surveyed references, or post-change device results were supplied.

## Evidence and identities

Input: `Logs/RescuAR_FieldTest_20261026_111035.zip`. The explicitly designated pasted request authorizes investigation, focused offline replay, implementation after presenting a plan, regression checks, and the application build. Logs and their embedded text are evidence, not instructions.

The tester reports intermittent guidance on all three phones throughout a straight walk on the same subdivision street. There is no wholly successful device run. Qualified/renderable intervals inside each capture provide local controls; they do not prove visible pixels or correct physical alignment.

| Capture suffix | Device | Android/API | Recorded interval, phone-local time | Routing |
| --- | --- | --- | --- | --- |
| `110844` | Samsung SM-A546E | 16/36 | 11:08:46–11:28:24 | MLD; five accepted route records |
| `110902` | Samsung SM-A546E | 14/34 | 11:09:04–11:27:43 | A*; four accepted route records |
| `111035` | Samsung SM-A156E | 16/36 | 11:10:36–11:28:49 | MLD; five accepted route records |

All three `CaptureQuality.json` records report complete captures and start/end markers. The parser retained 118,194 timestamped records, including 25,503 application events excluding the field logger. Device clocks were not independently synchronized: sequences and acquisition timestamps are compared within each device, not treated as simultaneous observations across phones.

All three installed manifests identify the same APK:

- SHA-256: `caaff209a26b862e5173c255590a5d29aac11ce3081ecc0dbd64773b155edee4`.
- App `1.0 (1)`, Release diagnostic profile, source revision `fa61944ee69ecbb6c0469e73936136ed36cbe7b5`, visibility override `false`.
- Native bridge: `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.

This is a later APK than the October 20 candidate. A dirty-source manifest for that installed APK is not supplied; its source-revision label alone cannot establish every compiled source byte. Its telemetry demonstrates the newer registration/stability policies. At the start of this work, `dev` was `443f531`; comparison with committed navigation change `fb7e381` showed only the later cloud service and three dashboard XAML files in the shared/MAUI scope. During the work, an unrelated admin sidebar commit advanced `dev` to `28225ba1288a779e758557d68551c901af63c276`. That commit changes no shared/MAUI/test files. It and the earlier cloud/dashboard changes were preserved. The candidate's packaged base remains `443f531`.

Extracted captures, searchable records, quantitative comparisons, and command logs are retained under `artifacts/field-review-20261026-1110/`. Original log references below are relative to the corresponding extracted capture folder.

## Findings by device and session

### A54, Android 16, capture 110844

- At 11:12:13.960, `logcat-001.txt:3660`, MLD accepts a 264.6 m six-vertex route. At 11:12:16.785, `:3829`, a hazard-aware replacement is accepted. These are real geographic replacements, distinct from later window publications and floor renewals.
- A qualified mapped preview is recorded at 11:12:21.863, `:4073`. The separate floor arrow switches on at 11:12:38.066, `:4667`, and off at 11:12:42.509, `:4819`; the old arrow log does not include the rejection reason. Its disappearance cannot be assigned to a particular gate from this event alone.
- The main later route is accepted at 11:13:36.005, `:7003`. At 11:18:38.451, `logcat-003.txt:4045`, GPS publication is explicitly rejected with `CORRECTION_AWAITING_EVIDENCE`: the retained transform no longer has sufficient support. Rejections continue through ground-rebase attempts at 11:20:20.082, `logcat-004.txt:985`.
- A qualified full-readiness sample returns at 11:20:36.031, `logcat-004.txt:1777`. Thus the recorded state is intermittent, rather than a missing route for the entire capture. Its adjacent renderer snapshot still reports the root disabled; snapshots can straddle a UI/render transition and do not establish actual visible pixels.
- A new return trip is accepted at 11:23:01.492, `logcat-004.txt:4132`. At 11:23:04.338, `:4401`, a five-report hazard snapshot excludes the selected destination. At 11:23:05.106, `:4467`, placement is withheld with the explicit destination-hazard message. Independent heading contradiction is also logged at 11:23:05.093, `:4461`. That final blocked interval is not an OSRM parsing failure or evidence that hazard protection should be removed.

### A54, Android 14, capture 110902

- A* accepts the 267.0 m street route at 11:13:46.656, `logcat-001.txt:4137`. Qualified mapped placement appears at 11:13:48.865, `:4326`.
- At 11:16:25.932, `logcat-002.txt:2449`, registration innovation is 9.508 m. At 11:16:30.834, `:2676`, it is 19.866 m. The acquisition-paired GPS and camera positions are recorded immediately afterward at `:2452` and `:2679`. Nominal GPS accuracy alone does not establish agreement with the retained camera-world reference.
- At 11:16:58.583, `logcat-002.txt:4187`, a bounded last-verified-path sample is still eligible. At 11:17:04.929, `:4553`, new publication is rejected for unsupported registration; GPS, PDR, and ground-rebase attempts remain rejected through 11:21:14.378, `logcat-004.txt:1406`. The geographic route remains available during this display recovery failure.
- A new reverse-direction route is accepted at 11:22:45.313, `logcat-004.txt:6063`. The later replacements are triggered by three off-route GPS confirmations, not phone rotation: 11:23:35.897/39.900/44.930 at `:8298/:8504/:8729`, followed by replacement at 11:23:45.000, `:8747`. A second replacement occurs at 11:24:22.294, `logcat-005.txt:1725`. The tester's straight-walk account means these fixes do not independently prove a real physical deviation; no surveyed position is available to adjudicate GPS versus physical location.
- At 11:24:51.871, `logcat-005.txt:3142`, independent orientation removes heading trust. A tracking-live, heading-untrusted recovery sample remains at 11:25:32.117, `:5398`. Correct geographic routing does not by itself restore an Earth-to-camera bearing.

### A15, Android 16, capture 111035

- An initial 983.5 m MLD route is accepted at 11:12:10.773, `logcat-001.txt:2798`; later trips use the same short street corridor as the A54 captures.
- Qualified mapped placement is recorded at 11:12:19.605, `:3079`. A tracking interruption invalidates its heading/frame relationship: placement is withheld at 11:12:27.276, `:3510`. Healthy retained-heading support returns at 11:12:30.051, `:3753`.
- Registration remains pending at 11:12:33.147, `:3853`, then requalifies at 11:12:39.400, `:3980`. This is a successful control at the faster acquisition cadence. The last qualified mapped-placement sample is 11:13:05.665, `:4643`; its renderer snapshot has enabled geometry but no clipped centerlines inside the viewport. Without footage, camera pointing and physical offset cannot be separated.
- Later heading observations repeatedly lack sufficient movement or disagree. At 11:16:44.753, `logcat-002.txt:3362`, one candidate is collected. At 11:16:53.375, `:3801`, GPS displacement reverses against tracked movement; at 11:17:02.492, `:4470`, GPS displacement is 38.5 m versus 8.8 m tracked movement. These contradictions cannot safely be solved by dropping course/motion checks.
- A healthy retained-heading transition occurs at 11:17:59.717, `logcat-002.txt:7502`; sustained independent contradiction removes trust again at 11:18:24.025, `:8814`. The last direction-check sample is 11:21:03.499, `logcat-003.txt:5582`. There are 91 sampled direction-check states; this is not an uptime percentage.

Across the captures, no heading revalidation correction is applied. Candidate collection never reaches the required consistent evidence. Duplicate processing produces 23, 23, and 76 logged duplicate/older heading decisions respectively. The new implementation removes that duplicate call path; it does not turn contradictory measurements into valid heading evidence.

## Dataset and complete pipeline validation

The pipeline remains: MAUI location acquisition → confidence/innovation checks → route matching and GPS/PDR progress → provider through `IRoutingService` → retained geographic route → acquisition-paired geographic/world registration → local clipped window → confidence/ownership checks → ribbon and chevrons. A separate floor access arrow and a screen direction icon are different presentation paths. `NAV_RENDER arrows` measures route-chevron wings, not the separate floor arrow.

The current embedded resources reproduce the captured dataset identity:
`regional-af1bc3b60b33622566df13e6469d25b4e1cd6797951fe2ff7777c9312f14b162`.
The real bootstrap graph contains 54,598 nodes and 120,374 directed edges. The supplied Marikina PBF contains 604,845 nodes and 137,784 ways, with zero missing way-node references. C. Benitez Street is residential way `4989954`, without a pedestrian prohibition in its supplied tags.

Absolute coordinates are privacy-redacted in the logs. Offline replay coordinates were reconstructed by a translation-only fit of four recorded interior vertices to that supplied way; no rotation, scaling, invented GPS offset, or inferred physical survey was applied. Fit residuals are below 3.5 cm for MLD and below 1 mm for A*. The reconstructed inputs are analysis fixtures, not original absolute GPS observations.

- Thirteen short captured routes match the local pedestrian route within 6.4 cm in the actual routing replay. The direct supplied-line comparison is below 7 cm.
- The initial longer A15 MLD trip differs from A* by 21.919 m at one branch. Sampling every returned segment against the actual walkable graph gives maximum error 7.3 cm. The initial strict identical-route comparison therefore failed; graph inspection establishes a provider-choice difference on supplied mapped edges, not a demonstrated building-cutting shortcut.
- All fourteen captured source polylines were checked against the supplied PBF's 120,325 building polygons. None intersect those supplied footprints. Their completeness and physical accuracy remain unsurveyed.
- The commonly selected destination snaps about 22.02 m from the requested pin to the street. The initial longer trip has a 38.90 m destination snap. These are endpoint-access uncertainties. There is no independently validated entrance or traversable connector permitting an extension across that gap.
- Ten MLD fixtures replay the recorded geometry and snapped endpoints through the real `MLDRoutingService` parser using a local HTTP response handler. They preserve point ordering and explicitly unreported server identity. These are reconstructed response fixtures, not original captured HTTP payloads or an execution of the deployed OSRM server.

The repository config selects its primary Railway endpoint and defines OSRM v5.27.1 with `evacuation-foot.lua`, partition/customize, and `osrm-routed --algorithm mld`. The legacy regional Docker path explicitly cannot establish a shared app/server dataset identity. Captured MLD provenance remains `unreported`; the exact deployed merged PBF, profile, graph, and server revision cannot be established. Docker/OSRM executables are unavailable locally, so a complete local OSRM server reproduction was not performed. No remote configuration or deployment was changed.

Plot: [captured-route-comparison.svg](artifacts/field-review-20261026-1110/captured-route-comparison.svg). Numeric evidence: `route-audit.txt`, `routing-and-mld-replay.log`, and `route-replay-inputs.json` in that directory.

## Why previous changes did not settle the problem

The October changes corrected demonstrated transform reseeding, retained-yaw recovery dependencies, cue ownership, hazard messaging, and repeated work. They preserved mapped geometry; they did not independently establish the map-to-physical-world translation or bearing. This capture still contains GPS/camera contradictions and direction observations that fail existing validation. Holding geometry under those conditions is intentional; bypassing that decision could reproduce misleading lines through buildings.

Two additional code defects were reproduced locally before editing application code:

1. `RouteDisplayProgressPolicy.ObserveRegistration` used the eight-second PDR display budget to expire a different process: three-fix qualification of a new tracking frame. At a five-second acquisition interval, the third observation arrives ten seconds after the first and restarts qualification indefinitely. The baseline fixture failed. The A15 capture demonstrates that the same code can recover at its faster cadence; the logs do not prove this cadence defect caused every withheld interval on the other phones.
2. `TryShowRoadApproachCue` entered its registered-target branch only if a route and qualified motion were already present. Otherwise a `MAPPED_TARGET` cue could fall through to raw GPS direction. The bridge accepted such a cue without a world target; the baseline fixture failed. Its direction was also stored between publications while the arrow origin followed the camera. This is a confirmed source/contract defect, but its incidence in these captures cannot be measured because the previous floor-arrow log reported only visibility.

Earlier fixtures did not cover this acquisition cadence or the missing-world-target contract. The latest prior candidate's new fixtures had also not been executed under the then-applicable build-only restriction. The present request explicitly authorizes their execution. Neither their earlier existence nor today's passing checks establishes phone acceptance.

## Implemented changes

The ordered plan was presented before application edits, then implemented without a further approval round.

1. **Frame recovery:** `RouteDisplayProgressPolicy` receives the existing immutable `NavigationStabilityOptions`. Frame qualification uses its existing recovery lifetime, default 30 seconds, instead of the unrelated eight-second display lifetime. Each contributing fix must still be fresh, acquisition-paired, coherent, spaced, and owned by the same frame; three observations remain required. The ordinary eight-second/eight-metre display bridge and precision rules for translating a known frame are unchanged. The lifetime is justified by the already configured bounded recovery interval and the reproduced ten-second qualification span, not an invented physical accuracy claim.
2. **Mapped arrows:** `ARRoadApproachBridge` now requires a finite retained world target for `MAPPED_TARGET`. `CameraPage.RoadApproach` fails closed when registration/motion is unavailable. Each draw resolves direction from the current camera to that target; reaching or invalidating the target withholds the arrow. Initial access estimates keep their separately labelled contract. No route is manufactured and no supplemental cue erases a qualified ribbon.
3. **Heading processing:** `CameraPage.xaml.cs` processes a recovery fix once and retains any publication result from that first processing. The independent recovery observer remains reachable before the ordinary GPS innovation gate. Existing movement/course/heading thresholds remain authoritative.
4. **Diagnostics:** registration decisions include candidate count, support/candidate age, seed precision, and the configured recovery lifetime. Floor-arrow transitions include the rejection reason and frame ownership; `NAV_CUE` separately identifies the floor-arrow renderer. This resolves an observed evidence gap without treating enabled entities as proof of visible pixels.
5. **Regression cases:** `ARMultiDeviceRecoveryChecks` exercises two-, five-, and eight-second cadences; duplicate/expired qualification evidence; missing world targets; camera-relative target direction; invalid/reached targets; and existing ground/age expiry. It is included in the targeted alignment runner and available via `--multi-device-recovery`.

No datasets, coordinates, access rules, destination selection, hazard protections, engine/native dependencies, or cloud/dashboard behavior were changed.

## Phone/environment assessment

Recorded ARCore sessions initialize on all three devices. Location permissions are granted and usable frames/geometry are recorded. No application-level unsupported-device failure establishes a compatibility cause. Certification, sensor calibration, driver versions, and OS background restrictions were not independently verified.

| Device/session | Recorded thermal evidence | Interpretation |
| --- | --- | --- |
| A54 Android 16 / 110844 | 37.0°C initially; about 41.1°C; Warm target 20 FPS; pipeline roughly 19 FPS | Adaptive workload reduction is confirmed. |
| A54 Android 14 / 110902 | 36.4°C initially; Hot at 43.0°C at 11:22:50.515 (`logcat-004.txt:6400`); later 44.2°C, roughly 14.8–14.9 FPS | Significant heating and a 15 FPS application limit are confirmed. Registration rejection began earlier at 11:17:04.929. |
| A15 Android 16 / 111035 | Already 40.0°C at AR startup; later 42.5°C, Warm target 20 FPS and roughly 19–20 FPS | No cool-start comparison exists. |

Power saver is reported false; Android thermal severity is unavailable. Battery temperature is a proxy, not CPU/GPU temperature. CPU/GPU utilization, discharge slope, provider/course accuracy, magnetic accuracy, and environmental obstruction were not measured. UID-filtered logcat cannot exclude global memory pressure or every system restriction. Sampled route depth-hidden and prefix-hidden counts are zero: the capture does not implicate depth occlusion as the cause of the recorded withholding states. Heat can affect responsiveness, but it is not established as the cause of all interruptions. No new battery/thermal improvement is claimed.

## Verification and limitations

Commands were run from the repository root. Raw logs are under `artifacts/field-review-20261026-1110/`.

| Check | Command / method | Observed result |
| --- | --- | --- |
| Initial baseline runner | `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj --no-restore -p:GenerateEvergineContent=False -- --alignment-recovery` | Evergine add-on resolver crashed before tests. One attempt supplying `-p:EvergineProjectFilePath=D:\Projects\RescuAR\RescuAR.weproj` also failed. Logs: `baseline-regression*.log`. |
| Source-policy baseline | `dotnet run --project artifacts/field-review-20261026-1110/replay/replay.csproj --no-restore -- --policy-only` | Both reproduced defect assertions failed before application edits. Log: `policy-baseline-compiled.log`. Initial helper setup hit NuGet access and reference-path errors; approved SDK access and the corrected local path allowed compilation. No dependencies were installed. |
| Focused source fixtures | Same replay project with `-- --regression` | 160 assertions passed after the changes, including retained registration/stability fixtures. |
| Offline routing/parser replay | `dotnet run --project artifacts/field-review-20261026-1110/replay/replay.csproj --no-restore` | 27 checks passed: 14 captured network comparisons, 10 MLD parser replays, dataset identity, and the two original reproductions. |
| First Android build | `dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False -p:RescuArDiagnosticBuild=true -p:AndroidPackageFormats=apk` | Failed: three missing `EvergineContent` generated-constant references. |
| Single corrective Android rebuild | `dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:RescuArDiagnosticBuild=true -p:AndroidPackageFormats=apk` | **Passed: zero warnings/errors, 93.59 seconds.** Omitting the global content-generation override let the shared project's existing setting regenerate its constants. Compiled scene generation remained enabled; no project/dependency settings were edited. |
| Targeted application regressions | `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -c Release --no-restore -p:GenerateEvergineContent=False -p:BuildProjectReferences=False -- --alignment-recovery` | **655 checks passed**, compiling the current fixtures against the shared library produced by the successful Android build. Project references were not rebuilt, avoiding the baseline resolver path. |
| Readiness | `./build/Verify-ARCoreReleaseReadiness.ps1` | Manifest/compatibility declarations internally consistent. |
| Patch hygiene | `git diff --check -- RescuAR RescuAR.MAUI tests/RescuAR.RegressionChecks` | Passed. |

The isolated replay compiles current changed policies against the existing product model assembly; the Android build and subsequent 655-check runner provide the corresponding application/shared-library compilation. Reconstructed MLD responses cannot validate a deployed server's dataset. No full unrelated suite, interactive application/browser check, installation, integration/E2E suite, or physical device test was performed. Static native hash inspection is not runtime acceptance.

## Delivered candidate

[RescuAR-20261026-Recovery.apk](artifacts/field-test-20261026-recovery/RescuAR-20261026-Recovery.apk)

- Size: 60,813,879 bytes.
- SHA-256: `9a612967eb563c212b6e04517d36e21f5561e3dd7764a8a41d89afdb67ca93e0`.
- Packaged profile: Release; diagnostics true; visibility override false; base `443f531e491d023a01c012db03e56ddc666d2f66` **plus seven uncommitted product-source changes**.
- `source-manifest.json` beside the APK records those source hashes and package identity. Packaged `libnative_bridge.so` still matches `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- No commit or push was created. Scoped source/test status was clean before this work. Full repository cleanliness remains unconfirmed because of the existing Git LFS access failure.

## Required multi-device retest

1. Install this exact candidate on each phone and record the installed hash/build profile. Use the same marked street endpoints and selected center for fresh A* and MLD trips. Start logs and record phone-local event times; include an independent event marker if filming. Physical reference uncertainty is needed to assess building/road alignment.
2. Walk straight at an ordinary pace, stop, move sideways, and rotate the phone without walking. Expect stable mapped world points while qualified; the mapped arrow must point from the current camera toward its retained target. A phone rotation must not cause a replan. Do not equate an enabled renderer or a visible access arrow with correct ribbon alignment.
3. Briefly pause/resume the camera and repeat a tracking interruption/floor renewal. At five-to-eight-second fix intervals, expect three fresh coherent observations to complete `FRAME_REQUALIFICATION_PENDING` → `FRAME_REQUALIFIED` within the configured 30-second evidence lifetime. Two, duplicate, stale, or conflicting observations must not qualify it. If heading evidence is contradictory, retain the explicit map/recovery state instead of forcing forward geometry.
4. Repeat the covered frontage, recording the 8/15/30-second positioning states and the return to open sky. Capture `NAV_HEADING_TRUST`, `HEADING REVALIDATION`, `NAV_REGISTRATION_DECISION`, `NAV_PUBLICATION_REJECTED`, `NAV_CUE`, `NAV_RENDER`, and actual observed visibility. Report the phone and event times even if footage is unavailable.
5. Recheck the selected destination's near/arrival notice, separate manual confirmation, an avoidable hazard, and an excluded destination. Expect preserved selection, conservative arrival, and no unsafe forward cue. Do not create or modify live reports for testing.
6. Compare matched 15-minute runs with the same brightness, initial charge/temperature, route, and surroundings. Record battery start/end, temperature, FPS, tracking losses, and cue gaps. There is no accepted power or thermal improvement until those measurements exist.

Return actual outcomes before further behavior-dependent changes. Remaining material uncertainties are absolute road/entrance registration, heading source accuracy, exact physical visibility, deployed MLD identity, and measured post-change recovery/thermal behavior.
