# RescuAR ARCore Final Finding Validation Matrix

Statuses in this matrix are deliberately conservative. Code implementation does not become `RESOLVED — VERIFIED` until the required build and physical-device evidence is attached.

| ID | Batch | Current status | Primary implementation evidence | Required closure evidence |
|---:|---:|---|---|---|
| 1 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | `ArCoreService.Lifecycle.cs`, `IArCoreService.cs` | Repeated single-owner start/pause/shutdown trace. |
| 2 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | `ArCoreService.Lifecycle.cs` | Idempotent shutdown with no retained session, loop, anchor, or importer. |
| 3 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Vulkan importer/command-context teardown files | Twenty Camera exit/re-entry cycles with no Vulkan/surface error. |
| 4 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Flood, ground, and Depth bridges | Flood clears on pause, tracking loss, anchor loss, mode change, and exit. |
| 5 | 8 | IMPLEMENTED — NEEDS FIELD VALIDATION | Diagnostic build policy and Camera diagnostics split | Release APK contains no production route override. |
| 6 | 8 | IMPLEMENTED — NEEDS FIELD VALIDATION | Conditional diagnostic compilation | Release build exposes no developer-only Camera action. |
| 7 | 9 | IMPLEMENTED — NEEDS FIELD VALIDATION | `AndroidManifest.xml`, compatibility verifier | Store/device eligibility matches AR Required policy. |
| 8 | 10 | PARTIALLY RESOLVED | Package and ADB validation scripts retained; standalone unit suite removed | Manual/device evidence plus future authorized automated coverage if required. |
| 9 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Bounded asynchronous lifecycle operations | No lifecycle stall or ANR during interruption testing. |
| 10 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Request generation and idempotent transitions | Duplicate page/activity events do not duplicate session work. |
| 11 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Generation checks and bounded gates | No stale queued work or unbounded wait under rapid transitions. |
| 12 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Activity revalidation and lifecycle ownership | Rotation/background/install return uses the current Activity. |
| 13 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Install-pending lifecycle flow | Install/return resumes through a fresh availability check. |
| 14 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Versioned capability snapshot | Capability version changes after resume/install/context replacement. |
| 15 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Typed lifecycle failure contracts | Unsupported, install, camera, native, renderer, and timeout cases classify correctly. |
| 16 | 1 | IMPLEMENTED — NEEDS FIELD VALIDATION | Explicit UI/session/draw-thread boundaries | No thread-affinity exception during lifecycle stress. |
| 17 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Render-generation/context ownership | Old context callbacks are rejected after replacement. |
| 18 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Explicit Vulkan/native disposal | No finalizer-dependent release or leaked hardware buffer. |
| 19 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Terminal camera pipeline failure state | Native/import failure shows a clear fail-closed state without retry storm. |
| 20 | 4 | IMPLEMENTED — NEEDS FIELD VALIDATION | YCbCr format and camera-frame metadata | Correct color, range, orientation, and UV mapping on the device matrix. |
| 21 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Camera importer boundary | Evergine/Vulkan compatibility confirmed on the locked stack. |
| 22 | 4 | IMPLEMENTED — NEEDS FIELD VALIDATION | Camera configuration policy | Selected resolution/FPS is logged and stable on each device. |
| 23 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Versioned display geometry/frame metadata | Rotation never combines mismatched frame and geometry generations. |
| 24 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | Tracking-state bridge | Active tracking failures are separated from lifecycle pauses. |
| 25 | 4 | IMPLEMENTED — NEEDS FIELD VALIDATION | Frame coherence policy | Camera, pose, Depth, geometry, and render timestamps remain coherent. |
| 26 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Ground probe policy | Central valid candidates accepted; edge/invalid candidates rejected. |
| 27 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Mandatory ground trust state | Provisional state is labeled and never silently promoted. |
| 28 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Central ground/recovery state | Route and flood make the same decision during anchor recovery. |
| 29 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Bounded ground probing/backoff | Probe counts and elapsed time stay within policy limits. |
| 30 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Reused Depth/ground buffers | Allocation and long-session memory measurements remain bounded. |
| 31 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Import failure budget/circuit breaker | Failure becomes terminal once; no per-frame exception storm. |
| 32 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Power/thermal workload policy | Warm/critical tiers reduce work and recover without oscillation. |
| 33 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | Shared sensor lease manager | Multiple consumers release sensors exactly once at final lease release. |
| 34 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | Heading alignment service | Physical heading error is acceptable across rotations/devices. |
| 35 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | PDR policy and diagnostics | Multi-user/device walk records accepted and rejected steps. |
| 36 | 7 | IMPLEMENTED — NEEDS FIELD VALIDATION | Route geometry sanitization/render policy | Long/complex routes are not silently truncated or routed through buildings. |
| 37 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Mandatory render-generation tokens | Previous-session static state cannot reach a new Camera session. |
| 38 | 9 | STILL OPEN | Explicitly excluded; pinned compatibility stack retained | Separate authorized framework migration and full AR/Vulkan validation. |
| 39 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | CI native rebuild with pinned NDK/CMake | Rebuilt native hash and package result recorded by CI. |
| 40 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | ELF/export/dependency/provenance verifier | Release `.so` passes alignment, export, dependency, and stripping checks. |
| 41 | 9 | IMPLEMENTED — NEEDS FIELD VALIDATION | `ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md` | Locked stack passes controlled build and device regression run. |
| 42 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Startup build manifest and package profile asset | Field archive contains commit, APK/native hashes, versions, ABI, and flags. |
| 43 | 8 | IMPLEMENTED — NEEDS FIELD VALIDATION | Diagnostic privacy policy | Exported logs contain no precise unauthorized location/route data. |
| 44 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Pose freshness and generation rejection | Stale pose cannot render after pause, expiry, session change, or teardown. |
| 45 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Flood generation/teardown invalidation | Draw thread acknowledges flood clearing in every invalidation path. |
| 46 | 8 | IMPLEMENTED — NEEDS FIELD VALIDATION | Separate conditional Camera diagnostics file | Production Camera path remains free of diagnostic UI/actions. |
| 47 | 8 | IMPLEMENTED — NEEDS FIELD VALIDATION | Canonical state terminology in code/metadata | UI and logs consistently use the approved lifecycle/trust terms. |
| 48 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Native packaging and startup self-test | Installed Samsung APK loads `libnative_bridge.so` successfully. |
| 49 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Startup self-test and terminal importer state | One failure log, safe frame release, no repeated exceptions. |
| 50 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | Tracking segmentation diagnostics | Controlled Samsung Depth-off/on runs separate lifecycle and active instability. |
| 51 | 5 | IMPLEMENTED — NEEDS FIELD VALIDATION | Depth-aware probe budget/backoff | Unavailable Depth does not cause high-rate retries or edge errors. |
| 52 | 6 | IMPLEMENTED — NEEDS FIELD VALIDATION | JNI ownership diagnostics | Warning volume is eliminated or traced to a proven owner. |
| 53 | 2 | IMPLEMENTED — NEEDS FIELD VALIDATION | Ordered producer/GPU/importer/surface teardown | No buffer-queue errors or process loss during repeated exit. |
| 54 | 3 | IMPLEMENTED — NEEDS FIELD VALIDATION | Final APK native/package validation | Exact installed APK passes ABI, library, export, dependency, hash, and manifest checks. |

## Final release rule

Do not mark RescuAR AR release-ready while Finding 38 remains intentionally open unless the owner accepts that support risk, or while any blocker lacks physical-device evidence. Finding 8 remains partially resolved after removal of the automated test project.
