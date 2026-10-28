# Session default and flood-tab crash repair — 2026-10-28

## Authentication

The sign-in checkbox and all session/email persistence reads defaulted Remember Me to Off. The user approved default On with an Off option, and explicitly requested a checked sign-in checkbox. The checkbox now starts checked when no choice is saved and has a short explanation. An existing saved Off value is respected; check it once at the next sign-in to enable persistence.

The capture does not establish a failure when Remember Me is checked: the user was unsure of its state, and the log contains no session-persistence diagnostics. Verified identity checks, invalid-session handling, secure storage and sign-out cleanup remain intact.

## Confirmed flood-tab crash

Evidence: `Logs/RescuAR_FieldTest_20261028_110715.zip`, `logcat-001.txt`. Its installed artifact manifest identifies source revision `c774243d98fd940384fe74e8c8a705cd7cc3a907`, Samsung SM-A546E, Android 14.

1. Line 3093, 11:08:50.885: FloodDepth selected.
2. Lines 3107–3133: camera viewport changes to 1080×1683; surface and geometry callbacks run.
3. Line 3164, 11:08:51.027: persistent Vulkan camera resources rebuild.
4. Lines 3165–3204: Scudo rejects invalid native-memory deallocation, SIGABRT; backtrace reaches Mali `vkDestroyImage`.

Source and the actual pinned Evergine library confirm conflicting ownership. `GetTextureFromNativePointer` creates an owning VKTexture, while `VulkanRgbaTarget` also destroys its image. The user approved `VKTexture.FromVulkanImage(..., ownResources: false)`, leaving the existing target as the single image/memory owner. Both normal disposal and partial-creation cleanup now avoid an owning wrapper. The same defect exists in dev; this is a documented exception to the prior native-source preservation checkpoint.

## Verification

- 38 focused session-persistence, identity and startup checks passed.
- 8 checks inspect the real compiled app and pinned engine: the wrapper declines ownership, the engine honors it and skips image/memory destruction, and app-owned cleanup remains present. These are binary contract checks; they do not invoke a GPU driver.
- Release source and XAML compilation passed, 0 warnings/errors, with `DesignTimeBuild=false`, `MauiXamlCValidateOnly=false`, `RescuArDiagnosticBuild=false` and unchanged existing core project output.
- ARCore manifest/dependency declarations passed. No dependency pins changed.
- Initial restricted build attempts hit local NuGet lock access and Evergine file-access failures. Running with normal local-file permissions resolved these; existing cached packages were used, with no new dependencies installed.
- Both checkouts contain identical changed source. No core routing, flood algorithm, native bridge binary, Admin, map presentation, merge, push or database operation changed. No APK was built.

## Required device retest

1. Sign in with Remember Me checked, close/force-stop and reopen. Expected: valid remembered session restores app access. Repeat with it unchecked; expected: sign-in is required after restart. Previously saved Off stays Off until changed.
2. Repeatedly switch AR → Flood and 2D → Flood, then rotate the phone. Expected: no crash; camera and floor-qualified flood simulation continue. Actual driver/device outcome remains pending; report the result before further behavior-dependent repairs.

## Commits

| Change | Integration worktree | Build checkout |
| --- | --- | --- |
| Default-checked Remember Me | 2234423 | 43f771d |
| Vulkan camera-image ownership | 8746146 | e79fb19 |
