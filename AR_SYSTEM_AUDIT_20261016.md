# AR system audit and corrective changes — 16 October 2026

The audit covers the shared A*/MLD navigation-to-AR pipeline, camera acquisition
and Vulkan conversion, pose/projection and frame coherence, ground acquisition
and recovery, route and flood rendering, sensors, arrival, lifecycle, and graphics
teardown. It also reviews the pending TIP follow-up changes themselves.

Changes are local and **uncommitted**, based on `36beaf49f2a5a2826cdd154ec4babed4df890c7b`
on `dev`. Map fallback is excluded as requested. Dependencies and native bridge
pins are unchanged. This is source, unit, build, and package verification; it is
not physical-device acceptance or a guarantee that no other defect exists.

## Evidence and diagnosis

The three October 16 captures used `188154c`, before the arrival and anchor
recovery corrections committed in `36beaf4`. The additional 17:42 screenshot
therefore does not establish whether those corrections worked.

At 17:42:17 the device reported 29.8 m from the destination with ±21 m GPS
accuracy. The screenshot reports ±27 m. At 17:42:20 camera tracking was live
while geographic route identity was suspended. Automatic arrival and route
visibility being withheld in that state follow their independent location
checks; this is not evidence that A* failed to calculate a route.

There is separate, direct evidence of camera conversion stalling: two-second
GPU fence timeouts at 17:32:35 and 17:42:21 in the second capture, device loss at
17:42:21.831, and another timeout at 17:45:44 in the third. Acquisition FPS does
not measure presentation smoothness. Earlier path replay established that the
supplied QC start/destination had a connected A* footpath route; these changes
do not replace the map extracts or invent sidewalk geometry.

The remaining findings below are source defects or new supported behavior.
Their presence in source does not prove each occurred during the TIP walk.

## Findings and implemented fixes

Paths in this table are relative to the repository. Camera files are under
`RescuAR.MAUI/Views/Camera/`; Android services and Vulkan helpers are under
`RescuAR.MAUI/Platforms/Android/`.

| # | Defect or gap | Implemented correction | Verification boundary |
|---:|---|---|---|
| 1 | Each camera conversion could block the draw thread for two seconds. Full viewport conversion also added GPU work. | `VulkanCameraCommandContext` waits up to 4 ms initially, then polls on subsequent callbacks. Pending targets are not presented. `ARCameraConversionPolicy` caps the longest target edge at 1280 while preserving proportions and UVs. The tested 1080×1901 viewport becomes 727×1280, about 55% fewer conversion pixels. | Dimension, pending-draw, and watchdog classification checks; Android build. Actual FPS and camera detail need phone comparison. |
| 2 | Timeout cleanup could destroy a submitted image or rewrite its descriptor before completion, violating GPU resource lifetime. | The importer retains the complete pending image/descriptor/target set. Timeout or device loss ends that graphics session; cleanup follows the existing GPU-idle teardown boundary. | Ownership-path review and terminal classification checks. GPU lifetime validation on the device remains necessary. |
| 3 | Device-loss recovery returned an old texture, which could be labeled as a fresh frame. | The importer throws a terminal failure instead of returning the old image. A conversion keeps its original metadata and cannot refresh an expired frame. | Failure policy, original-age, generation, and suspension checks. |
| 4 | Conversion had no dependency covering the previous draw's texture read, or the next draw's read of converted writes. | `VulkanCameraConversionPipeline` adds explicit incoming fragment-read/attachment-write and outgoing attachment-write/fragment-read dependencies on Evergine's graphics queue. | Source checked against Khronos synchronization rules; Android compilation. Requires GPU/device verification. |
| 5 | The pending-conversion change could create a CPU busy loop by skipping normal presentation pacing. A resized conversion target could also leave the background bound to the disposed texture for one draw. | Skipped draws use the handler's 16 ms idle pacing. Background texture binding now occurs after import on the draw thread. Format/size replacement first drains preceding GPU draws. The installed Evergine texture setter was inspected to confirm it updates the background material immediately. | Pending/teardown checks, framework method inspection, and build. Rotation/resize and pacing need phone checks. |
| 6 | Camera, pose and depth age was measured at publication, allowing queued old work to appear new. Pose/projection and depth inputs lacked complete numeric validation; depth dimension multiplication could overflow. | `ARFrameMetadata` carries acquisition time through conversion. Bridges preserve that age. Invalid positions, zero/invalid rotations, non-finite projection/intrinsic/UV values and invalid buffer dimensions are rejected or made unavailable. | Fresh/delayed/future metadata, pose, projection, ground-coordinate, depth age, overflow and intrinsics checks. |
| 7 | Short tracking hold returned visible guidance before testing GPS freshness, route identity, heading and rerouting. | `ARGuidanceConfidencePolicy` applies those independent gates during short hold and requires frozen-placement trust. Healthy brief holds still preserve guidance. | Positive healthy-hold case and separate stale-GPS, uncertain-identity, heading, reroute and low-trust rejection checks. |
| 8 | After 600 ground probes, a retained session was suspended permanently, even after later recovery. | `ARGroundProbeBudget` renews a bounded 600-probe allowance every five minutes. Existing sweep limits, 144-probe search windows, 30-second cooldowns, failure backoff and thermal limits remain. | Exhaustion, renewal, reset and clock-reset checks. The supplied logs reached at most 182/600, so exhaustion was not observed in these walks. |
| 9 | Heading acquisition could race stop/dispose and orphan a sensor lease. PDR stop could release a replacement lease. Delayed callbacks could contaminate a restarted consumer. | Heading start is serialized. Heading and PDR stop detach their specific lease under the owner lock and dispose that captured lease. Callbacks carry a consumer generation; stopped/disposed or older consumers cannot update current sensor state. | Actual service sources run against test-only MAUI sensor fakes: overlapping start/stop, shared consumers, restart, disposal and repeated release. Android sensor timing remains unverified. |
| 10 | A stale depth/flood reader could clear a newer publication. Ground updates accepted an old graphics/geometry generation from the same retained session. | Conditional clearing checks the publication version. Ground transitions require the complete active render token. Readers return unavailable for their invalid observation without deleting a newer publisher's data. | New-depth publication survives an old-reader clear; old graphics/geometry cannot overwrite ground. Flood invalidation reviewed; live flood rendering needs device checks. |
| 11 | Camera could miss destination changes while hidden and keep the previous trip's confirmed arrival. Delayed arrival decisions could reach a different trip, including a reselected identical pin. | `NavigationDestinationBridge` versions every selection/clear. Camera reconciles that version on appearance and binds retained routes and arrival UI to it. `SafeZoneConfirmationService` serializes evaluation, resets for a changed trip/pin and rejects older trip observations. | Same-pin new trip, previous confirmation reset, stale callback rejection and changed-pin reset checks. Shell/dialog behavior needs phone testing. |
| 12 | Canceled route/heading work could clear current state after an await. A new destination request could be skipped behind the canceled request and never restarted. | Route startup, progress and replacement recheck cancellation and trip version after waits. Old results cannot publish geometry or failure state. Completion schedules the current destination when an older request had occupied the request slot. | Source review of startup, unrouted/routed progress, replacement and completion paths; routing regressions and build. Rapid destination switching needs manual verification. |
| 13 | A superseded lifecycle timeout could fault a newer request. Late calls could alter disposed state, and duplicate shutdown requests could supersede one another. | Lifecycle state/failure publication checks request generation atomically. Shutdown intent rejects later start/pause requests, concurrent shutdown callers share the same task, and disposed state cannot reactivate. Queued Camera lifecycle UI checks the current snapshot. | Lifecycle policy and shutdown-intent checks; Android build. Native start/pause/drain behavior requires interruption tests. |
| 14 | The concrete-format HardwareBuffer fallback imported the first image once and then returned it indefinitely without normal frame/UV conversion. | Remove that obsolete frozen-image path. Concrete formats without the supported external-YCbCr conversion now produce an explicit unsupported-camera-format failure instead of misleading live passthrough. | Failure classification and build. This does **not** add concrete-format camera support; the field-tested external-YCbCr path remains supported. |
| 15 | Indoor GPS could remain too uncertain for automatic arrival without a way to report physical arrival. | Add **I'm at the destination**, with an explicit confirmation dialog and **Arrival Confirmed by You** result. It uses the selected trip, a nearby valid fix (≤75 m, accuracy ≤50 m, age ≤120 s), and rechecks eligibility after the dialog. Confirmation stops route work and records `USER_REPORTED` separately from `GPS_VICINITY`. | Eligibility boundaries and arrival/state checks. Dialog interaction and displayed result need phone testing. |

## Safeguards retained

- Automatic arrival still requires three distinct accurate observations over at
  least six seconds inside an accuracy-inclusive radius capped at 30 m, with
  fresh fixes, accuracy ≤15 m and no intervening major-road barrier. A user
  report does not count as automatic GPS confirmation or certify facility safety.
- The previously committed nearby-arrival hold limits unnecessary rerouting
  around uncertain destination fixes; accurate departure and barrier checks
  still release/reject the hold.
- Both routing engines use the same geometry and confidence gates. The actual
  mapped cyan polyline remains separate from the supplemental access arrow.
  The 7.5 m mapped preview and two-fix road-entry checks are preserved. No
  straight connector across an unknown building or road is fabricated.
- Geographic route state may survive a retained AR session; local camera,
  pose, ground, depth and flood data require the current render generation.
- No framework upgrade, native rebuild, device depth-policy change, map fallback
  change, integration test, E2E test or app/browser interaction was performed.

## Local verification

- **476 regression checks passed**, including **43 additional audit checks**
  beyond the 433-check TIP follow-up. The initial shared build exposed a missing
  required empty `Behavior.Update` override; it was corrected and the runner
  passed. The final runner reports no compiler warnings.
- Android **Release build passed: 0 warnings, 0 errors**. Its initial build caught
  two leftover references to the removed fallback; those were removed and the
  rebuild passed. No failed verification is being treated as a pass.
- `Verify-ARCoreReleaseReadiness.ps1` passed source manifest/compatibility checks.
- `Test-ARCorePackage.ps1` passed the rebuilt APK's merged manifest, sole ARM64
  ABI, native bridge ELF/export/dependency/provenance, required AR/Vulkan
  declarations and production build-profile assertions. The installed Android
  Windows batch launcher mishandles spaces in its SDK JVM option. A local
  quoted Java wrapper invokes the same installed inspector; SDK files and the
  repository verifier were not changed.
- No physical-device test was performed. Sensor fakes, policy checks and build
  success cannot establish GPU smoothness, heading accuracy or visual alignment.

The pending source fixes are included in this candidate, not in base commit
`36beaf4` alone:

`artifacts/field-test-20261016-ar-audit/RescuAR-20261016-AR-Audit.apk`

- SHA-256: `8a7847ea851a6577dcc9086d0274249a21f63e2e8ae58fa000a0eb7d1f54c0e8`
- Native bridge SHA-256: `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`
- Release, ARM64, diagnostic build **false**, route override **false**; local test
  signing. This replaces the earlier TIP follow-up candidate for the next test.
- The artifact folder contains the package report, native provenance, build and
  regression logs, retest notes and a source-file hash manifest for this working tree.

## Required phone retest

1. Repeat the TIP route using A* and MLD. With trusted GPS, heading and ground,
   confirm that the mapped cyan route appears and follows mapped turns. The
   access arrow must not replace the route or point across an unverified barrier.
2. Enter the selected indoor destination. With uncertain GPS, verify that
   automatic arrival remains held and the nearby confirmation action appears.
   Choose **Keep guiding** first: guidance must continue. Then confirm physical
   arrival: the overlay must say **Arrival Confirmed by You**, log
   `USER_REPORTED`, and stop rerouting. Also repeat accurate outdoor automatic arrival.
3. After arrival, leave Camera, select a new destination and return. Repeat with
   the identical pin. Each selection must start a fresh trip. Change destination
   while route calculation is pending; only the latest trip should receive a result.
4. Pan/walk, rotate, background/resume, and exit/re-enter Camera repeatedly.
   Check image proportions, camera smoothness, route placement and recovery.
   Include a session longer than five minutes with ground loss/reacquisition.
5. Supply the new field archive and actual observations before further
   behavior-dependent fixes. Compare completed-conversion FPS, deferred counts,
   conversion time, frame age, GPU timeouts/device loss, ground budget and arrival
   source. Acquisition FPS alone cannot close the stutter report.

Physical heading error, GPU/device-loss behavior, JNI wrapper pressure,
surface teardown under interruption and long-session allocation/thermal behavior
remain device-validation items. Existing framework support/migration Finding 38
also remains open; this audit does not mark it resolved.

## Technical references

The graphics corrections follow the requirements for resource lifetime,
descriptor updates and execution/memory dependencies:
[Khronos image lifetime](https://docs.vulkan.org/refpages/latest/refpages/source/vkDestroyImage.html),
[descriptor updates](https://docs.vulkan.org/refpages/latest/refpages/source/vkUpdateDescriptorSets.html),
[synchronization examples](https://docs.vulkan.org/guide/latest/synchronization_examples.html).
