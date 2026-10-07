# Phase 4 — Settings, offline maps and presentation

Branch: `migration/athena-modules`. Builds on completed authentication/profile, Safety Circles and camera guidance phases. Changes stay in the existing `RescuAR.MAUI` project. Shared routing, hazard/access policy, endpoint limits, ARCore/Vulkan requirements and dependency pins are preserved.

## Implemented behavior

- Settings persist device preferences for advisory popups, advisory/siren sounds, confirmed-turn and advisory haptics, detailed offline maps, and navigation speech. Settings and both camera voice buttons use the same established voice preference. Reopening cached pages reloads preferences and visible map pages subscribe only while active.
- Optional advisory popups use a separate presentation event. Camera emergency events still receive new remote advisories when popups are disabled. Remote polling, its initial-snapshot behavior and the advisory feed are preserved. Disabling sounds stops active alarm audio; dashboard siren state reflects the preference and player state. Device volume still applies. The screen does not promise background push delivery or a silent-mode override.
- Camera and general map display the custom raster export and building/park/water context beneath dev roads, locations and selected routes. The general map no longer requires a Google tile connection for its background. Disabling details retains embedded roads and existing offline routing.
- Optional detailed-map loading uses a seekable local copy, guarded installation, SHA-256 validation and atomic replacement. PNG tiles are read directly from the indexed archive. There is no remote tile fallback, vector-PBF substitution or fabricated tile outside coverage. The removal command turns details off and deletes only that copy; account caches, avatars, contact data and pending circle messages are retained.
- Help provides searchable, expandable topics and opens the existing hotline directory. System information reads real app/device/connection/permission values and measures the local map copy. Registration and profile share factual privacy/use content, correcting permanent-location and third-party-sharing assurances that the circle/cloud implementation cannot substantiate. No new retention or account-deletion commitments are introduced.
- Native and managed startup use matching dark radar artwork. The managed decorative animation starts/stops with page visibility. Startup uses one initialization task, the installed app version and actual session status; the fixed two-second delay is removed. Existing session, resident-address, permission, login and onboarding decisions are preserved.

## Asset provenance and measured coverage

Source: the user-supplied `rescuar-android_v.16-my-custom-branch.zip`, specifically its existing MAUI `MAP_V2.mbtiles`, `BUILDINGS.geojson`, `PARKS.geojson` and `WATER.geojson` assets. No additional `RescuAR.App` project is imported.

`build/Convert-OfflineMapAssets.py` deterministically converts original TMS tile rows into XYZ entry names and preserves every PNG byte. It avoids a new SQLite dependency or changes to existing package pins.

- Indexed raster package: **93,661,024 bytes** (89.3 MiB), **3,555 PNG tiles**, zoom **13–18**.
- Bounds (west, south, east, north): `121.067945526206, 14.6085349357737, 121.135837600791, 14.6748035828093`.
- Context features: **1,058 buildings**, **219 parks**, **465 water features**. Coordinates were checked against Web Mercator latitude/longitude limits.
- Package SHA-256: `1168d7528af49cf0a6d3d6befdbbdfa1a8e3bc2f350ea6ce943928ed07e62b12`.
- Supplied metadata identifies a Maperitive export. Survey date and underlying cartography attribution were not supplied; the information screen reports this limitation. Static context is not current hazard, shelter availability or access evidence.
- The package is included in the app and needs an additional 89.3 MiB local copy when details are first opened. Removing the copy keeps the packaged asset, allowing offline reinstallation. The existing unused vector `2D_MAP.mbtiles` remains excluded.

## Local validation

Run from the repository root:

```powershell
dotnet run --project tests/RescuAR.SettingsChecks/RescuAR.SettingsChecks.csproj -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False -p:NuGetAudit=false
dotnet run --project tests/RescuAR.CameraGuidanceChecks/RescuAR.CameraGuidanceChecks.csproj --no-restore -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj --no-restore -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False
./build/Verify-ARCoreReleaseReadiness.ps1
dotnet build RescuAR.MAUI.sln --no-restore --disable-build-servers -m:1 '-p:AndroidSdkDirectory=C:\Program Files (x86)\Android\android-sdk' -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False -p:EmbedAssembliesIntoApk=true
python build/Verify-Phase4Package.py
```

- Settings checks: **33 passed**, covering persisted preference identity, independent settings, failed writes, deduplicated/suppressed turn haptics, every real offline PNG tile, missing coverage, rejected PBF/corrupt tiles, reuse/repair, atomic integrity failure, cancellation and preservation of unrelated data.
- Camera guidance checks: **37 passed**, including offline routing/directions with a forbidden online provider and retained dev routing geometry.
- Shared routing/AR regression checks: **351 passed**.
- ARCore compatibility/manifest readiness: **passed**.
- Every converted tile was additionally compared against its original MBTiles PNG bytes and its TMS-to-XYZ row mapping; all **3,555 matched**. All page XAML parsed successfully.
- Final Android Debug build: **passed, zero warnings and zero errors**, with managed code embedded for direct installation. Signed APK: **196,861,274 bytes** (187.7 MiB), copied to `artifacts/RescuAR-phase-4-debug.apk`. SHA-256: `6497f32875ef232ba37a9bae4272bd16a10f4371409c143ee8f147a6ddd60df9`.
- Final package verification: **passed**. The checker verifies APK/inner-map CRC, the embedded app assembly against the final built DLL, all required assets, excluded unused MBTiles, the unchanged repository ARM64 bridge, production route-override profile, vibration permission and TTS query. Results are in `artifacts/phase4-build.log` and `artifacts/phase4-package-checks.json`. This is bounded package evidence, not the pinned release-native rebuild/stripping validation.
- Python asset scripts were run with the bundled interpreter at `C:\Users\brian\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`; the commands above use `python` as the portable equivalent.
- The new host-only test project adds no package references. `NuGetAudit=false` was used for its initial restore because network auditing is unavailable locally; no dependency pins changed.

## Device acceptance still required

No connected Android device is available in this workspace. Host checks prove the production tile reader uses local data, but do not establish Android screen rendering, permission interaction, spoken output, vibration or flight-mode user-flow acceptance. The existing pinned release/field-validation process remains required.

- [ ] Cold launch signed out, first run, signed in with address/permissions complete, and signed in with address/permissions incomplete. Check native-to-managed splash transition, background/resume, fast startup and absence of duplicate navigation.
- [ ] Change each setting, reopen the screen and restart the app. Check settings and both camera voice buttons agree, with no stale preference on cached pages.
- [ ] Toggle detailed maps rapidly and switch between Map, AR, Profile and Settings during loading. Check no duplicate layers or late visible-page updates.
- [ ] Disable Wi-Fi/mobile data before first map use. Check tiles and all three context layers load on the general map and camera map; pan inside/outside coverage and through zooms 13–18. Confirm basic roads remain when details are off and missing areas are not presented as live/safe data.
- [ ] Remove the local map copy offline, verify details turn off, and turn details on again. Check reinstallation, accurately measured storage, retained route guidance and unchanged pending circle messages/contacts/avatar data. Exercise insufficient-storage recovery.
- [ ] Mute an active siren in Settings and revisit Dashboard. Check state and sound agree; manual siren action explains the muted state. Exercise unsupported audio/haptic devices and ordinary device volume settings.
- [ ] With a new remote advisory, verify popups off still delivers the camera emergency event; independently verify sound/haptic toggles and the existing feed. Establish a new initial snapshot without replaying old popups.
- [ ] On a confirmed route, check one vibration near a turn, no repeated pulses as meters change, no haptics during unavailable/off-route/emergency-suspended guidance, and proper behavior on route replacement. Check offline speech with a locally installed voice.
- [ ] Search help topics, expand answers, open the hotline directory, review shared privacy/use content from registration/profile, and refresh real system/permission information after returning from device settings.

## Commit summary

`feat(settings): integrate preferences and detailed offline maps`

## Commit description

Complete Phase 4 in the existing MAUI app:

- Connect device preferences to advisory popups, siren sounds, confirmed-turn/advisory haptics, shared navigation speech and optional map detail.
- Load 3,555 raster tiles and building/park/water context offline, with verified atomic installation and scoped map-copy removal.
- Add searchable help, live system/permission information and shared privacy/use content that reflects actual storage and circle behavior.
- Match native/managed radar splash presentation and prevent duplicate startup navigation without a fixed delay.
- Preserve dev routing, hazard/access rules, ARCore/Vulkan requirements and dependency pins.

Validation: Android Debug build passed with zero warnings/errors; its directly installable APK and embedded app code passed package verification. All 33 settings checks, 37 camera checks, 351 routing/AR regressions and ARCore readiness passed. Physical-device/offline-flow acceptance and pinned release-native validation remain pending.
