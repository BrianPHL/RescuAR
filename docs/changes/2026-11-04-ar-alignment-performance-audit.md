# AR alignment and performance audit

Date: 2026-11-04 (Asia/Manila)
Starting source: clean `dev`, `62bda12`.

## Audit findings

The user reported expensive AR operation and intermittently skewed visualization
with both A* and MLD, with more promising but not yet repeatable MLD results.
This audit inspected the current source and focused host checks. It did not
observe a new device trial or measure frame time, battery use or physical error.

| Priority | Finding | Action and evidence boundary |
| --- | --- | --- |
| High | Ground-anchor recovery detected horizontal translation but not rotation. A rotation around a stationary retained anchor could leave geographic yaw and route registration in the previous world frame. | Detect sustained, verified same-reference anchor yaw changes and request existing frame requalification. This is a source coverage defect; its incidence in the reported trials is unproven. |
| High | A pending world correction blocked the untrusted tracked motion needed to collect new registration and movement-heading evidence. Recovery could wait on evidence it prohibited, especially after movement-based calibration. | Permit evidence collection only in the current correction frame while keeping route publication held until normal recovery qualifies it. |
| Medium | Local clipping, distance interpolation and tracked display projection repeatedly scanned retained route prefixes. The cost grew with total route size even for a short visible window. | Use binary cumulative-distance bounds and examine only the existing eligible progress interval. Preserve duplicate-distance and exact-vertex ownership, mapped corners and ambiguity checks. |
| Medium | Depth decoding performed two managed-to-Java calls for each pixel on the camera acquisition worker. | Bulk-copy the valid plane extent once into pooled storage, then decode its explicit little-endian strides in managed code. Preserve producer buffer position/limit and pooled output ownership. |
| Medium | Workload diagnostics formatted the same string every frame. Shared logging searched assemblies and reflected each write. Route width also dirtied unchanged segment transforms each frame. | Compare typed workload state first, cache Android logging delegates and set scale only when width changes. |
| High, field evidence required | A single qualified GPS registration can still carry an initial geographic bias. Compass repeatability establishes stability, not absolute street alignment. Existing correction policy intentionally rejects same-quality correlated GPS shifts. | Do not weaken those safeguards without independent evidence. Compare measured lateral/heading error at known references, including moving-heading calibration and both walking directions. |
| Medium, profiling required | Each camera import creates/imports Vulkan image, memory and view resources; the single conversion target requires scene presentation to wait for a completed conversion. Small pose/UV arrays also remain. | Measure acquisition, import, conversion/fence, draw and managed allocation costs first. Buffer caching and multiple conversion targets require explicit GPU fences, retained hardware-buffer lifetime and matching camera/pose metadata; no speculative native ownership rewrite was made. |
| Medium, data evidence required | A* embeds legacy GeoJSON while the server independently builds legacy PBF data. Checked-in central snapshot/manifest parity is absent, and ordinary OSRM responses do not establish the deployed dataset identity. | Obtain matching app/server data identities and compare full geographic geometry on the same trip. Do not interpret MLD's better appearance as proof of superior AR registration. No dataset or deployment was changed. |
| Medium, terrain limitation | The route uses horizontal local geometry at one trusted anchor height. It does not follow an independently measured uphill/downhill surface. | Validate placement on level ground first. Terrain-following geometry requires measured surface evidence and separate occlusion/continuity validation; a guessed per-vertex height would add misleading placement. |
| Medium, acceptance required | Existing host tests cover policy and source contracts; they cannot establish sustained visibility, physical alignment, power consumption or multi-device crash freedom. | Use the exact-candidate two-device procedure and retain matching logs/video. All physical and performance acceptance remains pending. |

The forward/inverse East/North-to-AR transforms were internally consistent in
the inspected source. Both providers use the same registered projection and
renderer; current normal publication uses a short local window. There is no
source evidence here for a blanket A* axis/scale error or for replacing A* with
MLD as the alignment fix. Map centerlines, initial GPS bias, heading bias and
world-frame corrections are distinct possible causes of visible displacement.

Owning evidence: [anchor correction/placement](../../RescuAR/AR/ARCameraSpatialController.cs),
[Camera evidence/publication](../../RescuAR.MAUI/Views/Camera/CameraPage.Positioning.cs),
[heading calibration](../../RescuAR.MAUI/Services/Navigation/ArHeadingAlignmentService.cs),
[registration and tracked projection](../../RescuAR/Navigation/Progress/RouteDisplayProgressPolicy.cs),
[local clipping](../../RescuAR/Navigation/Projection/LocalRouteProjector.cs),
[Android depth acquisition](../../RescuAR.MAUI/Platforms/Android/Services/ArCoreService.cs),
[Vulkan camera importer](../../RescuAR.MAUI/Platforms/Android/Services/ARCoreVulkanImporter.cs)
and [routing/data contracts](../../RescuAR/Navigation/Routing/README.md).

## Changes and purpose

- Shared anchor refinement and Camera positioning recovery address the two
  frame-ownership findings without blindly rotating a route or manufacturing GPS,
  progress or arrival evidence.
- `RouteDistanceIndex`, `LocalRouteProjector`, `RouteDisplayProgressPolicy` and
  `RouteProgressTracker` bound distance lookup work while preserving existing
  corridor, registration, hazard and progress rules. OSRM requests retain full
  GeoJSON geometry but omit unused turn-step payloads; current guidance continues
  deriving its instructions from geographic geometry.
- `ARDepthImageDecoder` and Android depth acquisition replace per-pixel Java
  access with one pooled bulk copy. Layout validation includes padded rows,
  strided pixels, truncated input and omitted final-row padding.
- Android workload diagnostics, `AndroidLog` and `ARRouteRenderer` avoid repeated
  unchanged logging/transform work. No appearance redesign was introduced.
- Added focused checks for correction-frame recovery, anchor yaw, decoding and
  distance lookup behavior/operation counts. Updated the owning
  [routing guide](../../RescuAR/Navigation/Routing/README.md),
  [field procedure](../field-testing/README.md),
  [status register](../status.md), change index and generated catalog.
  Dependency pins, native bridge/import ownership, provider selection, routing
  datasets, arrival thresholds and Depth compatibility policy remain unchanged.

## Verification

- **Passed:** 499 focused checks through the normal `--ar-audit` runner, including
  heading recovery, provider ownership, both registered publishers, correction
  requalification, depth decoding and route lookup boundaries. The 20,001-point
  fixtures establish bounded point reads for short windows instead of a
  full-route prefix scan; they do not measure Android frame time.
- **Passed:** 314 supplemental checks compiled the current shared AR, Navigation
  and Diagnostics source in an isolated host harness with existing local Evergine
  references. This was used while the normal tool settings were inaccessible;
  it did not substitute for the later normal runner. The final 22 distance checks
  passed again after removing two unreachable-loop compiler warnings.
- Initial restricted runs failed before compilation on NuGet settings/lock access
  and Evergine add-on resolution. The successful normal run used existing tool
  settings with the actual root `.weproj` supplied explicitly. No lock was deleted,
  packages/frameworks installed, dependency pins changed or scene generation
  disabled. From the repository root, the successful command was:

  ```powershell
  $projectFile = (Resolve-Path RescuAR.weproj).Path
  dotnet run --no-restore -c Release --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj "-p:EvergineProjectFilePath=$projectFile" -- --ar-audit
  ```

- **Passed: Android diagnostic Release build**, zero errors; existing
  `CS8601` in `SafetyCircleService.cs:274` remains. The first build
  caught one old workload-log field name left in initialization; corrected it
  and ran the allowed single fix/retest. Source profile:
  `62bda12-ar-audit-20261104`, `RescuArDiagnosticBuild=true`,
  `GenerateEvergineContent=false`, normal scene-code generation retained, existing
  root project file explicitly selected. This build uses the existing content
  assets and is not a fresh content/native rebuild or package validation.
- **Passed:** ARCore compatibility/manifest/GC-setting static check,
  documentation catalog/link/import checks and whitespace checks.
- **Not run:** app/browser interaction, integration/E2E, device walks, live server
  or dataset changes, package acceptance and measured performance/physical alignment.

## Device acceptance and remaining improvements

Use the [current field procedure](../field-testing/README.md). Compare three A*
and three MLD trips on Honor X9B and Samsung Galaxy A54 with the same APK, street,
starting point and direction, including a reverse trip. Record actual provider,
Depth mode, GPS accuracy/cadence, heading source, registration state, hold reason,
visible failure time and route geometry. Check phone rotation while stationary,
brief tracking loss/relocalization, walking past the original anchor, AR/2D return
and online/offline handoff. Expect qualified reappearance after frame recovery,
without continuing to show the old skewed placement or stalling permanently.

Measure lateral error and route bearing at known physical street/sidewalk
references, acquisition/recovery time, useful-AR visibility, camera-frame gaps,
frame-time distribution, allocation/GC rate and thermal state on repeated walks.
Compare identical diagnostic profiles; production and diagnostic timing are not
interchangeable. No measured speedup or physical alignment improvement is claimed
by the source optimizations. Obtain actual field evidence before further
behavior-dependent tuning, larger horizons or native caching changes.

The user authorized one combined commit on `dev` for rollback. No push or
deployment was requested.
