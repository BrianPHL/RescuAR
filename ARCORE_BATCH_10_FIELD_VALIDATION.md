# ARCore Batch 10 Field Validation

## Scope

This corrective revision removes the standalone automated test project and replaces its release claim with explicit build, package, and physical-device validation. It also repairs the missing dependency matrix, package-profile verification, evidence schema, and overstated ADB scenario reporting.

Finding 38 remains excluded. Do not change `net9.0-android`, MAUI `9.0.120`, Evergine `2025.10.21.3204`, or Vapolia.Google.ARCore `1.47.1`.

## Required removals

Delete the complete `RescuAR.Tests/` directory before building. Confirm `RescuAR.MAUI.sln` and `.github/workflows/arcore-native-bridge.yml` contain no `RescuAR.Tests` reference.

## 1. Clean build

From the repository root:

```powershell
dotnet restore RescuAR.MAUI.sln
dotnet build RescuAR/RescuAR.csproj -c Release
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release -f net9.0-android -r android-arm64
```

Pass criteria:

- Both projects build with zero errors.
- The MAUI target is `net9.0-android` and the runtime is `android-arm64`.
- No test project is restored or built.

## 2. Release-readiness validation

```powershell
./build/Verify-ARCoreReleaseReadiness.ps1
```

Pass criteria:

- The compatibility matrix is found.
- Manifest, ABI, MAUI, Evergine, ARCore, NDK, and CMake declarations match the locked profile.
- The validation profile is `ARCORE_MANUAL_FIELD_VALIDATION_V1`.

## 3. Release package validation

Publish an ARM64 APK, then run:

```powershell
./build/Test-ARCorePackage.ps1 `
  -ArtifactPath "<absolute-path-to-release-apk>" `
  -ExpectedAbi "arm64-v8a" `
  -ExpectedPackage "com.rescuar.app" `
  -ExpectedDiagnosticBuild "false" `
  -OutputDirectory ".\artifacts\package-evidence"
```

Pass criteria:

- `libnative_bridge.so` exists for ARM64 and passes ELF, export, dependency, alignment, and stripping checks.
- The APK contains `assets/rescuar-build-profile.txt`.
- The packaged profile reports `diagnosticBuild=false`, `correctiveBatch=ARCore-10`, and `validationProfile=ARCORE_MANUAL_FIELD_VALIDATION_V1`.
- Camera, AR camera, and Vulkan requirements are present in the merged manifest.

## 4. Android lifecycle exercise

```powershell
./build/Invoke-ARCoreAndroidInstrumentation.ps1 `
  -ArtifactPath "<absolute-path-to-release-apk>" `
  -Cycles 10 `
  -ExerciseCameraPermission
```

This script validates only the scenarios it actually performs: cold start, Camera-page entry/exit, background/foreground, lock/unlock, rotation/configuration change, and process continuity. It does not claim proof of graphics-context recreation or graceful shutdown.

Pass criteria:

- No process loss, fatal exception, ANR, native-load failure, Vulkan device loss, or buffer-queue teardown error.
- `BUILD_MANIFEST batch=ARCore-10` is present.
- `validationProfile=ARCORE_MANUAL_FIELD_VALIDATION_V1` is present.
- `ARCORE_NATIVE_BRIDGE_SELF_TEST result=PASS` is present.

## 5. Manual Camera/surface teardown test

On each test device, repeat 20 times:

1. Open Camera and wait for the physical camera background.
2. Confirm the cyan route and text guidance agree.
3. Navigate away from Camera and wait two seconds.
4. Return to Camera.
5. Rotate portrait to landscape and back.
6. Background and restore the application.
7. Lock and unlock the device while Camera is active.

Pass criteria:

- Camera passthrough returns every time.
- No blank/black camera, frozen frame, stale cyan route, stale flood plane, or process disappearance.
- Logcat contains no `dequeueBuffer failed`, `queueBuffer failed`, `VK_ERROR_DEVICE_LOST`, fatal signal, or repeated `DllNotFoundException`.

## 6. Depth, ground, flood, navigation, and sensor validation

Run once with Depth disabled and once with Depth automatic:

- Ground probing remains bounded and pauses during poor tracking.
- Provisional and verified ground are visually distinguishable.
- Flood geometry clears after tracking loss, anchor loss, mode changes, and Camera exit.
- Route geometry does not pass through buildings and agrees with maneuver text.
- Distance decreases reasonably while walking.
- Heading is physically correct after rotation and background/foreground.
- PDR steps are accepted or rejected with recorded reasons.
- Weak GPS/tracking hides unsafe AR geometry rather than presenting stale guidance.

## 7. Evidence and closure

Record the APK SHA-256, native-library SHA-256, install method, device model, Android/API version, ABI, GPU, ARCore version, Depth mode, and logs. Use `ARCORE_EVIDENCE_BUNDLE_SCHEMA.json` and update every row in `ARCORE_FINAL_FINDING_VALIDATION_MATRIX.md`.

Finding 8 must remain **PARTIALLY RESOLVED** because the automated unit test suite was intentionally removed. Findings requiring physical-device evidence must remain **IMPLEMENTED — NEEDS FIELD VALIDATION** until their stated scenarios pass.
