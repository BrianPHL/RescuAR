# RescuAR ARCore Dependency Compatibility Matrix

## Validated production profile

| Layer | Locked value | Change policy |
|---|---|---|
| Target framework | `net9.0-android` | Do not change without a separate full-stack validation batch. |
| .NET MAUI packages | `9.0.120` | Keep aligned with the installed .NET 9 MAUI workload. |
| Evergine | `2025.10.21.3204` | Upgrade only with Vulkan camera, lifecycle, and teardown retesting. |
| Evergine.LibBulletc.Natives | `2025.8.29.27` | Keep pinned with the validated Evergine profile. |
| Vapolia.Google.ARCore | `1.47.1` | Upgrade only after camera, Depth, tracking, and JNI regression testing. |
| Android runtime | `android-arm64` | The custom native camera bridge currently supports ARM64 only. |
| Minimum Android API | `26` | Must remain consistent with the project and native toolchain. |
| Android NDK | `28.2.13676358` | CI/native rebuild input; change only with ELF and device validation. |
| CMake | `3.22.1` | CI/native rebuild input; change only with the NDK profile. |
| Store capability policy | `AR Required` | Camera, AR camera, Vulkan 1.0, and Vulkan level 0 are required. |

## Supported validation targets

| Target | Required checks |
|---|---|
| Primary development phone | Build, install, native self-test, camera passthrough, Depth, route, flood, and repeated Camera-page entry/exit. |
| Samsung SM-A156E | Repeat the full test with Depth off and Depth automatic; verify tracking, native loading, JNI warnings, and teardown. |
| Additional ARM64 ARCore/Vulkan device | Repeat the full test on a different GPU/vendor when available. |

## Finding 38 decision

Finding 38 remains **STILL OPEN — EXCLUDED BY OWNER DECISION**. The production target must remain on the compatibility profile above because the Vapolia ARCore binding and Evergine MAUI/Vulkan integration have only been validated together on this stack. Framework migration requires a separate, authorized compatibility batch.

## Change-control rule

Change only one dependency layer at a time. Record the APK hash, native-library hash, build configuration, tool versions, device/GPU, Depth mode, and field result before accepting the candidate profile.
