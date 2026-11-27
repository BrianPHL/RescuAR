# AR camera lifecycle safeguards

Recorded on November 27, 2026 (Asia/Manila), starting from `dev` at `444a57e`.

## Changes and purpose

- Explicitly close acquired Android Depth and diagnostic camera images before
  releasing their Java wrappers. Return each Depth image once its bytes have
  moved to managed storage. [ARCore frame acquisition](https://developers.google.com/ar/reference/java/com/google/ar/core/Frame#acquireDepthImage16Bits())
- Publish Depth only when its native timestamp matches the captured camera pose.
  Match render generation, tracking epoch, world revision and zoom before reuse.
  Give route and flood readers a lease so a pooled buffer stays valid until the
  last reader releases it.
- Hide spatial overlays during tracking recovery and world-frame correction
  until the presented camera frame can use a pose from the new owner. Preserve
  camera passthrough while overlays requalify.
- Recover stalled or interrupted camera acquisition through the serialized ARCore
  lifecycle. Tolerate short gaps, pace retries, and offer 2D Map when the restart
  budget is exhausted. Fence pending buffers across pause and resume.
- Give shutdown and surface disconnect one cleanup result per Vulkan generation.
  Stop draws before handing permanent detach to a worker. Release Vulkan camera
  resources after GPU completion or confirmed device loss, and gate replacement
  graphics startup on cleanup. Temporary surface recreation retains its device.
  [Vulkan device-loss rules](https://docs.vulkan.org/spec/latest/chapters/devsandqueues.html#devsandqueues-lost-device)
- Update the [routing contract](../../RescuAR/Navigation/Routing/README.md) and
  [Android field procedure](../field-testing/README.md), and add host assertions
  for Depth ownership, recovery epochs, camera retry and teardown races. Existing
  Camera and 2D presentation surfaces provide recovery feedback.

## Verification

- Release host build passed with no warnings or errors. Focused suites passed
  `--ar-audit` (511), `--alignment-recovery` (946) and `--camera-lifecycle` (86).
  The suites share some assertions.
- The ARM64 Android Release build passed with `RescuArDiagnosticBuild=false`
  and existing Evergine content. It reported the existing nullable assignment
  warning in `SafetyCircleService.cs:274` and zero errors.
- The compiled Vulkan camera ownership and frame-boundary verifier passed 11
  checks against the Android assembly and the pinned Evergine engine.
- The ARCore release-readiness check passed. The documentation catalog was
  regenerated. The documentation verifier reported ten broken links in the
  existing tracked hotline backup and no failures in the changed guides.

## Follow-up

Use the [manual device matrix](../field-testing/README.md#manual-device-matrix)
to verify camera interruption, re-entry, tracking loss, and Depth on supported
hardware. Rapid turns still need observation because the retained mask uses its
original camera projection.
