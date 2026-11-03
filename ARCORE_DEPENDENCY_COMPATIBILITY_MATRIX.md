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

Current policy reviewed on 2026-11-03. Diagnostic logging alone does not enable
Depth on a quarantined device. `ArCoreService.DeviceCompatibility.cs` passes an
explicit `rescuar.arcore.force_depth_on` launch flag into the compatibility policy;
`force_depth_off` takes precedence when both flags are set. Route-depth occlusion
is a separate opt-in flag. Use the [current field procedure](docs/field-testing/README.md).

| Target | Required checks |
|---|---|
| Primary development phone | Build, install, native self-test, camera passthrough, Depth, route, flood, and repeated Camera-page entry/exit. |
| Samsung SM-A156E | Repeat the full test with Depth off and Depth automatic; verify tracking, native loading, JNI warnings, and teardown. |
| Samsung SM-A546E / Android API 34 | Production and diagnostic baselines retain the plane-only native-crash quarantine. A separate controlled diagnostic test may explicitly request Depth; confirm the logged configuration and repeat lifecycle, alignment, arrival and flood checks. The October 19 and batch 14 reports are historical evidence. |
| Samsung SM-A546E / API 36+ and SM-A156E / API 36+ | Production and diagnostic baselines retain the native-crash quarantine. Depth revalidation requires the explicit diagnostic force-on flag and device evidence; a successful build is not crash acceptance. |
| Additional ARM64 ARCore/Vulkan device | Repeat the full test on a different GPU/vendor when available. |

## Finding 38 decision

Finding 38 remains **STILL OPEN — EXCLUDED BY OWNER DECISION**. The production target must remain on the compatibility profile above because the Vapolia ARCore binding and Evergine MAUI/Vulkan integration have only been validated together on this stack. Framework migration requires a separate, authorized compatibility batch.

## Change-control rule

The 2026-10-29 crash candidate selects `MONO_GC_PARAMS=bridge-implementation=new`
through an AndroidEnvironment file. Exact-build symbols for Mono 9.0.20
(`6bceca0e711d8fb06f6ba715055d597afce5c4b6`) placed the field SIGSEGV in
`is_opaque_object` / `sgen-tarjan-bridge.c`. The pinned runtime supports `new`
as an alternative to its default Tarjan bridge. No framework, MAUI, ARCore or
native-bridge version changes. This is a mitigation requiring crash and
responsiveness checks on the physical devices; source/build validation does
not establish that the underlying memory fault is eliminated.

Change only one dependency layer at a time. Record the APK hash, native-library hash, build configuration, tool versions, device/GPU, Depth mode, and field result before accepting the candidate profile.
