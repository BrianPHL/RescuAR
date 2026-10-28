# Camera loading-cover flicker — 2026-10-28

## Evidence

The user supplied `RescuAR_FieldTest_20261028_123152.zip` and `xBhnmWxq.mp4`. The installed manifest identifies build `613ee05ccecd5cb49b8f5b3d196a40d07019eae0`. Log capture completion is confirmed; no fatal signal/exception/abort entries were found.

Offline video inspection confirms short “Preparing camera view…” cover flashes after the feed is already visible, including frames near 24.0 and 74.35 seconds. The video was sampled offline; no running application or device was controlled. No exact wall-clock synchronization between video frames and log lines is assumed.

Log lines 3397 and 3780 report unavailable/stale texture observations at 12:34:00.879 and 12:34:10.922, interleaved with coherent frames at lines 3644 and 3982. The session remains Running from 12:33:51.647 until its actual pause begins at 12:34:25.284.

## Correction

The startup UI rechecked the current rendering texture every refresh. The existing native pipeline can temporarily clear that bridge while conversion is pending; a missing texture at one instant does not mean startup has restarted.

CameraStartupState now remembers the first qualified texture for an active session and graphics context. The page checks that this first texture matches the current render generation. Once ready, transient texture gaps and AR/Flood switches do not show the startup cover again. Page exit, 2D mode, backgrounding, a real camera pause, session/context replacement, invalidation or a confirmed failure clears that readiness. Re-entry still waits for a fresh qualified frame, and failures remain visible.

This changes display state only. The 750 ms texture freshness threshold and all native camera, spatial, tracking, route and flood safety gates remain unchanged. The hidden ground-placeholder renderer, stable tab geometry and previous crash repairs are preserved.

## Validation

- 35 focused startup checks passed, including ten consecutive post-ready conversion gaps and lifecycle/generation/failure resets.
- 11 compiled camera ownership/resize-wiring checks and ARCore declarations passed.
- Release source and actual XAML compilation passed, 0 warnings/errors. Used DesignTimeBuild=false, MauiXamlCValidateOnly=false, RescuArDiagnosticBuild=false and unchanged existing core output.
- Exactly three existing application/test paths changed, identical in both checkouts. No APK was built; no merge, push, deployment, database operation or device/UI test was performed.

## Required device check

After the next user-built installation, open Camera and allow its first live feed to appear. Keep AR running and switch between AR and Flood: expect no brief startup-cover flashes. Switching to 2D or backgrounding and returning may show startup while the camera genuinely resumes. Confirm that real failures still show a message and report any mismatch. Device confirmation of this correction is pending.

## Commit

`fix(camera): keep startup cover dismissed after camera readiness`

- Integration worktree: `bee5e75`
- Build checkout: `309b034`
