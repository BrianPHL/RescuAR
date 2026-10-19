# Field validation corrections — 19 October 2026

The user's MLD, A*, arrival, flood, and logging findings were read before the
captures. The changes below are local and uncommitted. Ground acquisition fixes
from the preceding review are retained. No routing datasets, dependency pins,
native bridge binaries, or hosted services were changed.

## Supplied evidence

All three installed-artifact manifests identify the same APK SHA-256:
`583bdeefe9d0f49f56d488244ec99acdb59313c8c8a4c5969cb9a4aee4983c6b`.
That hash matches the APK supplied after the preceding ground/flood review.
Its embedded revision is `9a79006b279e2e1ed2551d8cabc87c20c5c98c55`, but the
artifact also contains that review's uncommitted changes; the revision alone
does not identify its complete source state.

| Capture | Evidence quality | Observed behavior |
|---|---|---|
| `091153`, HONOR ALI-NX1 / API 35 | Incomplete. Logger was dead at collection, no completion marker, about 27 seconds and one application event. | No navigation or Depth conclusion can be drawn for this phone. |
| `091241`, Samsung SM-A546E / API 34 | Complete; 09:12:44–09:36:39, 6.42 MiB, 7,928 application events, 682 navigation events. | MLD followed by A*. Route visibility alternated with position confidence; Depth was disabled. |
| `091351`, Samsung SM-A546E / API 36 | Complete; 09:13:54–09:35:06, 4.04 MiB, 3,634 application events, 387 navigation events. | MLD route hidden in all 20 periodic destination-active snapshots; repeated identity suspension; near-destination arrival rejected; Depth disabled. |

The ZIP is preserved in `Logs/`. Review copies are under the ignored
`artifacts/field-review-20261019/logs/` directory. References below use the
capture folder and original line numbering.

## Findings and corrections

### MLD/A* guidance and progress

In `091351/logcat-001.txt:3700`, a fix only 1.3 m from the route was classified
Low because its reported accuracy was 43.1 m, despite a 144.2-point independent
corridor score gap. Subsequent poor-precision fixes repeatedly erased good
route-identity observations. Low precision and competing route branches were
being treated as the same failure. Identity suspension also rejected PDR steps.

`GpsPdrFusionPolicy` now retains recent identity evidence across a weak fix only
when it still matches the same unambiguous corridor. Weak fixes do not advance
progress or count as confirmation. Evidence expires after ten seconds, distinct
observation timestamps are required, and actual off-route/branch ambiguity
continues to suspend identity. CameraPage supplies the provider timestamp.

`RouteProgressTracker` also treats densely divided, connected straight segments
as one corridor. It continues to distinguish parallel/reversing branches. This
is a separate source correction, not proof that dense vertices caused every
observed Low match.

A screen arrow and destination distance can now remain available while the
floor route is hidden, using fresh GPS, a tracked camera pose, and the current
session's stable heading. It is labelled as destination direction and does not
create a walking connector or bypass the cyan route's independent confidence
checks.

### Road registration and heading

The screenshots show a geographic overlay displaced or rotated relative to the
street. A* calculates from geographic coordinates; it does not use projected
camera lines as its routing input. These images therefore do not establish
that the GeoJSON coordinates themselves are skewed.

The captures retain fixed initial yaw estimates of approximately -114.0 and
-81.8 degrees. Revalidation repeatedly rejected short noisy movements or GPS
course disagreement (`091241/logcat-001.txt:5741`, for example). The source could
accept a placeholder course of zero merely because displacement-derived speed
was high, even when provider speed did not support a course reading.

Heading revalidation now checks provider speed before using its course and
requires a displacement baseline scaled to GPS uncertainty, bounded to 8–20 m.
Three consistent movement windows still precede a bounded correction. No
permanent rotation or offset was applied to the map data.

The complete captures contain no Vulkan device-loss or camera fence-timeout
event. Later camera telemetry reports roughly 20 FPS under the existing Warm
thermal limit. These facts do not prove the reported visual stutter is resolved.
Street registration, continuity, and distance stability require another walk.

### Arrival

`091351/logcat-002.txt:1659` rejects a fix at 22.5 m with 20.5 m accuracy;
line 1736 rejects 23.4 m with 10.5 m accuracy because the entire uncertainty
circle does not fit inside the old 30 m radius.

Automatic confirmation now uses two distinct fresh observations over at least
three seconds, accepts accuracy up to 25 m, and tests the fix position against
the facility radius, capped at 40 m. The default facility radius remains 30 m.
Stale fixes, changed trips, poor precision, mapped major-road barriers, and
positions beyond the bounded radius remain excluded. Arrival confirms vicinity;
facility entry is still unverified.

A prominent top card now says “You're near [destination]” with the approximate
distance within the existing 75 m manual-confirmation range. It appears before
automatic confirmation and explains the entrance/confirmation action instead
of presenting only a button.

### Flood visualization

Both Samsung captures log supported Depth with `FORCED_DEPTH_OFF` and
`requested=DISABLED` (`091241/logcat-001.txt:1671`,
`091351/logcat-001.txt:2414`). The source used that generic experiment label
for a device-profile quarantine. Subsequent image requests produced
`IllegalStateException`; accepted depth-frame counters stayed zero.

The diagnostic field candidate now requests automatic Depth on supported
profiles, including these Samsung profiles, and records
`DEPTH_FIELD_REVALIDATION`. An explicit Depth-off experiment is still respected.
Normal builds retain the existing native-crash profile quarantine until this
retest is accepted. Configuration logs now distinguish quarantine, explicit
flags, and the field revalidation mode.

The real scene-depth water compositor is retained. The wire-grid substitute is
removed. Until coherent depth arrives, the UI states that it is preparing the
water preview; disabled Depth is identified explicitly. Disabled sessions no
longer attempt image acquisition. Fresh depth, ground identity, generation,
tracking, metric height, and object masking requirements remain enforced.

The captures also contain repeated native `spherical_rectifier` errors even
with Depth disabled. Enabling Depth and compiling the compositor do not prove
that the installed native provider will produce a usable stream or avoid its
previous native abort. That is a material phone-validation requirement.

Google's [DepthMode reference](https://developers.google.com/ar/reference/java/com/google/ar/core/Config.DepthMode)
confirms that image acquisition with Depth disabled throws
`IllegalStateException`. Its [Depth guide](https://developers.google.com/ar/develop/java/depth/developer-guide)
documents checking support, enabling automatic Depth, and using scene depth for
occlusion.

### Logging

Application logging was not removed from the two complete captures. The HONOR
capture is genuinely incomplete and cannot be reconstructed from this ZIP.
The smaller archive is not evidence that navigation ran without emitting logs.

Diagnostic status cadence changes from 30 seconds to five seconds. New compact
`NAV_PLACEMENT` and `NAV_PROGRESS` records capture visibility reasons, route and
ground identity, GPS age/accuracy, heading, progress, fusion, and arrival state.
Heading decisions now include collecting/unavailable states and provider speed.
Road diagnostic placement includes the applied yaw and calibration time.
`ARCORE_DEPTH_CONFIGURATION` and `FLOOD_COMPOSITION` identify Depth enablement,
waiting states, ground matching, and actual water composition. Coordinate
consent/redaction behavior is retained.

## Verification and required retest

- Focused regression command: `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-validation`.
- Result: 43 checks passed, covering the retained ground fix, arrival, dense and
  parallel road matching, precision-only identity holds, duplicate/expired GPS
  evidence, heading/course handling, destination bearings, and Depth policy.
- Android Release build passed with zero warnings and zero errors.
- `build/Verify-ARCoreReleaseReadiness.ps1` passed. APK checks passed for the
  merged manifest, ARM64 native bridge, and diagnostic build profile. The native
  bridge hash matches the supplied capture; the bridge was not rebuilt.
- No integration, automated browser, or physical-device testing was performed.

Candidate: [RescuAR-20261019-FieldValidation.apk](artifacts/field-test-20261019/RescuAR-20261019-FieldValidation.apk).
SHA-256: `901de92a9519d0d3eb29ee79c715a90a40d54f4cd887759084da72e9f57dbb34`.
Package evidence is in `artifacts/field-test-20261019/package-evidence/`.

Retest both routing algorithms on the same road. Check the destination-direction
cue during brief cyan-route loss, correction after sustained straight walking,
and distance continuity. Near the entrance, expect the explicit proximity card
and faster automatic vicinity confirmation. For flood mode, expect scene-masked
water at the selected height rather than grid lines. Collect a complete capture
for each phone, especially a fresh HONOR capture, before further
behavior-dependent changes.
