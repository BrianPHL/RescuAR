# ARCore Batch 10 Revision Manifest

## Baseline

- Source: `RescuAR(10).zip`
- Scope: remove the standalone automated test suite and correct the remaining release-validation defects.
- Finding 38: excluded; compatibility stack unchanged.

## Added files

- `ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md`
- `ARCORE_BATCH_10_FIELD_VALIDATION.md`
- `ARCORE_BATCH_10_MANIFEST.md`
- `ARCORE_FINAL_FINDING_VALIDATION_MATRIX.md`

## Modified files

- `.github/workflows/arcore-native-bridge.yml`
- `ARCORE_EVIDENCE_BUNDLE_SCHEMA.json`
- `RescuAR.MAUI.sln`
- `RescuAR.MAUI/RescuAR.MAUI.csproj`
- `RescuAR.MAUI/Platforms/Android/Services/AndroidBuildManifestReporter.cs`
- `build/Invoke-ARCoreAndroidInstrumentation.ps1`
- `build/Test-ARCorePackage.ps1`
- `build/Verify-ARCoreReleaseReadiness.ps1`

## Deleted files

- `RescuAR.Tests/ArCameraImportFailurePolicyTests.cs`
- `RescuAR.Tests/ArCoreLifecyclePolicyTests.cs`
- `RescuAR.Tests/BridgeSafetyTests.cs`
- `RescuAR.Tests/GuidanceAndWorkloadPolicyTests.cs`
- `RescuAR.Tests/IdempotentReleaseLeaseTests.cs`
- `RescuAR.Tests/Properties/AssemblyInfo.cs`
- `RescuAR.Tests/RescuAR.Tests.csproj`

Delete the complete `RescuAR.Tests/` directory when applying this corrective package. ZIP extraction alone cannot remove files already present in the destination.

## Behavioral changes

- The solution and CI no longer restore or execute the standalone test project.
- Production safety-policy classes remain in the application because runtime code uses them.
- CI continues to rebuild and inspect the native bridge and final APK.
- The final APK contains a generated `rescuar-build-profile.txt` asset.
- Package validation compares the observed APK diagnostic profile against the expected production value.
- Build provenance now reports `ARCORE_MANUAL_FIELD_VALIDATION_V1`; it no longer claims an automated safety-test profile.
- The ADB script reports only scenarios it actually performs.
- The evidence schema requires exactly one keyed closure record for every Finding ID 1–54.

## Locked compatibility profile

- `net9.0-android`
- MAUI `9.0.120`
- Evergine `2025.10.21.3204`
- Evergine.LibBulletc.Natives `2025.8.29.27`
- Vapolia.Google.ARCore `1.47.1`
- `android-arm64`
- NDK `28.2.13676358`
- CMake `3.22.1`
