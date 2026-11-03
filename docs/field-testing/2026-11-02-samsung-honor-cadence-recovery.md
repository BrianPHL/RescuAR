# Samsung/Honor field regression and recovery

The recovery source was subsequently committed as `d83d797`. The candidate
source label, APK hash and original verification below identify the earlier
locally built artifact; that artifact is not automatically identical to a new
build of the committed revision. Use the [current field procedure](README.md).

Reference behavior: `b239fa7`. Checkout: `dev`, HEAD `9a48b90`, with the preceding uncommitted AR recovery changes retained and refined. No commit or push was created.

## Evidence

Inputs are the Honor X9B archive containing `A_.mp4` (429 s) and `Mld.mp4` (484 s), `Samsung Galaxy A54.mp4` (1005 s), and `RescuAR_FieldTest_20261102_170127.zip`. Recordings were inspected through sampled stills, not application interaction. They establish visible behavior, not sensor accuracy or speech audibility measurements.

The Samsung installed-artifact manifest confirms APK SHA-256 `af8826ebd186a4ca7a3fc0857c244befe1071e1c24a7cf5cac0551da3e5e3915`, the exact previously supplied candidate. Model: SM-A546E, Android 14/API 34. CaptureQuality reports a complete capture with four log segments, 8,336 application events, and 858 navigation events. Honor's APK identity and internal sensor decisions are not available from its videos.

- Across the Samsung capture, 198 of 275 bounded navigation polls selected no fix; 51 selected GNSS and 26 selected network locations. These counts include startup requests. They do **not** establish how many native callbacks Android supplied or why the hardware delivered a particular cadence.
- Of 65 logged stability decisions, 55 were unreliable. The A-STAR session remained in prolonged uncertainty for over 280 s despite later fresh acquisitions.
- AR was permitted in 6 of 74 logged A-STAR quality snapshots and 0 of 85 MLD snapshots. These transition/throttled snapshots are not a visibility-time percentage.
- `logcat-001.txt:9969` begins A-STAR registration confirmation at 17:13:15.498. The next qualified observation at `:10220` is 8.310 s later and restarts the eight-second confirmation window. Registration later seeds once, then support/tracking/position recovery frequently hides it.
- `logcat-003.txt:4115` shows MLD pending initial registration at 17:22:47.497. Subsequent selected GNSS observations occur at 17:23:19.525, 17:23:34.696, and 17:23:50.445. An eight-second two-fix startup requirement cannot complete at that cadence.
- `logcat-004.txt:60` rejects a fresh 7.5 m GNSS acquisition while reporting 35.3 s since reliable positioning; `:2451` still rejects a fresh 6.5 m acquisition after 148.9 s. Strong heading/tracking alone does not resolve that geographic recovery latch.
- Honor A-STAR repeatedly displays “Direction not confirmed” while geographic distance decreases. Honor MLD has clear ribbons around 02:40–03:20, then also enters direction recovery from about 03:40. Its visible success is intermittent; the exact heading source/transition cannot be inferred from the video.
- Samsung's video shows held text/distance and stale map-marker ages, consistent with the location/recovery logs. Nearness of two screen markers alone does not prove facility entry or qualifying automatic-arrival evidence.

## Changes and relation to b239fa7

| Failure | Correction |
| --- | --- |
| Later startup code required two qualified acquisitions within 8 s | Restore `b239fa7`'s single **already qualified and pose-paired** registration seed. Optional two-fix policy remains available, but the Camera no longer opts into it. |
| Later native requests stopped after 2 s and awaited ≤6 m accuracy/≤500 ms receipt age for an early result | Restore the reference's ≤8 m prompt GNSS delivery and 15 s acquisition budget when no fix is available. Actual observation age must still be ≤3 s, and consumed observations remain excluded. |
| Only raw GPS/network sources were requested | Add the optional Android platform fused provider when present, retaining GPS/network fallbacks and the same reported-accuracy/native-time checks. No Google/Huawei dependency was added. Android documents this provider as combining other location inputs when available: [LocationManager](https://developer.android.com/reference/android/location/LocationManager#FUSED_PROVIDER). Availability does not guarantee a faster or more accurate fix. |
| AR residual checks could veto geography, including arrival | Separate AR placement holds from geographic acceptance. Fresh plausible GPS feeds map matching, progress, voice/text eligibility and arrival even when AR alignment is held. Contradictory AR placement still cannot render. GPS distance filtering no longer depends on AR's stationary classification. |
| Existing three-fix recovery repeatedly restarted after 15 s GPS gaps | Remove that cadence-dependent geographic latch. Fresh qualified observations restore geography; stale/duplicate/inaccurate fixes and implausible short-term displacement remain rejected. Some of this hold logic predates `b239fa7`; the later acquisition changes exposed it more often. |
| Fixed per-fix progress caps accumulated distance lag with sparse GNSS | Scale forward correction by elapsed accepted-acquisition time, with existing reliability weighting and a bounded 60 m maximum budget. Ordinary rapid-fix correction remains unchanged. |
| Route identity and arrival candidates expired before the next fresh observation | Allow up to 30 s between distinct strong identity matches and between qualifying arrival observations. Weak identity retention stays at 10 s. Each arrival fix still needs ≤5 s age, valid precision, destination ownership and no mapped major-road barrier. |
| Known-frame AR continuity expired after only 8 m of tracked walking | Permit up to 30 m of **live tracked**, straight-segment display recovery within the existing 30 s lifetime. Step/PDR bounds remain 8 m. Tracking/world/yaw continuity, lateral/speed limits, no estimated turn, qualified registration and ground/route ownership checks remain required. This never creates geographic progress or arrival evidence. |
| A contradicted magnetic calibration could block the rest of a trip | After a five-second hold, allow a fresh synchronized stable magnetic capture in the same known frame. Never return the contradicted yaw as trusted. Movement corrections, including unfinished ones, are not discarded through this path. |

The previous compass-reference correction, untrusted startup estimate, persistent measured map marker/accuracy circle, offline basemaps and voice features remain. No routing dataset, route-finding algorithm, server deployment, model-specific offset or native AR dependency pin was changed.

## Verification

`--field-cadence`: **560 focused checks passed** in one run. Added checks replay recorded GNSS spacing/delivery ages with synthetic straight-road coordinates, exercise the production geographic policy and registration settings, bound visual-only recovery, recover strong route identity and arrival across 17 s gaps, and exercise magnetic reacquisition. Existing routing ownership, heading, map, voice/haptic, arrival and registration checks also passed.

`build/Verify-ARCoreReleaseReadiness.ps1`: passed. Release Android build: zero errors, one existing `CS8601` warning in `SafetyCircleService.cs:274`. Native bridge, manifest and package checks passed. The raw capture and extracted videos remain in ignored artifacts; no private coordinates or credentials are included in this document.

Replacement APK: `artifacts/field-regression-20261102-1735/RescuAR-field-cadence-recovery-b3f73f6c.apk`. SHA-256: `b3f73f6c978c1fc1fa026fee5bc242b7d69a68abfdfec3f81e12a662c5420707`. Embedded source revision: `9a48b90-field-cadence-recovery-20261102`. Full verification summary: `artifacts/field-regression-20261102-1735/verification-summary.json`.

No physical-device, app interaction, integration/E2E, or browser application verification was performed. Unit replay isolates software failures; it does not establish sustained visible AR, surveyed street alignment, fused-provider delivery cadence, or verified facility entry.

## Device retest

1. Install the replacement APK, confirm its recorded hash/source revision, and test A-STAR offline and MLD online on both devices. Expect fresh location acquisitions to update measured position and geographic guidance without repeated recovery latching. If the device supplies sparse GNSS, a GPS-derived map marker must still age honestly between actual observations.
2. Walk a straight safe section, switch AR → 2D → AR, then stop/restart guidance. Expect AR startup to avoid waiting for an unavailable eight-second pair and a suspended magnetic estimate to retry from fresh stable samples. Live bounded geometry may bridge a GNSS interval; stale frames, turns requiring new evidence and contradictory placement must remain suppressed.
3. Stay near the selected destination long enough for two distinct qualifying fresh fixes. Expect vicinity confirmation when the precision, radius and barrier rules pass, or the existing user-arrival option when eligible. A displayed pin overlap alone is not automatic arrival evidence.
4. Capture logs and screen recordings again if any case fails. The native acquisition log now reports GPS/network/fused callback counts, and quality logs distinguish geographic holds from AR placement holds, making the next failure directly diagnosable.
