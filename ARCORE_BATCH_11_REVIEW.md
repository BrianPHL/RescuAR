# RescuAR corrective batch 11

## Scope and provenance

Reviewed all three supplied ZIPs, using their names as version labels. Archive
documents were treated as evidence, not instructions or proof that field tests
had passed. CURRENT matches the tracked source at base commit `d8986bd`.
The batch is on `codex/ar-camera-recovery` in an isolated checkout; the original
workspace's 15 existing modified files were preserved. Selected existing fixes
were reviewed and refined rather than blindly included. Dashboard edits and
the older placeholder shelter contacts were excluded.

| Archive | Files | SHA-256 |
| --- | ---: | --- |
| BEFORE_FIRST_FIELD_TESTING | 515 | `e8c187650d9a260f45847e24d63bcb8ac83b5a5696873345f8b0b3514f36bc35` |
| BEFORE_SECOND_FIELD_TESTING | 546 | `0122188a33ddc4956f1895d2f1615a2b0c0d14d0b694b3e5e558322d8c255a76` |
| CURRENT | 548 | `24a8822f44c79b5cf156f9151cbe6b2d521ba5d8a641a53a9419fd8b6bb42909` |

After normalizing archive root folders and text line endings, CURRENT differs
in 108 paths from FIRST and 34 paths from SECOND. The 34-path review inventory
is below. An unchanged source file can still contain an inherited defect;
those fixes are explicitly identified separately.

## Camera findings and corrections

The pause/resume proposal is appropriate, but the Android rendering surface
must also be restored before camera frames resume.

1. **Current-specific recovery failure:** CURRENT added deferred surface
   restoration with an 18-second teardown wait. Its resize method also required
   rendering to be enabled and the worker to have the original thread ID.
   A restored Android surface can run on a new worker, and recovery needs to
   resize while rendering is suspended. These conditions can prevent recovery
   permanently. Temporary surface loss now retains the session, importer, and
   graphics device. It pauses frame work and refreshes only the replacement
   surface; permanent handler removal still cleans up resources.
2. **Shared lifecycle weakness:** SECOND already tore down graphics on temporary
   closure and removed surface callbacks; FIRST lacked the later Activity
   lifecycle wiring. CURRENT's attempted restoration compounded these issues.
   The new `ArCoreRunIntent` retains the Camera view's request separately from
   Activity and surface readiness. Both return orders work, repeated pause
   notifications cannot lose the request, and late callbacks cannot undo a
   Camera-tab exit or terminal shutdown.
3. **Graphics ownership:** the installed Evergine Android 2025.10.21.3204 backend
   was inspected. It creates a worker for each surface creation, sends surface
   information on that worker, then begins drawing. Initial loading, drawing,
   surface replacement, and terminal handler cleanup are now serialized.
   Importer ownership transfers only after conversion has drained, and old
   worker callbacks are rejected. Layout changes alone cannot authorize reuse
   of a destroyed native surface handle.
4. **Redundant graphics rebuilding:** CURRENT refreshed and resized the same
   swap chain for every notification. Inspection of the pinned Vulkan backend
   showed that `RefreshSurfaceInfo` already rebuilds it and waits for the device.
   Duplicate same-size notifications are skipped; a size-only resize waits for
   the graphics device first. Idle callbacks sleep briefly, and elapsed frame
   time is bounded after a long pause.
5. **Repeated prerequisite work, inherited:** an existing session no longer
   repeats availability, installation, and bridge startup checks on every
   resume. Camera permission is checked before resuming it. First startup still
   performs the full checks. Obsolete asynchronous surface-recovery code was
   removed; stale lifecycle completions cannot replace newer pause/resume
   results.

This follows Google's [ARCore Session lifecycle contract](https://developers.google.com/ar/reference/java/com/google/ar/core/Session)
and Vulkan's [surface and presentation synchronization requirements](https://docs.vulkan.org/spec/latest/chapters/VK_KHR_surface/wsi.html).
The latter requires application synchronization; retaining a session alone
does not make concurrent native rendering safe.

### Crash evidence and limits

The workspace also contains independent field logs:

- `Logs/RescuAR_FieldTest_20260929_003026/logcat-001.txt`, lines 7763 and 8482:
  native segmentation fault in a worker, with the Mali driver's
  `vkCmdPipelineBarrier` on the crash stack.
- `Logs/RescuAR_FieldTest_20260927_134151/logcat-014.txt`, lines 8674 and 9146:
  native segmentation fault on `ms_depth`.

These are two different native failure paths. The logs do not establish that
each executable exactly matches a supplied ZIP. This batch removes identified
rendering races and preserves CURRENT's Android 16 depth-off profiles for
Samsung SM-A156E and SM-A546E. It does not claim that vendor-driver or depth
crashes are eliminated. A native segmentation fault cannot be recovered with
a C# exception handler. Device soak testing remains required.

## Other findings and corrections

| Finding | Version attribution | Correction |
| --- | --- | --- |
| Offline first launch has no shelters when the persistent cache is empty | Introduced in CURRENT when the bundled list was removed | Restore nine historical location entries. Saved data takes priority and successful live data replaces it. Offline opening status and occupancy are explicitly unverified; historical sample officers, telephone numbers, facilities, and capacities are not restored. |
| Dense mapped routes disappear because source-preserving geometry exceeds 64 segments | CURRENT interaction between stricter geometry and the existing publisher | Reduce the local window until it fits, down to the existing 7.5 m approach window. Preserve mapped corners and endpoints; never invent a shortcut to meet capacity. |
| Tiny retained spans disappear; invalid interior points can join unrelated neighbors | CURRENT lowered sanitizer spacing to 1 cm while drawing still rejected larger spans; invalid-point dropping was inherited | Align the renderer's span threshold and reject the complete invalid/non-monotonic window. |
| A prior road-diagnostic pool affects normal route occlusion | Introduced with CURRENT's expandable diagnostic pool | Restrict normal-route visibility calculations to the 64 route slots. |
| Nearest origin snap chooses a disconnected pedestrian fragment despite another nearby path reaching the shelter | Exposed by CURRENT's connected-component restrictions | Filter origin candidates by viable destination components, retaining the 20 m origin limit, 50 m destination limit, and major-road barriers. |
| Proximity-only arrival can confirm a shelter across a mapped major road | CURRENT relaxed arrival to two accurate vicinity fixes | Apply the same major-road barrier to initial and ongoing arrival checks. Continue to label facility entry unverified. |
| A transient location exception ends route progress; initial confidence is immediately erased | Inherited Camera-page behavior, interacting with CURRENT's strict visibility gates | Retry location polling, preserve route state, apply initial fusion before window publication, and retain startup confidence. PDR (step-based position estimates) advances geometry only within 8 seconds and 8 m of trusted GPS. |
| Rejected/confirmation-pending reroutes use the same 45-second delay as successful reroutes | Inherited policy | Confirmation retries after 3 seconds; failed attempts retry after 10 seconds; successful routes retain 45 seconds. Consecutive reliable observations are still required. |
| Two different-origin detours can confirm each other based on length and algorithm alone | Inherited replacement policy | Require matching origin and destination geometry before confirming the second candidate. |
| Large graph parsing resumes on the UI thread | Inherited bootstrap, with CURRENT adding raw-road consumers | Run shared validation and graph creation on a background worker. |
| Missing/malformed server coordinates manufacture a shelter pin; parsing depends on device number format | Inherited shelter fetch behavior | Parse invariant decimal coordinates, reject unusable rows, validate cached coordinates, and preserve usable offline data when every server row is invalid. Unknown facilities are no longer presented as confirmed amenities. |
| Missing native bridge in all three ZIPs | Shared packaging defect; Android ignore rule excludes the required library | Include a rebuilt ARM64 `libnative_bridge.so` and narrowly allow that file through the ignore rule. |
| Packaging verifier rejects the equivalent Vulkan value `0x400003` | Inherited verifier defect, reproduced on the Release APK | Accept hexadecimal zero-padding normalization while retaining the required numeric version. |
| Unreachable untrusted-heading branches and obsolete arrival-radius comment | CURRENT stale code | Remove unreachable alternatives and correct the comment without relaxing heading gates. |

Batch metadata and the package, compatibility, and instrumentation scripts now
identify `ARCore-11`. Older batch-10 documents remain historical records.

## Validation

Completed against the isolated corrective checkout:

- Production Android Release build (`RescuArDiagnosticBuild=false`): **PASS,
  0 warnings, 0 errors**; ARM64 ahead-of-time compilation completed.
- Android Debug diagnostic build (`RescuArDiagnosticBuild=true`): **PASS,
  0 warnings, 0 errors**; conditional camera test paths compiled.
- Regression console checks: **35 PASS**. These execute the real intent and
  navigation policies, not mocked replacements. They cover callback order,
  late callbacks, geometry integrity, disconnected origin snapping, road
  barriers, arrival observations, reroute delays, detour consistency, and
  offline coordinate parsing. They do not execute Android native rendering.
- `Verify-ARCoreReleaseReadiness.ps1`: **PASS**.
- `Test-ARCorePackage.ps1` on the Release APK: **PASS**. Verified the application
  ID, required camera/AR/Vulkan declarations, production diagnostic flag, batch
  marker, and ARM64 native packaging.
- Native bridge: **ELF64/AArch64**, stripped, minimum load alignment **16384
  bytes**, expected exports/dependencies present. SHA-256:
  `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- No Android devices were connected. Initialization latency, camera return,
  permission transitions, and sustained native stability are **not yet
  device-validated**.

Reproduce the main checks from the repository root:

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -c Release
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release -f net9.0-android -p:RescuArDiagnosticBuild=false
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Debug -f net9.0-android -p:RescuArDiagnosticBuild=true
./build/Verify-ARCoreReleaseReadiness.ps1
```

Run these sequentially because the application and checks share core build
outputs. Package verification also needs the installed NDK's `llvm-readelf`
and Android's `apkanalyzer`; supply their paths to `Test-ARCorePackage.ps1`.

### Required device checks

1. Measure ten cold starts through the first camera frame; record the device,
   Android version, Google Play Services for AR version, and batch marker.
2. Repeat Home/app-switcher return and lock/unlock at least 20 times, including
   immediate returns and 30-second backgrounds. Check new camera timestamps,
   not just whether the process remains alive.
3. Switch Camera to another tab during startup and during surface restoration;
   return rapidly. Check that hidden Camera does not reactivate and that its
   next deliberate entry resumes.
4. Exercise camera permission denial/restoration, rotation where supported,
   route entry/exit, and surface recreation. Mere rotation does not prove that
   the native surface was recreated.
5. Run at least a 30-minute walk/thermal soak on the affected Samsung profiles
   with their production depth protection intact, plus another supported
   device. Collect native crash and graphics errors. Keep diagnostic depth
   override experiments separate from the production validation.
6. Test first offline launch, cached offline launch, and subsequent live shelter
   refresh. Confirm offline status wording and compare dense-route corners to
   the 2D map. Arrival across a mapped major road must remain unconfirmed.

## Applying the ZIP

The ZIP contains changed files at their repository-relative paths, including
the native bridge, regression checks, this review, and commit summary/body.
It contains no `.git`, build outputs, field logs, credentials, or full APK.
Back up or commit local edits first; overlaying a changed file replaces that
file completely. Merge overlapping edits manually if using the original
workspace. Do not delete unrelated files. Rebuild and run the device checks
before distributing the application.

## CURRENT versus SECOND review inventory

Paths below are the complete 34-path normalized difference inventory. Preserved
changes were inspected; their presence alone is not treated as a regression.

| Path | Disposition |
| --- | --- |
| `RescuAR-FieldLog.ps1` | Preserve field-log changes |
| `RescuAR.MAUI/Platforms/Android/EvergineViewHandler.Android.cs` | Correct surface recovery and synchronization |
| `RescuAR.MAUI/Platforms/Android/Services/ArCoreService.DeviceCompatibility.cs` | Preserve device-specific depth protection |
| `RescuAR.MAUI/Platforms/Android/Services/ArCoreService.Lifecycle.cs` | Correct retained pause/resume intent |
| `RescuAR.MAUI/Services/Navigation/ArHeadingAlignmentService.cs` | Preserve heading protection |
| `RescuAR.MAUI/ViewModels/Dashboard/DashboardViewModel.cs` | Preserve CURRENT dashboard behavior |
| `RescuAR.MAUI/ViewModels/Prepare/EvacuationCenterInfoViewModel.cs` | Restore offline fallback; validate coordinates |
| `RescuAR.MAUI/Views/Camera/CameraPage.Diagnostics.cs` | Preserve diagnostic behavior |
| `RescuAR.MAUI/Views/Camera/CameraPage.xaml` | Preserve camera UI and diagnostic controls |
| `RescuAR.MAUI/Views/Camera/CameraPage.xaml.cs` | Correct location, startup, arrival, and retry integration |
| `RescuAR.MAUI/Views/Dashboard/DashboardPage.xaml` | Preserve CURRENT dashboard layout |
| `RescuAR/AR/ARCameraSpatialController.cs` | Preserve spatial continuity protection |
| `RescuAR/AR/ARGuidanceConfidencePolicy.cs` | Remove unreachable branches; preserve gates |
| `RescuAR/AR/ARRouteBridge.cs` | Preserve route identity/geometry snapshots |
| `RescuAR/AR/ARRouteGeometrySanitizer.cs` | Reject invalid complete geometry; preserve corners |
| `RescuAR/AR/ARRouteRenderer.cs` | Correct capacity, span threshold, and diagnostic-pool interaction |
| `RescuAR/AR/ARRouteVisualPolicy.cs` | Preserve visibility/occlusion policy |
| `RescuAR/Navigation/Data/MajorRoadCrossingPolicy.cs` | Preserve major-road barrier checks |
| `RescuAR/Navigation/Data/NavigationDataBootstrap.cs` | Move heavy bootstrap work off UI thread |
| `RescuAR/Navigation/Data/PedestrianRoadFilter.cs` | Preserve pedestrian access restrictions |
| `RescuAR/Navigation/Data/RoadGraphBuilder.cs` | Preserve mapped-road graph restrictions |
| `RescuAR/Navigation/Guidance/PedestrianTurnGuidanceService.cs` | Preserve turn guidance |
| `RescuAR/Navigation/Guidance/SafeZoneConfirmationService.cs` | Add barrier guard to vicinity confirmation |
| `RescuAR/Navigation/Guidance/SafeZoneFacilityCatalog.cs` | Correct stale radius comment |
| `RescuAR/Navigation/Models/RoadGraph.cs` | Preserve shared barrier API |
| `RescuAR/Navigation/Progress/GpsPdrFusionPolicy.cs` | Preserve confidence and route-identity policy |
| `RescuAR/Navigation/Progress/RouteCorridorPolicy.cs` | Preserve local corridor restrictions |
| `RescuAR/Navigation/Progress/RouteProgressTracker.cs` | Preserve matching/trust rules |
| `RescuAR/Navigation/Progress/RouteReplacementPolicy.cs` | Strengthen candidate endpoint consistency |
| `RescuAR/Navigation/Projection/LocalArNavigationPolicy.cs` | Preserve bounded window sizes |
| `RescuAR/Navigation/Projection/NearbyRoadLineProjector.cs` | Preserve raw-road diagnostic projection |
| `RescuAR/Navigation/Routing/AStarRoutingService.cs` | Correct disconnected origin selection |
| `RescuAR/Navigation/Routing/HybridRoutingService.cs` | Preserve routing fallback policy |
| `RescuAR/Navigation/Routing/MLDARIntegrationService.cs` | Fit complete source-preserving local windows |

## Conventional Commit

```text
fix(android): retain AR sessions and correct navigation regressions

Pause retained ARCore sessions during temporary Activity and surface loss.
Resume only when Camera is requested and both prerequisites are ready.
Serialize graphics callbacks, transfer importer ownership to recreated
surface workers, and avoid redundant swap-chain rebuilds.

Correct route geometry capacity, disconnected origin selection, arrival
barriers, startup confidence, location retries, and reroute confirmation.
Restore honest offline shelter locations and reject malformed coordinates.
Include the required native bridge and update batch-11 package checks.

Validation: Android Release/diagnostic builds, 35 regression checks, compatibility
declarations, and Release package/native-bridge verification passed.
Android device lifecycle and native-crash soak validation remain pending.
```
