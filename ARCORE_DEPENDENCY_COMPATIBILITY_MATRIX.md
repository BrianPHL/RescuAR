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
| Samsung SM-A546E / Android API 34 | Normal builds retain the plane-only native-crash quarantine. The October 19 diagnostic candidate revalidates supported automatic Depth; repeat lifecycle, alignment, arrival, and realistic flood composition. See the [October 19 review](AR_FIELD_VALIDATION_20261019.md) and [batch 14 field validation](ARCORE_BATCH_14_FIELD_VALIDATION.md). |
| Samsung SM-A546E / API 36 and SM-A156E / API 36+ | Normal builds retain the native-crash quarantine; diagnostic field candidates revalidate Depth unless explicitly disabled. A successful build is not provider/crash acceptance. See the [October 19 review](AR_FIELD_VALIDATION_20261019.md). |
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
