# Flood-tab resize follow-up — 2026-10-28

## What the new capture proves

`Logs/RescuAR_FieldTest_20261028_114543.zip` identifies installed source revision `adfede614b5c033b2bf6fb7fbf04b2d88e389522`, including the earlier ownership fix, on Samsung SM-A546E / Android 14. Start/collection APK hashes agree. Capture completion markers are present.

In `logcat-001.txt`:

1. Lines 2120–2121, 11:47:30.879: enter Flood. Camera resources rebuild successfully.
2. Lines 2440–2441, 11:47:33.766: return to AR.
3. Lines 2502–2544: surface resizes to 1080×1650, camera resources rebuild, first conversion submits.
4. Line 2546, 11:47:33.891: SIGSEGV at address 0x2. Lines 2577–2579 identify Mali `begin_renderpass` / `vkCmdBeginRenderPass`.

This capture gets past the earlier cleanup abort. It has a different crash signature and confirms that the ownership repair alone was insufficient. The subsequent ARCore rectifier error appears after the fatal signal; it does not establish the first cause of this crash.

## Identified resize defect and approved changes

The Android handler resized the swapchain directly. The pinned Evergine Display independently queued the same surface event and resized again in `GraphicsPresenter.Prepare`, inside `Application.DrawFrame`. The handler's direct resize also did not update Display.FrameBuffer. A native-free test using the actual installed Display reproduces two buffer replacements for one event and the stale Display reference.

The user approved both changes:

- Reserve 38 layout units for the tab controls. Hiding the 38-unit speaker button in Flood no longer shrinks the bar and expands the camera viewport by 33 device pixels on this phone.
- Use a frame-start Display adapter. It snapshots real surface geometry, forwards existing input/focus, drains graphics work only for an actual change, and invokes the engine's normal resize/publication path before UpdateFrame. Its later Prepare override cannot perform a second resize. Changes arriving during a frame wait until the next frame start. The Android handler retains ownership of the native surface.

The source mechanism matches the captured timing; the native GPU crash itself was not reproduced locally. The previous non-owning camera-image wrapper remains in place. No routing, GPS, arrival, flood algorithm, bridge binary, dependency pin, authentication, Admin or map-data changes were made.

## Validation

- 20 focused checks passed using the real pinned engine Display and native-free Surface/SwapChain implementations. Cases include the old duplicate resize, one frame-start publication, mid-frame changes, coalescing, zero sizes, recreation, focus/input forwarding, surface ownership/disposal and failed graphics quiescence.
- 11 compiled checks passed: previous image-ownership checks plus actual Android handler ordering and single-owner wiring. No GPU driver is invoked by these checks.
- Release source and actual XAML compilation passed: 0 warnings and 0 errors. Build used DesignTimeBuild=false, MauiXamlCValidateOnly=false, RescuArDiagnosticBuild=false and unchanged existing core output.
- ARCore declarations passed. Seven changed application/check/test files match between both checkouts. Original dev/Athena branches remain untouched.
- No application/device/browser interaction, APK build, merge, push, deployment or database operation was performed.

## Device retest received

The user reported “Fixed” and supplied `RescuAR_FieldTest_20261028_120931.zip`. Its installed manifest identifies `4256adaed82aa3a326bfcce645bed5c1be6820ec`; capture completion is confirmed, repeated AR/Flood/2D transitions are present, and no fatal signal/exception/abort entries were found. This supports the reported fix for the captured scenario; it is not general device/field acceptance. The later requested cone/loading presentation changes are documented in [camera presentation](2026-10-28-camera-presentation.md) and still need their own UI check.

## Original device retest instructions

After the next user-built installation, repeat AR → Flood → AR and 2D → Flood several times. Rotate the phone and background/resume. Expected: no crash; the camera stays live, correctly sized and aligned, and floor-qualified flood simulation remains usable. Collect and report actual results before further behavior-dependent repairs. Successful local checks do not establish device resolution.

## Commit

`fix(camera): apply surface resizing once before frame preparation`

- Integration worktree: `119d35d`
- Build checkout: `a482bef`
