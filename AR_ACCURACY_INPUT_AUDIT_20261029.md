# AR accuracy input audit — 29 October 2026

Reviewed checkout `e7d268f` and the complete `RescuAR_FieldTest_20261026_185849.zip` capture/recording. This work follows the removal of application ML road alignment; no ML or cloud dependency is introduced.

## Concrete acquisition defect

The application requested `GeolocationAccuracy.Best` from MAUI **9.0.120**, assuming that request would acquire the best available position. In that exact SDK implementation, Best uses a **50 m completion threshold**. A single GPS or network callback meeting that threshold completes the request and removes the listeners. The app then waits for its next route-progress poll. It can consequently stop collection before a better result arrives and repeatedly interrupt GPS acquisition.

This is verified against the [pinned MAUI Android implementation](https://github.com/dotnet/maui/blob/9.0.120/src/Essentials/src/Geolocation/Geolocation.android.cs). The SDK behavior is intentional; relying on it for stable AR-grade acquisition was the application integration problem.

The capture's initial accepted location reports **11 m accuracy**, its requested origin is snapped approximately **17.4 m** to the selected road, and the resulting world-space route start is approximately **17.38 m from the camera**. The rendered placement follows that input; no extra fixed stand-off is needed to reproduce it. The old logs omit provider identity, so they cannot establish whether that particular observation came from GPS or network positioning. The acquisition defect is confirmed, but it is not claimed as the sole proven cause of every physical placement error.

## End-to-end audit findings

| Stage | Finding |
| --- | --- |
| Route retrieval and coordinates | The accepted MLD polyline follows the same straight mapped corridor. Longitude/latitude parsing, metre conversion and selected-destination ownership are consistent with the supplied geometry. |
| Location acquisition | The first-within-50m shortcut was active. Actual provider and bearing uncertainty were lost at the adapter boundary. |
| GPS timestamps/cache | Original acquisition timestamps are retained; no cache receipt-time relabeling was found. Freshness gates remain necessary. |
| Phone heading | The pinned MAUI implementation uses Android RotationVector, not GameRotationVector. Quaternion components, rear-camera -Z, magnetic declination and AR azimuth conversion are consistent with the SDK conventions. Six repeatable readings are not independent proof of absolute compass accuracy; the MAUI wrapper discards Android orientation accuracy information. No constant heading offset is guessed or applied here. |
| Pose pairing | Location/orientation observations use bounded acquisition-time history in the same session/epoch/world. Recorded orientation pairing skews are small; these do not explain a 17m displacement while stationary. Sensor callback timing can still contribute to rotational error and has not been independently measured on a device. |
| Geographic-to-AR placement | Captured GPS/pose/yaw values reproduce the published offset. No extra north reflection, double application of yaw or duplicate GPS offset was identified. |
| Rendering hierarchy | The route root is a newly created identity transform, attached to the scene rather than the camera. Heading uses the same camera quaternion/forward convention as rendering. No serialized non-unit route scale or hidden camera-parent transform was found. |
| Ground placement | Recorded camera-to-ground values are plausible. A horizontal positioning error is not repaired by changing floor height. |
| Map matching/PDR | The previously corrected continuous segment matching, bounded origin correction and PDR walking-direction handling remain. They do not turn a geographic estimate into a surveyed road position. |

These checks narrow the problem; they do not certify every device sensor or the physical road map. Current logs still cannot isolate absolute compass bias from absolute location/map error.

## Focused implementation

- Replace Android's one-shot MAUI current-location call with one native `LocationManager` collector shared by requests. Both enabled GPS and network providers may contribute real observations. No provider is selected merely by name: current candidates are compared by reported accuracy, then acquisition time.
- During active camera navigation, retain the same collector between polls. Request updates with **1 s minimum time and zero minimum distance**, allowing stationary accuracy improvements and slow walking updates. Foreground ownership ends on destination clear, page departure or Android activity pause/stop. Requests outside navigation remain bounded and release their collector afterward. Concurrent callers are serialized.
- Prefer a current GPS fix reporting **8 m or better** without unnecessary delay. Otherwise collect candidates for **2 seconds** before returning the best current result. The existing 15-second no-fix timeout remains. This is a collection policy, not a claim that the reported radius is guaranteed real-world error. Coarse fallback retains its actual uncertainty and downstream placement gates.
- Reject stale/future observations at the adapter; retain actual coordinates, accuracy and acquisition time. Preserve provider, GPS bearing accuracy and native elapsed acquisition timestamp for diagnosis. Approximate-only permission skips GPS requests rather than causing a fine-permission exception.
- Withhold GPS course from route selection when its reported bearing error exceeds **30 degrees**. Good course and legacy readings without that metadata retain their existing handling. This course is walking/GPS direction, not the magnetic camera heading.
- `NAV_LOCATION_SOURCE` records source ownership and precise-permission state; `CURRENT`, `NAV_POSITION_PIPELINE` and position-innovation diagnostics now include actual provider/bearing quality. No per-frame image processing is added.

No route coordinates are moved onto the camera, no synthetic access connector is created, and PDR estimates remain separate from arrival/location evidence. Normal ARCore tracking and the existing routing, hazard and proximity rules remain.

## Verification and device boundary

- **872** focused alignment/recovery checks passed, including **9** new acquisition/course cases. They verify the recorded 11m first result does not immediately end acquisition, a later precise GPS result wins, coarse fallback preserves metadata, stale/future/invalid observations are refused, provider name cannot override quality, and explicitly inaccurate course is withheld.
- Android Release diagnostic build passed: **0 warnings, 0 errors**, **1 minute 49.93 seconds**. An initial compile identified two namespace references in the new Android adapter; these were corrected before its one allowed rebuild.
- Release-readiness, whitespace and the **complete ARCore package verifier passed**. The verifier used a local quoted Java adapter to avoid the vendor batch launcher's known spaced-path problem; repository verification scripts were not changed. Native bridge SHA remains `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- Candidate: `artifacts/field-test-20261029-location-acquisition/RescuAR-20261029-Location-Acquisition.apk`, SHA-256 `c920655899be53e4a29588fc1cec5ac5d3b0e8e5cd1522a85a0affc471db36e8`. Its source manifest records base `e7d268f` plus the current uncommitted changes. Build/test logs are under `artifacts/field-review-20261029-acquisition-audit/`.
- Native listener delivery, activity lifecycle, acquisition improvement, battery cost and physical road alignment require a device. The pure policy tests do not simulate Android location hardware. Existing source/sensor-lease checks do not constitute a hardware pass.

For the next short capture, stand facing the known road for **30–45 seconds**, rotate to the gate and back, then walk **15–20 m**. Stop guidance, select the destination again and briefly background/restore the app. Expected: no phone-turn walking progress; no five-second polling blink with supported evidence; a route following the road; a rebuilt route on restart; location-source stop on background/destination clear. Return timestamps and the archive if the line still crosses the house. Compare actual GPS/network provider, fix accuracy and heading at that time before further changes. The known road's approximate 275-degree direction is a useful independent heading check, not a value to force into camera orientation.
