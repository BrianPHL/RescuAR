# Camera presentation — 2026-10-28

The user confirmed that the resize crash is fixed. The new completed capture `RescuAR_FieldTest_20261028_120931.zip` identifies the latest installed build `4256ada`, contains repeated AR/Flood/2D transitions and has no fatal entries. That evidence applies to the captured scenario, before these presentation changes.

## Requested changes

- Hide the ground-anchor placeholder mesh only: disable the capsule entity's MeshRenderer in MyScene.CreateScene, including diagnostic builds. Retain its transform and source material because flood placement and route rendering still use them.
- Cover the initial skybox with an opaque startup overlay while retaining the running native surface beneath it. Clear the cover when the existing camera bridge supplies a fresh current-generation texture and the session is running. Ground tracking, route calculation and heading acquisition do not delay the camera feed.
- Known faults show their message without a busy spinner. After 20 seconds, slow startup shows a delayed status with a 2D-map alternative. The cover still clears if the camera becomes ready later. Leaving the page, backgrounding or choosing 2D hides the cover and stops its startup timer; active AR/Flood entry/resume refreshes it through the existing display hooks.

## Validation and preservation

- 12 startup presentation checks, 18 existing ground/flood checks and 3 existing startup-scene checks passed.
- 11 compiled ownership/resize-wiring checks and ARCore declarations passed.
- Release compilation rebuilt the changed core scene and ran actual XAML compilation: 0 warnings/errors. Used DesignTimeBuild=false, MauiXamlCValidateOnly=false and RescuArDiagnosticBuild=false.
- Eight focused application/test paths match between checkouts. AR spatial/ground/flood/routing algorithms, serialized assets, fixed native resizing, dependency pins, authentication and map data are unchanged.
- No APK build, merge, push, deployment, database operation, running-app interaction or device UI check was performed.

## Device UI check

On the next user-built installation, enter Camera from a cold launch: expect the loading status instead of the skybox, then the live feed. Check that the cone stays hidden after floor detection while the route and selected flood depth remain usable. Recheck 2D/AR/Flood switching and background/resume; report any mismatch. Permission denial or slow startup should show a clear status, with the main 2D tab still available.

## Commit

`feat(camera): add startup status and hide ground placeholder`

- Integration worktree: `d778c99`
- Build checkout: `6c4e513`
