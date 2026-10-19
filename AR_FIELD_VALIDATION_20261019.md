# Field validation corrections — 19 October 2026

The user's MLD, A*, arrival, flood, and logging findings were read before the
captures. The changes below are local and uncommitted. Ground acquisition fixes
from the preceding review are retained. No routing datasets, dependency pins,
native bridge binaries, or hosted services were changed.

## Supplied evidence

All three installed-artifact manifests identify the same APK SHA-256:
`583bdeefe9d0f49f56d488244ec99acdb59313c8c8a4c5969cb9a4aee4983c6b`.
That hash matches the APK supplied after the preceding ground/flood review.
Its embedded revision is `9a79006b279e2e1ed2551d8cabc87c20c5c98c55`, but the
artifact also contains that review's uncommitted changes; the revision alone
does not identify its complete source state.

| Capture | Evidence quality | Observed behavior |
|---|---|---|
| `091153`, HONOR ALI-NX1 / API 35 | Incomplete. Logger was dead at collection, no completion marker, about 27 seconds and one application event. | No navigation or Depth conclusion can be drawn for this phone. |
| `091241`, Samsung SM-A546E / API 34 | Complete; 09:12:44–09:36:39, 6.42 MiB, 7,928 application events, 682 navigation events. | MLD followed by A*. Route visibility alternated with position confidence; Depth was disabled. |
| `091351`, Samsung SM-A546E / API 36 | Complete; 09:13:54–09:35:06, 4.04 MiB, 3,634 application events, 387 navigation events. | MLD route hidden in all 20 periodic destination-active snapshots; repeated identity suspension; near-destination arrival rejected; Depth disabled. |

The ZIP is preserved in `Logs/`. Review copies are under the ignored
`artifacts/field-review-20261019/logs/` directory. References below use the
capture folder and original line numbering.

## Findings and corrections

### MLD/A* guidance and progress

In `091351/logcat-001.txt:3700`, a fix only 1.3 m from the route was classified
Low because its reported accuracy was 43.1 m, despite a 144.2-point independent
corridor score gap. Subsequent poor-precision fixes repeatedly erased good
route-identity observations. Low precision and competing route branches were
being treated as the same failure. Identity suspension also rejected PDR steps.

`GpsPdrFusionPolicy` now retains recent identity evidence across a weak fix only
when it still matches the same unambiguous corridor. Weak fixes do not advance
progress or count as confirmation. Evidence expires after ten seconds, distinct
observation timestamps are required, and actual off-route/branch ambiguity
continues to suspend identity. CameraPage supplies the provider timestamp.

`RouteProgressTracker` also treats densely divided, connected straight segments
as one corridor. It continues to distinguish parallel/reversing branches. This
is a separate source correction, not proof that dense vertices caused every
observed Low match.

A screen arrow and destination distance can now remain available while the
floor route is hidden, using fresh GPS, a tracked camera pose, and the current
session's stable heading. It is labelled as destination direction and does not
create a walking connector or bypass the cyan route's independent confidence
checks.

### Road registration and heading

The screenshots show a geographic overlay displaced or rotated relative to the
street. A* calculates from geographic coordinates; it does not use projected
camera lines as its routing input. These images therefore do not establish
that the GeoJSON coordinates themselves are skewed.

The captures retain fixed initial yaw estimates of approximately -114.0 and
-81.8 degrees. Revalidation repeatedly rejected short noisy movements or GPS
course disagreement (`091241/logcat-001.txt:5741`, for example). The source could
accept a placeholder course of zero merely because displacement-derived speed
was high, even when provider speed did not support a course reading.

Heading revalidation now checks provider speed before using its course and
requires a displacement baseline scaled to GPS uncertainty, bounded to 8–20 m.
Three consistent movement windows still precede a bounded correction. No
permanent rotation or offset was applied to the map data.

The complete captures contain no Vulkan device-loss or camera fence-timeout
event. Later camera telemetry reports roughly 20 FPS under the existing Warm
thermal limit. These facts do not prove the reported visual stutter is resolved.
Street registration, continuity, and distance stability require another walk.

### Arrival

`091351/logcat-002.txt:1659` rejects a fix at 22.5 m with 20.5 m accuracy;
line 1736 rejects 23.4 m with 10.5 m accuracy because the entire uncertainty
circle does not fit inside the old 30 m radius.

Automatic confirmation now uses two distinct fresh observations over at least
three seconds, accepts accuracy up to 25 m, and tests the fix position against
the facility radius, capped at 40 m. The default facility radius remains 30 m.
Stale fixes, changed trips, poor precision, mapped major-road barriers, and
positions beyond the bounded radius remain excluded. Arrival confirms vicinity;
facility entry is still unverified.

A prominent top card now says “You're near [destination]” with the approximate
distance within the existing 75 m manual-confirmation range. It appears before
automatic confirmation and explains the entrance/confirmation action instead
of presenting only a button.

### Flood visualization

Both Samsung captures log supported Depth with `FORCED_DEPTH_OFF` and
`requested=DISABLED` (`091241/logcat-001.txt:1671`,
`091351/logcat-001.txt:2414`). The source used that generic experiment label
for a device-profile quarantine. Subsequent image requests produced
`IllegalStateException`; accepted depth-frame counters stayed zero.

The diagnostic field candidate now requests automatic Depth on supported
profiles, including these Samsung profiles, and records
`DEPTH_FIELD_REVALIDATION`. An explicit Depth-off experiment is still respected.
Normal builds retain the existing native-crash profile quarantine until this
retest is accepted. Configuration logs now distinguish quarantine, explicit
flags, and the field revalidation mode.

The real scene-depth water compositor is retained. The wire-grid substitute is
removed. Until coherent depth arrives, the UI states that it is preparing the
water preview; disabled Depth is identified explicitly. Disabled sessions no
longer attempt image acquisition. Fresh depth, ground identity, generation,
tracking, metric height, and object masking requirements remain enforced.

The captures also contain repeated native `spherical_rectifier` errors even
with Depth disabled. Enabling Depth and compiling the compositor do not prove
that the installed native provider will produce a usable stream or avoid its
previous native abort. That is a material phone-validation requirement.

Google's [DepthMode reference](https://developers.google.com/ar/reference/java/com/google/ar/core/Config.DepthMode)
confirms that image acquisition with Depth disabled throws
`IllegalStateException`. Its [Depth guide](https://developers.google.com/ar/develop/java/depth/developer-guide)
documents checking support, enabling automatic Depth, and using scene depth for
occlusion.

### Logging

Application logging was not removed from the two complete captures. The HONOR
capture is genuinely incomplete and cannot be reconstructed from this ZIP.
The smaller archive is not evidence that navigation ran without emitting logs.

Diagnostic status cadence changes from 30 seconds to five seconds. New compact
`NAV_PLACEMENT` and `NAV_PROGRESS` records capture visibility reasons, route and
ground identity, GPS age/accuracy, heading, progress, fusion, and arrival state.
Heading decisions now include collecting/unavailable states and provider speed.
Road diagnostic placement includes the applied yaw and calibration time.
`ARCORE_DEPTH_CONFIGURATION` and `FLOOD_COMPOSITION` identify Depth enablement,
waiting states, ground matching, and actual water composition. Coordinate
consent/redaction behavior is retained.

## Verification and required retest

- Focused regression command: `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-validation`.
- Result: 43 checks passed, covering the retained ground fix, arrival, dense and
  parallel road matching, precision-only identity holds, duplicate/expired GPS
  evidence, heading/course handling, destination bearings, and Depth policy.
- Android Release build passed with zero warnings and zero errors.
- `build/Verify-ARCoreReleaseReadiness.ps1` passed. APK checks passed for the
  merged manifest, ARM64 native bridge, and diagnostic build profile. The native
  bridge hash matches the supplied capture; the bridge was not rebuilt.
- No integration, automated browser, or physical-device testing was performed.

Candidate: [RescuAR-20261019-FieldValidation.apk](artifacts/field-test-20261019/RescuAR-20261019-FieldValidation.apk).
SHA-256: `901de92a9519d0d3eb29ee79c715a90a40d54f4cd887759084da72e9f57dbb34`.
Package evidence is in `artifacts/field-test-20261019/package-evidence/`.

Retest both routing algorithms on the same road. Check the destination-direction
cue during brief cyan-route loss, correction after sustained straight walking,
and distance continuity. Near the entrance, expect the explicit proximity card
and faster automatic vicinity confirmation. For flood mode, expect scene-masked
water at the selected height rather than grid lines. Collect a complete capture
for each phone, especially a fresh HONOR capture, before further
behavior-dependent changes.

## Follow-up: 10:24 field capture

The later MLD, A*, and faint-water findings were read before opening
`Logs/RescuAR_FieldTest_20261019_102425.zip`. This section records the subsequent
review and local changes; the preceding section describes the earlier capture.

### Capture evidence

All three installed manifests identify APK SHA-256
`e2396a692c05c5d2975160121a0cdd80ae487812e25792ed727562a0a47acbe9`,
embedded source revision `29a3eecc4f5d7d84f2b34040c3309b2aa6cc7915`, and a
diagnostic Release profile. Review copies and a compact count summary are under
the ignored `artifacts/field-review-20261019-1024/` directory.

| Capture | Coverage | Evidence |
|---|---|---|
| `102329`, Samsung SM-A546E / API 36 | Complete, 10:23:34–10:45:42; 3,427 application / 246 navigation events | MLD: one route request, zero dynamic reroutes, 52 AR alignment publications. Of 101 current-location samples, 24 reported accuracy worse than 30 m; median 15.5 m, worst 49.38 m. |
| `102500`, Samsung SM-A546E / API 34 | Complete, 10:25:04–10:45:04; 7,399 application / 447 navigation events | A*: zero dynamic reroutes, 71 AR alignment publications. Of 173 current-location samples, 29 reported accuracy worse than 30 m; median 10 m, worst 100 m. |
| `102425`, HONOR ALI-NX1 / API 35 | Incomplete, about 13 seconds; zero application / navigation events | Logger dead at collection, start marker present, end marker absent. This capture cannot diagnose the reported intermittent guidance. |

The MLD request is at `102329/logcat-001.txt:2448`. Repeated rebuilding of the
AR window is separate from requesting a geographic route. GPS uncertainty,
tracking recovery, route-position confidence, and briefly pending geometry
appear in visibility records. For example, `102329/logcat-001.txt:3941` records
geometry preparing despite fresh GPS and trusted heading. Neither complete
capture contains an `AR_ROUTE_GEOMETRY_STALLED` event. These observations
explain possible visual interruptions, but do not establish a GPS hardware
fault or reproduce the uncaptured phone's failure. Location redaction also
prevents reconstructing its actual street trajectory from this archive.

The A* off-route candidate at `102500/logcat-004.txt:4708` reaches only 1/3
confirmations. There is no recorded dynamic replacement. The source checks
distance away from the route corridor; walking backward along the same route
can therefore preserve stale committed progress without requesting a replan.
The source gap exists for both algorithms. The user's physical reverse-walking
test covered A* only; MLD reverse behavior remains untested on a phone.

### Corrections

`RouteBacktrackingPolicy` now compares raw matched position and GPS displacement
independently of forward-only GPS/PDR progress. Replanning requires accuracy
at most 20 m, an unambiguous accepted match, displacement and reverse progress
of at least the larger of 12 m or the combined GPS uncertainties, at least six
seconds of walking, and two continued-movement observations at least two seconds
apart. A stationary position after one GPS jump cannot complete confirmation.
It also handles walking away from a route's start, where projection clamps
progress to zero. Stale/duplicate observations, poor accuracy, route changes,
arrival proximity, and cooldowns guard the new evidence.

The backward match search now covers at least 60 m instead of relying solely
on four preceding vertices. Dense GeoJSON vertices can no longer limit the
search to just a few metres. A confirmed reverse walk requests a route from
the current GPS position. An active A* route uses offline A* for this request;
MLD uses the existing connectivity-aware router. Destination version checks,
mapped access validation, replacement-distance confirmation, and independent
AR placement checks still apply. A new route may correctly point back toward
the same destination; recalculation does not promise an alternative street.

`ROUTE_BACKTRACKING` records now distinguish reverse-walking confirmation from
ordinary corridor departure. Periodic `NAV_PROGRESS` includes confirmation,
retreat distance, and the reason a reverse replan is held. Dynamic request logs
identify the preceding algorithm and whether its selection is being preserved.

The screenshots show estimated-floor mode, previously capped at 56/255 opacity
(22%). Estimated-floor water now reaches 120/255 (47%), and verified-floor
water reaches 160/255 (63%), using a deeper blue (`#0084B8`). Scene depth,
metric water height, surface feathering, transparent invalid-depth pixels,
ground validation, and the estimated-floor caption remain in place. The
screenshots at 10:49–10:50 fall after the complete captures ended, so the archive
cannot establish the mask coverage of those exact frames. Physical visibility
and intermittently missing guidance still require device acceptance.

### Local verification and next walk

- `--field-validation`: **67 checks passed**, including 24 new reverse-walking
  checks. A test fixture initially retained prior branch identity instead of
  representing an ambiguous match; correcting that fixture and rerunning passed.
- Android Release build: **0 warnings, 0 errors**.
- Release readiness and APK manifest / ARM64 native-bridge / diagnostic-profile
  checks: **passed**. The native bridge remains
  `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- No full regression suite, integration tests, UI automation, installation, or
  physical-device testing was performed for this follow-up.

Candidate: [RescuAR-20261019-RoutePosition.apk](artifacts/field-test-20261019-route-position/RescuAR-20261019-RoutePosition.apk).
SHA-256: `884945e9a93d063e71a96b587b4933d7aa422922f6a5232ecd57b8eab6dbb2e6`.
Package evidence: `artifacts/field-test-20261019-route-position/package-evidence/`.
The later `111542` capture identifies this candidate's embedded base revision as
`dbc60efb35164f057ba575de469dec0f22b2c745`; that revision alone does not identify
the uncommitted follow-up changes. This candidate is superseded by the startup
correction below.

1. Record a complete capture per phone. Walk normally, turn only the phone,
   then walk backward along the same street far enough to exceed GPS uncertainty
   (usually 15–40 m with a usable fix). Expect one confirmed current-position
   replan and refreshed remaining distance, without requests on every GPS poll.
2. Repeat separately in A* and MLD. During cyan/arrow loss, retain the exact
   screen message and time so visibility reasons can be matched to the capture.
3. Compare flood mode at 0.6 m and 1.77 m, including estimated-floor mode.
   Expect more visible blue water below the simulated height, with valid
   above-water geometry clear. Report the actual results and complete captures
   before further behavior-dependent changes.

## Follow-up: 11:15 black camera capture

`Logs/RescuAR_FieldTest_20261019_111542/` is a complete Samsung SM-A546E / API 34
capture spanning 11:15:46–11:25:41, with 191 application events and seven
navigation events. Its installed APK SHA-256 is exactly the preceding
`884945e9a93d063e71a96b587b4933d7aa422922f6a5232ecd57b8eab6dbb2e6`
candidate. The native bridge hash is unchanged.

### Confirmed startup failure

The camera surface appears at `logcat-001.txt:919`. At lines 978–985, the first
Evergine update throws `NullReferenceException` in
`ScreenContextManager.InitializeASingleScene`. The pinned framework's method
dereferences the supplied scene before initialization; `MyApplication` had
queued a null scene. Rendering is disabled by the draw-error handler, which
awaits native surface recreation. Merely revisiting the retained Camera tab
does not recreate that failed engine state.

At lines 991–994, ARCore startup consequently reports `GraphicsUnavailable`
with session generation zero. Reopening Camera fails the same way at lines
1230–1234. Camera permission is granted, and accepted camera poses remain zero
through the last coherence record at line 1360. No routing or flood behavior
can be accepted from this run.

The delivered APK was built with `GenerateEvergineScenesCode=False`. Evergine's
exporter separately excludes compiled scenes from assets. Inspection confirmed
that the scene asset was absent from the APK and that the pre-correction
Release assembly lacked both `RescuAR.CompiledMyScene` and the application's
generated factory registration. Disabling generation therefore removed both
ways of loading the startup scene. This was an error in the supplied build,
and the earlier manifest/native-package checks did not cover it.

### Correction and verification

- The shared project now rejects disabled scene generation before compilation.
  Regression builds also keep scene generation enabled.
- `MyApplication` directly references the generated factory, logs scene loading
  and queueing, and rejects a null startup scene with an explicit explanation.
- Three focused checks verify the factory's scene identifier, compiled asset
  path, and application registration override. All passed. A negative build
  check also confirmed that `GenerateEvergineScenesCode=false` is rejected.
- Android Release build passed with zero warnings and zero errors, using
  `dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False`.
  Do not disable `GenerateEvergineScenesCode` in application or regression builds.
- Release readiness, manifest, ARM64 bridge, and diagnostic-profile checks
  passed. Additional inspection extracted `RescuAR.dll` from the exact new APK
  assembly-store payload and verified that `MyApplication` constructs and
  registers `CompiledMyScene` for scene
  `bef99f5b-28b9-4d53-98a1-36b4a18fdc05`.
- No device installation, manual application interaction, integration tests,
  or full regression-suite run was performed.

Corrected candidate: [RescuAR-20261019-CameraStartup.apk](artifacts/field-test-20261019-camera-startup/RescuAR-20261019-CameraStartup.apk).
SHA-256: `41cd0e77dbda4b3ad7dd71ba2809b905c6e574f00efdd19e501b877ca259c58f`.
The embedded revision is `81b25794295977999e091e9469c6ff9b16de1bc9`, with the
local startup corrections still uncommitted. Evidence is under
`artifacts/field-test-20261019-camera-startup/package-evidence/`, including
`startup-scene-test-report.json` and the extracted assembly hash.

Install this candidate, fully close and relaunch the app, then open Camera.
Expect the live camera image to appear. Leave and reopen Camera once; expect
the image to resume. Return the actual result and a complete capture before
resuming the routing and flood acceptance walk. Builds and packaged-code
inspection cannot establish physical camera behavior.

## Follow-up: 11:40 guidance consistency capture

The user's latest findings were read before opening
`Logs/RescuAR_FieldTest_20261019_114043.zip`. All three installed manifests
identify the corrected camera-startup APK
`41cd0e77dbda4b3ad7dd71ba2809b905c6e574f00efdd19e501b877ca259c58f`.

| Capture | Evidence |
|---|---|
| `113912`, Samsung SM-A546E / API 36 | Complete, 11:39:15–11:57:14; 3,648 application / 338 navigation events. Two initial MLD requests, no dynamic reroutes. GPS median reported accuracy 21.5 m; 43 of 124 current fixes worse than 30 m. Nine GPS-to-graph origin rejections. |
| `114043`, Samsung SM-A546E / API 34 | Complete, 11:40:46–11:57:43; 5,882 application / 288 navigation events. One initial MLD request, no dynamic reroutes. GPS median reported accuracy 7.5 m; 21 of 120 current fixes worse than 30 m. Four GPS-to-graph origin rejections; repeated motion/feature tracking losses and low-light states. |
| `114004`, HONOR ALI-NX1 / API 35 | Incomplete, about eleven seconds; zero application/navigation events. Logger dead at collection, completion marker absent. |

The user identified Android 16 as the A* test phone. Its complete capture
nevertheless reports `MLD (Railway OSRM)` throughout the active trip, and
`HybridRouting` records successful online selections. There is no recorded
offline A* selection in either complete capture. The API 34 phone loses
connectivity at `114043/logcat-001.txt:4218` and intentionally retains its
downloaded MLD route. Losing Internet during an existing trip does not by
itself request an A* replacement. These logs cannot establish a comparison of
two different route providers or a completed reverse-walking test in MLD.

### Reproduced guidance loss

At `113912/logcat-002.txt:781–787`, GPS matches the road with High confidence,
11 m reported accuracy, and only 5.4 m cross-track error. Its raw route position
is 37.5 m, but smoothed trip progress remains 58.8 m. The display uses that
smoothed coordinate as the road reference, fails the 20 m GPS-to-graph limit,
clears the bridge, and reports “Pedestrian route unavailable.” The geographic
route itself remains valid.

At `114043/logcat-002.txt:2961–2968`, the same sequence occurs with 7 m accuracy,
3 m cross-track error, raw position 88.8 m, and fused progress 127.2 m. The
origin rejection is therefore partly along-route smoothing error, rather than
distance away from the actual road. Those failures are reproducible without
weakening the physical placement limit.

### Shared correction

`RouteDisplayProgressPolicy` now uses the actual matched GPS route position
for the display window, road reference, and segment. Trip-distance smoothing
continues independently. Between fixes, step-based PDR advances from that
physical display anchor by its own increment, preserving the observed
GPS-to-road offset instead of centering the road on the camera every step.
The derived alignment reference is used only for AR projection; route requests,
arrival checks, and stored device locations continue to use actual GPS.

The same policy serves both MLD and A*. It also supplies heading/road-entry
segment references and recent display references for anchor/world recovery.
New trips and route replacements reset its evidence. Existing GPS precision,
route confidence, barriers, ground/tracking trust, the 20 m placement limit,
and PDR's eight-second/eight-metre bounds remain enforced. Physical movement
can refresh the display even while smoothed trip progress is being held.

Heading revalidation additionally rejects GPS/AR movement-distance disagreement
beyond reported uncertainty. For example, a 100 m GPS jump alongside 5 m of
tracked motion cannot supply a new road yaw merely because the bearings agree.
This addresses a pattern present in the capture's heading records without
applying a fixed rotation or shift to the road dataset.

The false “geometry stalled” record at `113912/logcat-003.txt:6704` occurs while
the camera session is paused, pose session is zero, GPS is about 110 seconds
old, and the renderer already reports `WithinCapacity`. Geometry preparation
timeouts now require fresh tracked frames. A retained geographic route with
missing AR placement is labelled as placement recovery. Periodic diagnostics
record raw GPS, smoothed trip, and displayed progress separately.

No GeoJSON coordinates, road connections, routing preference, or dependency
pins were modified. The evidence supports a display-reference offset; a genuine
map-data offset still needs a verified street reference. Location redaction
prevents surveying the dataset from this archive. Actual tracking loss and
prolonged GPS uncertainty can still hide the cyan route and direction arrow.

### Local verification and required walk

- **96 focused checks passed**, including 26 new display/projection checks for
  both algorithm labels, the reproduced offset failure, lateral placement,
  bounded PDR, duplicate fixes, trip resets, corner segment selection, and rejection
  of incoherent heading movement.
- Android Release build passed with **zero warnings and zero errors**. Release
  readiness, whitespace, manifest, ARM64 bridge, and diagnostic-profile checks
  passed. Inspection of the managed assembly extracted from the actual APK
  also confirmed startup-scene registration and the new shared display policy.
- No physical-device, integration, browser, or full regression-suite testing
  was performed for this follow-up.

Candidate: [RescuAR-20261019-Guidance.apk](artifacts/field-test-20261019-guidance/RescuAR-20261019-Guidance.apk).
SHA-256: `30d7bd8f51d4efa8c143a565b42c6d54183b00f1bc2d003d7c5a51dc0c8734b0`.
Its embedded source revision is `d48304d5e95523bfea19b6265571bc3187458769`, plus
the uncommitted changes described here. Package and managed-code evidence is
under `artifacts/field-test-20261019-guidance/package-evidence/`. The native
bridge remains `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.

Retest the same straight street and a corner. Expect the cyan route to survive
good GPS fixes that differ from smoothed walking progress, and expect step
updates to preserve the road's lateral placement. For an A* run, disable
Internet **before starting a new trip** and confirm an offline selection in the
capture; an existing MLD route is retained through connectivity loss. Test
opposite-direction walking separately in both providers, and return the actual
results and complete captures before further behavior-dependent adjustments.

## Shared A* / MLD guidance follow-up

The implementation follows the parity plan: use the same progress, reroute,
road-entry, visibility, recovery, and arrival rules for either provider, while
allowing their mapped paths and online/offline capabilities to differ.

### Provider selection and replacement

`HybridRoutingService.FindReplacementRouteAsync` now owns the selection used
for both ordinary deviations and sustained reverse walking. An active A* trip
keeps the offline provider when Internet returns. An active MLD trip uses the
normal connectivity-aware provider and retains its existing transport fallback.
Both providers now receive the same deferred, bounded road-access retry; the
earlier A* backtracking branch bypassed that retry. Hazard replacements still
use the common safety-first planner, with client-side hazard validation and
offline fallback. A hazard replacement can deliberately change providers.

Replacement calculations do not publish AR geometry. The existing destination
version, barrier, geometry, distance-increase, and atomic acceptance checks
remain in CameraPage. Network loss alone retains downloaded MLD guidance.

### Density-dependent match regression found by the new tests

The first parity run used the actual A* graph planner and the production MLD
HTTP adapter/parser with an in-memory OSRM protocol fixture. The fixture has
five-metre vertices, duplicate coordinates, a mapped corner, and a server
distance that differs from the geometry. It is representative test data, not
a newly captured device response; no remote server is contacted.

That run exposed a real matching discrepancy: during backward walking three
metres beside the road, continuity penalties could select a dense route vertex
ahead of the closest physical projection. The same road encoded as a long A*
segment projected correctly. This could stall reverse-walking confirmation.

Matching now selects the road corridor with the existing continuity score,
then selects the nearest physical projection within that same continuous
corridor. Independent-corridor scoring, parallel-road ambiguity checks,
monotonic trip progress, and reroute confirmation thresholds remain intact.
No GeoJSON coordinates, data identities, heading transforms, or snap limits
were changed. A geographic dataset offset still requires a reliable physical
reference; these changes do not establish that the source roads are surveyed.

### Visibility and recovery safeguards

- A valid mapped preview is retained when a fresh publication waits for tracking
  or an anchor. Visibility still requires the independent tracking, GPS,
  heading, access, and route-identity gates. Anchor handoff still discards an
  old preview before accepting geometry in the new frame.
- The brief verified-window GPS hold now explicitly rejects suspended route
  identity. Its immutable snapshot is atomically published across UI/GPS work.
  The existing six-second, 25-metre accuracy, 15-metre displacement, route
  version, and ground-reference limits remain enforced.
- World-correction and replacement-anchor publication now recheck the active
  trip and route under the progress lock, using the latest bounded physical
  display anchor. A superseded callback cannot republish its earlier route.
- Diagnostics include the visual kind, visual mode, preview/entry state,
  reroute state/result, trip version, and provider selection policy.

### Verification and phone acceptance

The scoped `--ar-guidance` runner includes the existing 96 field checks,
60 new provider/parity checks, and 57 lifecycle/frame/sensor/visibility audit
checks. The first run exposed the density issue above; after the focused
correction, **213 checks passed**. The new scope reuses the camera resume checks
and does not run the unrelated full regression harness.

Final verification repeated those 213 scoped checks after making the hold
snapshot atomic. Android Release completed with **zero warnings and zero
errors**. Source readiness, whitespace, merged manifest, ARM64 native bridge,
and diagnostic-profile package checks passed. The package check required an
explicit path to the installed native inspector and permission to launch that
inspector outside the process sandbox; it passed without application changes.

Inspection of both managed assemblies extracted from the actual APK confirmed
the generated startup-scene factory and registration, shared replacement
entry point, CameraPage's call to it, atomic hold state, and the physical
corridor-matching correction. The extracted payload matches the final build
store used by its assembly manifest.

Candidate: [RescuAR-20261019-Routing-Parity.apk](artifacts/field-test-20261019-routing-parity/RescuAR-20261019-Routing-Parity.apk).
SHA-256: `a0b90b53710802614db9e27f93aceb0cf8913475b7a06a7159126d66b9aae6af`.
The embedded source revision is `299ee0f5df7ac0d3350a5ba80bacc1ac3de68d78`, plus
the uncommitted guidance changes. Diagnostics are enabled and the route
visibility override is disabled. Test/build logs, package evidence, and managed
assembly checks are under `artifacts/field-test-20261019-routing-parity/`.
The native bridge remains `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.

Run `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj --no-restore -- --ar-guidance`
for this scope. Device acceptance remains separate from these local checks:

1. Start a new online trip and confirm `MLD (Railway OSRM)` in its capture.
   Walk forward, reverse direction long enough to exceed GPS uncertainty,
   and leave/rejoin the mapped road. Expect confirmed current-position replans,
   stable progress during GPS jitter, and an explicit recovery message when
   trustworthy AR placement is unavailable.
2. Stop that trip, disable Internet, and start a new trip. Confirm `AStar`
   before repeating the same route and maneuvers. Restore Internet during the
   A* trip; ordinary replans should keep A*. Disabling Internet during an
   existing MLD trip does not establish an A* test.
3. For each provider, briefly interrupt tracking, turn a corner, leave/reenter
   the camera, and change the destination during pending route work. Expect
   recovery with fresh placement, no old-trip geometry, no premature arrival,
   and a working camera. Recheck ground acquisition and visible flood Depth.

Return the actual results and complete captures before further changes that
depend on physical-device behavior. No device installation, UI automation,
integration tests, remote routing calls, or full-suite run were performed.

## 13:21/13:23 captures: live guidance, reverse walking, and alignment

The findings in the supplied task were read before the ZIP. This follow-up
uses `RescuAR_FieldTest_20261019_132301.zip`, containing two complete captures:

| Capture | Device / Android | Confirmed active provider | Navigation records in capture quality report |
| --- | --- | --- | --- |
| `132120` | Samsung SM-A546E / Android 16 (API 36) | AStar | 114 |
| `132301` | Samsung SM-A546E / Android 14 (API 34) | MLD (Railway OSRM) | 327 |

Both installed APKs match the preceding routing-parity candidate SHA-256
`a0b90b53710802614db9e27f93aceb0cf8913475b7a06a7159126d66b9aae6af`.
Both active routes have six points and a reported distance of 261.5 metres.
Both captures contain good and weak GPS observations, trusted guidance periods,
and depth occlusion events. Neither records a dynamic reroute or a confirmed
backtracking candidate. A* recorded no PDR steps; MLD recorded 133 detected,
87 accepted, and 46 rejected steps. Reverse detection therefore cannot depend
on step callbacks. The tester confirmed staying on the same street, but the
physical reverse-walking times are unknown; the logs cannot establish those
intervals or prove that every retreating GPS fix represents backward walking.

### Confirmed defects and remaining uncertainty

- Reverse evidence was reset on every weak GPS observation. A* recorded
  repeated raw retreats of roughly 20-24 metres that did not exceed the sum
  of the two fixes' uncertainty. Some retreats disagreed with tracked AR
  movement. These are not evidence for simply reducing the GPS distance gate.
- Heading revalidation required movement along the forward route tangent;
  correct same-street reverse movement was rejected. Qualified heading
  candidates were also erased by brief weak-fix interruptions. In MLD,
  `logcat-002.txt` records 1/3 at 13:35:02, 2/3 at 13:35:15, then an unavailable
  fix at 13:35:17. Later 1/3 candidates are similarly interrupted.
- Every new route-window publication marks geometry pending. CameraPage
  suppressed the entire route during this preparation even when its previous
  local geometry remained prepared and usable. PDR also depended on the first
  segment of the published visual instead of the current geographic segment.
- A prepared route is not necessarily visible. MLD reports trusted placement
  at 13:32:22 while the camera faces about 174 degrees away from the route.
  Both captures also record depth-hidden segments. Existing telemetry did not
  distinguish prepared, enabled, occluded, and camera-facing geometry.
- Initial projected and rendered route azimuths agree to 0 degrees in both
  captures. This supports the existing axis convention; it does not establish
  surveyed road placement. Interrupted heading correction can preserve an
  existing yaw error; its share of the reported rightward offset is unconfirmed.
  A systematic GeoJSON translation, GPS bias, or device-specific error also
  remains unconfirmed without a physical reference and synchronized movement
  observations. No blanket geographic or rightward offset correction is
  justified by these captures.

### Focused shared corrections

1. Reverse detection retains qualified evidence through weak fixes for at most
   15 seconds between reliable observations. Weak fixes add no evidence.
   Fresh live AR translation can corroborate at least 12 metres of backward
   movement with at least 3 metres of accurate GPS displacement/retreat. It
   requires the same session, ground reference, stable calibration, and street
   tangents within 35 degrees. A corner falls back to the GPS-only distance
   checks. GPS
   retreat opposed by forward AR motion is rejected. Without AR corroboration,
   the existing GPS uncertainty distance remains required. Both paths retain
   the six-second movement window, two advancing confirmations, spacing,
   ambiguity checks, destination protection, and reroute cooldowns.
2. Heading validation accepts either walking direction along a matched street.
   Three consistent qualified windows remain necessary. Weak fixes preserve
   candidates for at most 30 seconds between candidates and 60 seconds overall;
   conflicting signals, changed tracking references, duplicates, and expired
   evidence cannot accumulate a correction. GPS motion contradicting AR by
   over 90 degrees cannot rotate the route toward a GPS jump. Existing bounded
   correction sizes and cooldowns remain in force.
3. New GPS placement uses the existing fusion, identity, accuracy, and matching
   checks. Only qualified fixes refresh the physical display reference. PDR
   uses that reference and the current geographic route tangent independently
   of whether geometry is visible. Every step, including unpublished steps,
   is bounded to eight seconds and eight metres of verified GPS, with a fresh
   matching AR reference and no confirmed deviation or active reroute. This
   extends the existing publication budget to progress itself; it does not
   create a fused device coordinate or replace GPS arrival evidence.
4. The already verified geometry can survive a brief weak-fix interruption
   using that bounded physical reference. The six-second geometry hold still
   requires the same route version and ground reference, bounded displacement,
   available route identity, live tracking, and trusted heading. A replacement
   window no longer causes a preparation blink when its prior geometry was
   allowed and remains prepared in the same tracked ground reference. Resetting
   road entry or accepting a replacement requires a newly prepared window;
   the six-second preparation timeout remains.
   Depth occlusion, obstacle continuity, ground, tracking, and geometry rejection
   checks remain active.
5. `NAV_RENDER` records the applied version, root visibility, prepared/enabled
   segment counts, depth/prefix suppression, enabled segments in front of the
   camera, segment midpoints in the viewport, and arrow wings. These counts
   describe draw-thread state and approximate camera coverage, not proof that
   pixels are visible through scene objects or UI. Reverse diagnostics include
   independent AR retreat and the evidence source.

Both providers continue through the same current-GPS replacement entry point
and atomic destination/version, access, geometry, and distance validation.
No routing dataset, provider selection, dependency, arrival threshold, flood
renderer, native bridge, or camera startup-scene configuration was changed.

### Validation and manual acceptance

Validation is limited to the requested Android Release build. No additional
automated tests, readiness/package test scripts, integration tests, device
installation, or application interaction are authorized for this follow-up.
The final build completed successfully with **zero warnings and zero errors**:

```powershell
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False
```

Startup scene code generation remains enabled. The final build log is
`artifacts/field-test-20261019-live-guidance/android-release-final-build.log`.
The first build also passed; source review then tightened reuse across route
replacement/ground changes and restricted AR corroboration around corners,
followed by the final build above. No automated tests were run in either pass.

Candidate: [RescuAR-20261019-Live-Guidance.apk](artifacts/field-test-20261019-live-guidance/RescuAR-20261019-Live-Guidance.apk)
(60,338,609 bytes). SHA-256:
`9003f2fe2f180b4c697c4d75467d71f880920c6af9209af3bb619679740272e9`.
Source base: `9dd638e2e5615f3a0553d022fbc12f7d05f68f3a` plus this follow-up's
uncommitted changes. The build and candidate hash establish a local compilation
and artifact identity, not package-test or physical-device acceptance.

Repeat these steps for a new online MLD trip and a separate new offline A*
trip; confirm the provider in each capture. Restoring Internet during A* should
retain A* for ordinary replans. Losing Internet during MLD alone is not an A*
test. Record approximate elapsed times for each maneuver in the next report.

1. On a safe straight street, wait for qualified placement, face the route,
   and walk forward 20-30 metres. Expect a joined solid cyan line and matching
   arrows/text without a preparation blink at each position update. Brief GPS
   uncertainty may retain the last verified path with an explicit message;
   sustained uncertainty should fall back to recovery guidance.
2. Stay on that street and deliberately reverse 20-40 metres for at least ten
   seconds. Allow two fresh accurate observations and continued movement.
   Expect a logged current-position reroute, then updated route, distance,
   instructions, and AR placement after fresh verification. Poor positioning
   must not force an unsafe immediate replan. A valid new route can still ask
   the user to turn back toward the destination; verify that its origin and
   remaining distance use the new position.
3. Stand still and rotate the phone, then resume forward walking. Expect no
   reverse reroute from pointing changes or GPS jitter. Turn away from the
   route: camera-facing guidance should explain where to look. Depth-obscured
   geometry must not appear to pass through a genuine obstacle.
4. Compare the cyan road with a fixed visible street reference while facing
   along it and after turning around. Include the raw-road diagnostic view,
   reported accuracy, and a video if offset persists. Do not judge accuracy by
   assuming the person stands on the road centreline. Check `NAV_RENDER` and
   heading decisions to distinguish placement error, camera coverage, and
   occlusion.
5. Recheck a corner, leaving/rejoining the road, brief tracking loss, camera
   exit/reentry, a destination change during route work, arrival, and flood
   depth/ground acquisition. Expect no stale-trip line, repeated replans,
   premature arrival, or black camera. Return actual results and both complete
   captures before further fixes that depend on device behavior.

## 14:25/14:26 labeled captures: road alignment and offline shelter positioning

The supplied findings were read before either archive. Both labeled ZIPs are
complete, with verified start/end markers: MLD contains 4,342 application / 265
navigation records and 11 images; A* contains 6,335 application / 300 navigation
records and 13 images. All 24 images were inspected. Both installed APKs have
SHA-256 `e7ba140131d9608dfe33b5bff849b8b903b0a75dd34a86ed1bb742591b739648`,
source revision `524d748572c59ef7b421288b755653c326c744de`, diagnostics enabled,
and the route visibility override disabled. The earlier shared guidance fixes
are present in this revision and the current checkout.

### Evidence and confirmed findings

| Evidence | Finding |
| --- | --- |
| A* `logcat-001.txt:3284-3298`, 14:29:56 | Fresh CURRENT location, 16 m accuracy; offline A* selected a six-point, 278.6 m mapped route. |
| MLD `logcat-001.txt:3742-3906`, 14:29:56-14:29:59 | Fresh CURRENT location, 13 m accuracy; MLD selected a six-point, 270.0 m route. Later replacements were hazard-triggered. |
| A* `logcat-002.txt:1857` and `:2923`, 14:32:53 / 14:33:20 | Separate 1/3 heading candidates propose roughly 18-19 degrees of correction; no correction was applied during the capture. |
| Both captures' ground/recovery records | A* replaces its ground reference 22 times by arrival; MLD 11 times. Ordinary moving-ground retirement happens around 15 m of walking. |
| MLD `logcat-002.txt:7831-8001`, 14:34:51-14:34:55 | Two qualified proximity fixes confirm the destination vicinity at 28.2 m. Preserve this working arrival behavior. |
| A* `logcat-003.txt:3278`, 14:36:48 | Arrival was confirmed by the user, with 36.3 m geographic distance and 25.5 m reported accuracy. This is separate from automatic GPS confirmation. |
| A* `logcat-005.txt:120`, 14:42:01 | Arrival is still USER_REPORTED; the old route remains in memory, GPS polling is stopped, and rendering is disabled. The user confirmed they were checking this earlier trip, not starting a new trip. |
| `ASTAR_3` through `ASTAR_9`, `ASTAR_12-13`; `MLD_4-5`, `MLD_9` | Cyan guidance visibly crosses walls, fronts of houses, or other objects. Other images show the line along the street; this is an alignment failure, not universal absence of geometry. |

Many images are screenshots of video playback. The gallery clock is not the
original navigation time; visible playback offsets and remaining distances
allow approximate correlation only. Images have no EXIF acquisition times.
GPS coordinates and destination labels in the logs are consent-redacted.
These captures cannot establish a surveyed road/building offset or identify
an exact geographic translation to apply to the dataset.

The shared heading policy incorrectly treated a floor-anchor generation as
a change of camera world frame. Every ordinary floor handoff restarted the
movement baseline and discarded heading candidates. Camera poses remain in
the same AR session world during these handoffs. Three independent movement
windows often require more travel than the 15 m anchor retirement interval,
so the error-correction path could repeatedly restart before completing.
This is a confirmed source defect shared by both providers. Its precise share
of the observed building intersections still requires a physical retest.

The shelter-selection screen had a separate confirmed defect: it initialized
the user's location to fixed city coordinates, used an unchecked OS last-known
reading, and attempted only a one-second Low-accuracy update. Reentering that
screen offline did not refresh positioning. Catalog download methods also
carried location arguments/defaults and could apply distances for an earlier
position after a newer GPS observation. Its map could draw a fixed fallback
point labeled as the user's location. These defects are independent of A*.

The active offline A* trip itself received 127 current GPS samples; the median
reported accuracy was 7.5 m. The logs do not show an offline GPS acquisition
failure during active navigation. Nor do they show a new A* request at 14:42.
After arrival, hundreds of pointless anchor-rebase retries produced recovery
messages for completed guidance. Clearing completed visual state fixes this
misleading state without restarting the trip or changing arrival criteria.

### Implementation and preserved boundaries

- `HeadingRevalidationPolicy.cs` and `CameraPage.xaml.cs`: retain movement
  evidence across ordinary floor-anchor swaps. New AR sessions and explicit
  ARCore world corrections still invalidate movement evidence. Three qualified
  consistent windows, accuracy limits, disagreement checks, expiry, bounded
  correction sizes, and cooldowns remain required.
- `ReliableLocationCache.cs` and `MauiLocationService.cs`: retain only actual
  qualified readings in the shared location service and local preferences.
  Cache display is limited to two minutes and 30 m reported accuracy, with the
  original acquisition timestamp. Weak/older observations do not overwrite a
  newer qualified reading. Current-location requests still ask the device for
  a fresh fix; cached coordinates never acquire a new timestamp. Persistence
  omits walking course/speed and is rate-limited. Access remains permission-gated.
- `EvacuationCenterInfoViewModel.cs`: use that shared service online and offline,
  reacquire positioning when the page appears, and recompute distances against
  the latest eligible reading when catalog data is applied. No default user
  coordinate remains. Unknown/expired location removes distance/nearest claims
  and lists shelters by name. Last-known positions are labeled with their time
  and accuracy; distance is explicitly a straight-line estimate. A foreground
  display timer expires old distance claims and stops when the page leaves.
- `EvacuationCenterInfoPage.xaml` / `.xaml.cs`: expose positioning status and
  omit a user pin when positioning is unavailable. Cached pins are labeled
  last-known. Missing/invalid live shelter coordinates are skipped instead of
  replaced with a common city point; an all-invalid response preserves the
  existing saved catalog.
- `CameraPage.xaml.cs`: clear route visuals and suppress route-recovery work
  after either arrival path, while retaining geographic progress/destination
  for the arrival summary. A new destination selection still starts a new
  versioned trip normally.

The A* planner, MLD HTTP geometry, road graph/data, road-entry confirmation,
PDR budgets, reverse/off-route reroute thresholds, hazard checks, destination
persistence, automatic arrival policy, native bridge, flood renderer, and
compiled startup scene configuration were not changed. A* reconstruction uses
mapped source vertices; MLD requests full GeoJSON geometry; the local projector
preserves intermediate route vertices. No building-avoidance geometry, road
offset, guessed connector, or confidence override was introduced.

### Build and manual field acceptance

Android Release completed with **zero warnings and zero errors**:

```powershell
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False
```

No additional automated suites, readiness/package test scripts, device
installation, or UI interaction were run. Log/image analysis is investigation,
not a behavioral test. Build log:
`artifacts/field-test-20261019-road-alignment-offline/android-release-build.log`.

Candidate: [RescuAR-20261019-Road-Alignment-Offline.apk](artifacts/field-test-20261019-road-alignment-offline/RescuAR-20261019-Road-Alignment-Offline.apk)
(60,268,977 bytes). SHA-256:
`184b9509ee0923e0e3d933187010a210308a8dc47e459f8d546852648a58daaa`.
Source base: `8e026de65fec1101c1d2ea36f382b80282643ce9` plus these uncommitted
changes. No Git commit was created.

1. At the same safe street position, open the shelter list online and then
   offline with Location still enabled. Check acquisition time/accuracy, the
   same selected shelter, and comparable straight-line distances. Route distance
   can legitimately differ between providers and from straight-line distance.
2. After a qualified location is acquired, close/reopen the app offline within
   two minutes. Expect an explicitly last-known position until a fresh fix
   arrives. With no new usable fix, wait beyond two minutes: distance/nearest
   claims and the user pin must disappear, with no jump to a default city point.
   A stale cache must not start AR placement or confirm arrival.
3. Start separate online MLD and new offline A* trips to the same chosen center.
   Walk a safe straight section for at least 60 m, including several ground
   handoffs. Expect consistent heading confirmations to survive floor swaps,
   and bounded corrections when independent signals agree. Compare the cyan
   line with visible street edges throughout, at a corner, and when facing back.
4. Recheck reverse walking, leaving/rejoining the road, camera exit/reentry,
   and a destination change. Expect current-position confirmed replans, stable
   destination assignment, and no reroute from standing still or rotating the
   phone. Fresh placement must still recover safely after actual tracking loss.
5. Finish MLD by qualified proximity confirmation and separately exercise manual
   arrival. Expect the arrival summary and completed guidance: no cyan route,
   stale instructions, or repeated AR recovery warnings. Select a destination
   again to start a new trip; confirm its cyan geometry recovers from fresh data.
   Recheck flood depth and ground acquisition for regression.

Return both complete captures and original navigation footage or timestamped
screenshots. Physical road alignment, offline cache persistence on each phone,
and UI layout remain unverified by the build. If building intersections persist,
obtain a consented synchronized position trace and a reliable road reference
before changing geographic data or applying an offset.

## 15:36 capture: approved local placement and hazard corrections

The investigation and plan were presented before implementation and explicitly
approved. Evidence and the original plan are retained in
`artifacts/field-review-20261019-1536/Investigation-and-proposed-plan.txt`.

The capture confirms two MLD trips, not a successful A* trip. Failed A* hazard
replacement attempts did not change the accepted provider. An estimated ground
reference remained fixed while the camera travelled 85.81 m away, beyond the
existing 15 m retirement and 20 m publication limits. A route window remained
unchanged for approximately 81.5 seconds while confidence could still report
Full. Separately, hazard monitoring repeatedly rejected outward escape paths
that the planner had validated, considered the passed portion of a long current
segment, retried unchanged NoRoute cases, and used a turn-card warning that
ordinary guidance updates could overwrite.

### Changes

- Estimated references now renew horizontal X/Z at the existing 15 m limit.
  Floor Y, provisional trust, ongoing Plane/Depth search budgets, and AR session
  ownership are retained. One invalidation advances the ground reference and
  replacement generation and requests a fresh route rebase. Invalid poses and
  implausible camera heights cannot manufacture a replacement floor.
- Publications carry the captured session, ground reference, pose timestamp,
  source observation, and placement origin. Rendering uses that origin.
  Confidence, short geometry holds, and the final render gate reject a route
  whose reference is incompatible or farther than the existing 20 m limit.
  Equivalent local points cannot preserve an obsolete placement origin.
- Both providers use the same hazard monitor. It clips the current segment to
  remaining progress, retains only a validated replacement's outward escape
  contract, and continues rejecting later re-entry or a hazardous destination.
  An initial route does not acquire an escape exemption merely because GPS
  currently matches inside a hazard.
- An unsuccessful assessment is retained for unchanged route, hazards,
  connectivity, and mapped segment. Changed exclusions, connectivity, a new
  segment/trip, or an existing confirmed reverse/off-route replan can permit
  reconsideration. The existing cooldown remains. Stale completions cannot
  suppress a newer trip or hazard assessment.
- Hazard status persists in the status banner independently of the turn card:
  finding an alternative, leaving a reported hazard, or no safer route found.
  A real emergency advisory retains priority and includes the route notice.
- Bounded diagnostic records add the actual accepted algorithm, source geometry
  fingerprint and embedded dataset identity; publication/draw origins and
  sample times; signed GPS-to-road offsets; GPS versus tracked-AR movement
  residuals; and heading-evidence reset reasons. Remote dataset identity,
  location provider, and bearing uncertainty are explicitly unavailable when
  the current interfaces do not supply them. No absolute coordinates were added.

Road data, geographic offsets, GPS sampling and accuracy gates, heading
thresholds, automatic arrival criteria, flood materials, native bridge, and
dependency pins retain their existing behavior. A stable remaining lateral
offset is not established as corrected by these changes. The new diagnostics
are intended to distinguish GPS translation, yaw, source geometry, and actual
draw placement in the next capture.

### Verification and candidate

**213 focused regression checks passed**, covering the new reference boundaries,
long provisional walks, captured-reference identity, hazard escape and re-entry,
unsafe destinations, unchanged/revised assessments, stale completions, and the
existing A*/MLD guidance, backtracking, arrival, ground/flood, and compiled scene
fixtures. The routing fixtures use in-memory graphs and stubbed HTTP responses.

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj --no-restore -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=True -- --placement-hazards
```

Android Release passed with **zero warnings and zero errors**:

```powershell
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release --no-restore -p:GenerateEvergineContent=False -p:RescuArDiagnosticBuild=true -p:AndroidPackageFormats=apk
```

An initial build attempt also set `GenerateEvergineScenesCode=True` globally,
which incorrectly activated scene generation in the MAUI host and failed with
WESC003 / namespace errors. The one corrective rebuild used the command above,
leaving the shared application project's existing scene generation enabled.
The startup guard and generated scene factories remain intact. Only whitespace
alignment and this validation record were changed after the successful build.

`Verify-ARCoreReleaseReadiness.ps1` and `Verify-AndroidNativeBridge.ps1` passed.
The native bridge is unchanged, ARM64, stripped, and has 16 KiB load alignment.
The SDK lacks `apkanalyzer`, so `Test-ARCorePackage.ps1` was not run. Equivalent
package ID, required ARCore/camera/Vulkan declarations, Vulkan version/level,
minimum API, ARM64-only libraries, and diagnostic profile checks passed using
the installed `aapt` inspector and the artifact-local
`Verify-PackagedCandidate.ps1`. No dependencies or SDK components were installed.
The inspection used NDK 30's readelf; native provenance toolchain fields describe
the pinned bridge configuration, not a native rebuild in this run.

Evidence:

- `artifacts/field-review-20261019-1536/implementation-regression.log`
- `artifacts/field-review-20261019-1536/release-readiness.log`
- `artifacts/field-test-20261019-local-placement-hazards/android-release-build.log`
- `artifacts/field-test-20261019-local-placement-hazards/package-check-report.json`
- `artifacts/field-test-20261019-local-placement-hazards/native-bridge-provenance.json`

Candidate: [RescuAR-20261019-Local-Placement-Hazards.apk](artifacts/field-test-20261019-local-placement-hazards/RescuAR-20261019-Local-Placement-Hazards.apk)
(60,674,615 bytes). SHA-256:
`2dcf0fdcead0c25de03fdeafd4bc5f52bc3fd5e325a501ae83cf41b7fd903cb7`.
Source base: `d997103a8855082056ca77dd563aff53bc84973f` plus these uncommitted
changes. Diagnostics are enabled and the route visibility override is disabled.
No commit, device installation, UI interaction, live integration test, or E2E
test was performed.

### Required phone retest

1. Start a new online MLD trip and a separate new offline A* trip to the same
   center, leaving Location enabled. Verify each accepted provider in the new
   `NAV_ROUTE_ACCEPTED` records. A failed A* replacement on an MLD trip is not
   evidence of an A* trip.
2. Walk a safe straight section for more than 60 m with ground still estimated,
   then allow Plane/Depth verification. Expect bounded reference handoffs,
   unchanged floor height, and no frozen old window returning as Full. Check
   cyan placement at fixed street landmarks in both walking directions.
3. Exercise outward escape, a passed hazard on a long segment, an unavailable
   alternative, and a newly changed hazard. Expect a persistent, appropriate
   notice; no repeating unchanged failed calculation; and renewed assessment
   after a relevant change. Use reported/test hazards without entering danger.
4. Recheck reverse walking, a corner, leaving/rejoining the route, brief tracking
   loss, camera exit/reentry, a destination change, and completed arrival. Expect
   shared provider behavior, current-trip geometry, and preserved arrival rules.
5. Check indoor ground acquisition and flood depth briefly. Expect camera startup,
   visible flood rendering, and no floor-height jump after horizontal renewal.

Return the actual outcomes and complete captures for both providers before any
further behavior-dependent correction. Include original navigation footage or
timestamped markers at identifiable street points if lateral error persists.
Physical road alignment, device rendering, and banner layout remain unverified
by these builds and unit/package checks.
