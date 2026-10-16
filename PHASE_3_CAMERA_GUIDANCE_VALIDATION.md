# Phase 3: Camera guidance integration

Branch: `migration/athena-modules`. Baseline: `988815e` (Phase 2). Implementation date: 2026-10-07.

## Implemented behavior

- Replace the camera's 2D placeholder with a Mapsui map containing embedded road context, listed evacuation centers, the selected pedestrian route, route start, destination, and an actual device-location marker. Gray roads/centers supply context; they do not certify pedestrian access or facility safety.
- Use the existing `NavigationDataBootstrap` road datasets. This map has no online tile layer or tile-service dependency. The additional custom MBTiles/building/park/water assets remain in Phase 4.
- Preserve the existing `HybridRoutingService`/`MLDARIntegrationService` provider boundary, shared access/direction/cost/endpoint policies, road-access checks, GPS/PDR fusion, off-route/hazard rerouting, replacement decisions, and arrival confirmation.
- Allow route startup, foreground GPS progress, geographic guidance, and accepted reroutes while ARCore is paused in 2D mode. PDR and AR projection remain dependent on their original camera requirements. No yaw-zero AR fallback is introduced.
- Retain route geometry and progress when switching modes. Invalidate the old AR display window on map entry, then require valid session heading and fresh road-entry evidence before displaying a rebuilt AR window. Map-created routes use the same retained-route path when the camera resumes.
- Label unavailable and last-known locations explicitly. Use real coordinates only; a regional default viewport is never a user marker. Suppress live instructions and speech without fresh usable GPS, accepted route confidence, and road-entry evidence.
- Add fit/retry controls and share stop-navigation and destination-selection actions with the camera module. Low-light fallback opens this map directly. Permission denial is visible and requires explicit retry within the current page visit.
- Add a directions sheet accessible from the turn card and the Directions button. It uses dev's pedestrian turn classifier, combines polyline vertices into maneuvers, identifies the selected destination, and displays retained-route remaining distance. Directions are estimates from geometry, not street-level maneuver metadata or proof of arrival.
- Add opt-in voice guidance with a persisted device preference (`NavigationVoiceEnabled`). The same mute control works in AR and 2D views. Meaningful instruction changes and near-turn thresholds are spoken; each changed meter does not trigger speech.
- Serialize speech requests and cancel obsolete announcements on mute, mode/page changes, destination reset, rerouting, or emergency overlays. Missing/failed speech engines display a written-directions fallback. Offline audible guidance requires an installed speech voice; device behavior remains to be verified.
- Keep one declaration for each camera event handler, including the existing zoom/flashlight handlers. Do not import the custom branch's overlapping overloads or its separate `RescuAR.App` project.

## Automated validation

| Check | Result |
| --- | --- |
| Android solution Debug build | Passed; zero warnings and zero errors |
| Camera guidance service checks | 37 passed |
| Existing routing/AR regression checks | 351 passed |
| ARCore manifest and pinned compatibility declarations | Passed |
| Signed Debug APK archive integrity | Passed; all ZIP entry CRCs valid |
| Packaged native bridge | ARM64 ELF; exact match to the checked-in bridge; only ARM64 native libraries packaged |
| Merged manifest speech-engine discovery | `android.intent.action.TTS_SERVICE` query present |
| Camera XAML/event wiring inspection | XML parses; all 26 referenced handler names have one declaration |
| Whitespace validation | `git diff --check` passed |
| Connected device inventory | No Android device connected; manual flows pending |

The focused checks execute the production directions, voice coordination, and offline-map data services. They cover short and dense routes, left/right turns, repeated vertices, malformed geometry, speech deduplication, meaningful distance thresholds, cancellation, unavailable-engine recovery, serialized announcements, cached-map cancellation, and offline routing with an online provider that rejects every attempted call.

These checks do not establish successful MAUI touch/layout behavior, audible Android speech, GPS reception, camera pause/resume, or field navigation. The NuGet vulnerability metadata request was unavailable during initial test-project restore; dependencies resolved from the existing cache. Dependency pins were not changed.

The sandboxed Android packaging attempt failed with closed worker pipes and left a generated resource archive invalid. Removing only `RescuAR.MAUI/obj/Debug` and rebuilding outside the sandbox resolved this. The final build used the existing checked-in native bridge. A pinned NDK/CMake native rebuild, `llvm-readelf` verification, the full CI package script, Release qualification, and instrumentation were not performed; the pinned native toolchain is unavailable locally.

## Reproduction

Run from the repository root:

```powershell
dotnet build RescuAR.MAUI.sln --no-restore --disable-build-servers -m:1 `
  -p:AndroidSdkDirectory='C:\Program Files (x86)\Android\android-sdk' `
  -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False

dotnet run --project tests/RescuAR.CameraGuidanceChecks/RescuAR.CameraGuidanceChecks.csproj `
  -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False

dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj

./build/Verify-ARCoreReleaseReadiness.ps1
```

Local evidence under ignored `artifacts/`: `phase3-android-build.log`, `phase3-guidance-checks.log`, `phase3-routing-checks.log`, `phase3-arcore-checks.log`, `phase3-package-checks.json`, and `RescuAR-phase-3-debug.apk`. The package JSON records APK/bridge hashes. Do not commit APKs, generated files, or helper scripts.

## Pending device acceptance

Use a supported ARM64 ARCore/Vulkan Android device and the existing package/device requirements in `.github/workflows/arcore-native-bridge.yml` and `ARCORE_BATCH_14_FIELD_VALIDATION.md`. Record device/build identifiers and evidence for these flows:

1. Select a verified center, start navigation, and open 2D Map. Confirm the selected target, route, actual location, and distance agree. Pan/zoom and confirm GPS refresh preserves the viewport; Fit route should restore the route view.
2. Disable Wi-Fi/mobile data before startup. Obtain a fresh GPS fix and select a destination connected in the embedded graph. Confirm road context, offline route, written directions, and progress work without downloading tiles. Disconnected destinations must report unavailable routing instead of manufacturing a connector.
3. Disable connectivity during an online route. Confirm the existing failover/replacement policy remains active, the displayed route changes only after an accepted replacement, and the directions sheet reflects that replacement. No AR tracking should be needed for acceptance in 2D mode.
4. Enable voice with an installed offline voice, walk a route with turns, and confirm announcements agree with the displayed mode's instructions. Verify mute is immediate, meter updates do not chatter, and near-turn cues remain audible. Repeat with connectivity disabled and with a speech engine unavailable.
5. Switch repeatedly between AR and 2D. Confirm camera/frame work pauses in 2D, route progress continues through GPS, and return to AR requires valid heading and new road-entry evidence. The old AR window must not reappear at its previous position.
6. Leave the Camera tab during route startup, map loading, or speech; return and switch destinations quickly. Confirm cancelled work cannot restore old markers, instructions, route geometry, or speech.
7. Deny location permission, turn location services off, or lose a usable fix. Confirm accurate unavailable/last-known labels and held live guidance. Verify Retry and recovery after restoring permission/location services.
8. Exercise stop-navigation, rerouting, emergency overlays, and confirmed arrival. Obsolete speech must stop and existing arrival/access checks must remain authoritative.
9. Check sheet scrolling, tap handlers, zoom, flashlight, banner spacing, and orientation changes on a phone. Capture screenshots for the 2D map, directions sheet, and voice controls.

## Suggested commit message

```text
feat(camera): integrate offline map and voice guidance

Replace the camera's 2D placeholder with embedded road context and the
selected route. Reuse existing routing, GPS progress, rerouting, and
arrival policies while ARCore is paused, and rebuild trusted AR windows
when returning to the camera.

Add geometry-based directions, persisted voice controls, meaningful
announcement thresholds, and cancellation of obsolete speech. Preserve
camera handlers, strict native requirements, and dependency pins.

Validation: Android build, 37 guidance checks, 351 routing/AR regressions,
ARCore readiness, and APK integrity/native identity checks passed.
Physical Android and connectivity-disabled device acceptance remain pending.
```
