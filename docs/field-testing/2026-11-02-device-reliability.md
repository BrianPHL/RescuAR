# Shared navigation reliability candidate

Historical candidate at `9a48b90`. The later
[cadence recovery report](2026-11-02-samsung-honor-cadence-recovery.md) supersedes
its two-observation startup policy. Current Camera registration uses one already
qualified, pose-paired observation; the original candidate description below is
retained as history. Use the [current field procedure](README.md) for retesting.

This candidate replaces provider-specific acquisition timing with a common native navigation path. GPS acquisition is mapped from Android elapsed realtime into the camera clock; original provider UTC, coordinates and reported accuracy remain unchanged. Navigation deadlines and evidence age use a stable process clock. A delayed fix is paired with its historical acquisition pose, never automatically with the current camera position. The bounded history holds 512 snapshots and avoids duplicate camera frames, so high publication rates do not evict still-eligible observations. Reusing a camera frame cannot refresh its acquisition time.

Both providers now share heading recovery and a one-second polling delay between bounded navigation requests. Initial AR registration requires two spaced observations agreeing with tracked movement. The app retains the accepted geographic route, voice and arrival checks while collecting AR recovery evidence. A prolonged recovery identifies the missing input and recommends the 2D map after 15 seconds. The displayed provider comes from the actual route, including fallback, rather than network icons.

The MLD adapter requests alternatives and uncached replacements, and respects comparable server evacuation weights. Physical distance breaks equal-cost ties. Missing comparable weights retain server ordering. Access/hazard checks and bounded endpoint matching remain in place.

Optional route-depth occlusion is off in the baseline candidate. Supported depth acquisition remains available for ground/flood features, and known native-crash protections remain active. Diagnostic logging alone no longer bypasses those protections. Deliberate experiments can enable depth or route occlusion through diagnostic launch flags.

## Record the exact candidate

Use the same signed APK on both phones. Record its SHA-256, source revision and ARCore/device profile from the package evidence and logs. Retain the prior candidate for comparison. Confirm Precise location and the actual provider shown in the app. Wi-Fi status alone cannot identify the selected provider.

After each run, collect the existing device logs without changing the app or clearing logs:

```powershell
./build/Capture-NavigationFieldEvidence.ps1 -DeviceSerial DEVICE_SERIAL -TrialName honor-astar-outbound -OutputDirectory artifacts/field-trials/honor-astar-outbound
```

The collector reads model/OS/app/ARCore versions, attempts the installed APK checksum, and exports selected navigation log tags. It retains existing diagnostic consent and coordinate redaction. It does not launch the app or change device settings. Use a separate output directory for every trial and preserve the matching screen recording.

## Initial field matrix

For both Honor X9B and Samsung Galaxy A54, perform three repeat trips for A-STAR and three for MLD with the same known start/destination and walking direction. Include the reverse journey. Check:

1. Cold start and stationary heading acquisition with the ground visible.
2. Walking forward, stopping, turning the phone and returning it to the route.
3. AR → 2D → AR without changing the destination. Verify trip/route continuity, current user position and offline basemap recovery.
4. Background/resume and a brief camera-tracking interruption. Check recovery once usable data returns.
5. A genuine deviation on permitted streets. For MLD, confirm a new API request from the current origin, and record any detour at C. Benitez/Q. Gregorio with its actual destination.
6. Baseline route-depth occlusion off. Compare an explicit depth experiment only after the baseline run is recorded.

For a controlled A-STAR comparison with connectivity unchanged, an advanced tester can launch the diagnostic APK with a provider override:

```powershell
adb -s DEVICE_SERIAL shell am start -S -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p com.rescuar.app --es rescuar.navigation.provider astar
```

Use `auto` instead of `astar` to restore normal provider selection, and confirm the actual route label. This override is ignored by production builds. Optional diagnostic launch flags are `--ez rescuar.arcore.force_depth_off true`, `--ez rescuar.arcore.force_depth_on true`, and `--ez rescuar.arcore.route_depth_occlusion true`; repeat a baseline run before interpreting a change in these experiments.

## Inspect failures by layer

- `NAV_LOCATION_ACQUISITION`: selected provider, real accuracy, acquisition age and consumed-fix watermark.
- `NAV_FRAME_CLOCK`: whether the native frame timestamp mapped into the elapsed-realtime clock, or frame receipt time was needed as an explicit fallback.
- `NAV_HEADING_REACQUIRE`: shared recovery without geographic replanning.
- `NAV_REGISTRATION_DECISION`: initial confirmation, frame requalification, bounded corrections and support age.
- `NAV_QUALITY`: actual route provider, all reported blockers, recovery duration, pose-pairing skew, registration state and renderer visibility/depth counts.
- `DYNAMIC REROUTE STARTED` and MLD endpoint messages: replacement request versus accepted geometry. A renderer counter alone does not prove that the user saw the ribbon in the viewport.

## Acceptance and remaining physical validation

The targets from the reviewed plan remain field acceptance targets, not results established by unit tests: sustained useful AR on both phones, recovery within five seconds after required usable inputs return, and no persistent Samsung-only fallback in good measured conditions. Check the proposed 95% eligible visibility target against recordings and logs.

This candidate does not add surveyed or VPS geographic anchors. Two coherent GPS fixes reduce unstable seeding but cannot prove that a stable GPS bias is absent. Measure lateral placement at known street/sidewalk reference points at near and 7.5 m look-ahead distances. The proposed 2 m accurate-placement target and avoidance of building/private-property boundaries remain unverified until those measurements are supplied. If GPS/compass registration cannot meet them, a verified geographic reference is required; offline operation must retain an offline reference option.

Report the exact trial/build, provider, timestamp, visible error, recovery message and matching evidence bundle. Maintain a common baseline and change one identified cause at a time.
