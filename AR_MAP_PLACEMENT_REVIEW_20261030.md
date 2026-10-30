# AR / 2D route placement review — 2026-10-30

## Evidence

Reviewed `Logs/RescuAR_FieldTest_20261030_161013.zip` and both supplied recordings,
`Screen_Recording_20261030_161519.mp4` (187.5 seconds) and
`Screen_Recording_20261030_161631.mp4` (59.5 seconds). Neither recording is missing.
The capture covers 16:10:17–16:17:58 on SM-A546E / Android 14; it does not provide
a second device's evidence. The tested APK hash is
`f1c8e9daa2fc79d218a47776da846d1b0e3dc2c368e0b98435a5fd6e7343ce13`.

| Provider | Accepted route | Geometry fingerprint prefix | Source |
| --- | --- | --- | --- |
| MLD (Railway OSRM) | 16:12:41.761; 6 points; 260.052 m | `558d38779f6f` | logcat-001.txt:3110 |
| AStar | 16:15:49.977; 6 points; 249.120 m | `7b433a007add` | logcat-002.txt:332 |

The first recording shows MLD and a switch to the Camera module's 2D map. AR
guidance becomes hidden/offscreen while the map retains the route. In the second
recording, approximately 35–50 seconds, the cyan A* line projects over the houses
and parked car to the right of the road. These are placement/visibility symptoms;
they do not establish incorrect downloaded road geometry.

## Where the paths differ

1. Both providers return a `RouteResult`. The Camera 2D map and AR consume that
   same `activeRoute`; selecting another provider does not select another AR
   coordinate convention. `CameraPage.Map2D.cs:BuildCameraMapRoute` projects
   `route.Points` directly with Mapsui longitude/latitude → Web Mercator. Its
   blue observation marker and accuracy circle can update independently of
   committed turn progress. The separate General / Offline Map is a dataset
   browsing page, not the navigation renderer shown in the recording.
2. AR additionally needs a geographic-to-AR registration. A qualified GPS fix
   is paired with the camera pose at acquisition, then retained across later
   observations. The registered reference, shared map-matched progress and same
   route points feed `PublishRegisteredRouteWindow` / `MLDARIntegrationService`.
   This includes A*, despite that publisher's historical MLD name.
3. `LocalRouteProjector` converts WGS84 offsets to local East/North meters.
   `MapToArCoordinates` maps East to +X and North to −Z at zero yaw, then applies
   the calibrated world yaw. The floor anchor translation is subtracted for
   local publication and added once by the renderer root. The ground quaternion
   is not applied as a second horizontal route rotation. Mesh length is meters;
   the 2D map's pixel width / Mercator zoom must not be copied as AR scale.
4. Reconstructed logged windows agree with those equations, including bearings,
   lengths and nonzero ground origins. A headless check of the installed
   Evergine transform also confirms that the renderer's Y-axis rotation uses
   radians as implemented. No axis reversal, double origin, scale error or
   route-data mismatch was demonstrated. Replacing the working projection with
   a guessed rotation would not be justified.

## Confirmed defect and correction

`GpsRegistrationCorrectionPolicy` required three or more observations spanning
eight seconds, but discarded every observation except the latest three. At a
one- or two-second cadence, that queue can never span eight seconds. The capture
includes this faster GPS cadence. AR therefore could retain its initial origin
even when sustained, more accurate paired observations should qualify a bounded
correction. The 2D map does not depend on this AR registration filter.

The filter now retains the most recent qualifying interval, with at least three
observations and an eight-second span. It accepts at most one evidence sample per
second, expires observations after 30 seconds, and preserves the previous
five-second-cadence recovery behavior. Existing improved-accuracy, residual
scatter, uncertainty overlap, 2 m correction and cumulative uncertainty limits
remain. It never changes the device location, route geometry, yaw or reported GPS
accuracy. The shared correction decision now reports the actual evidence count.
No new subscription, sensor, dependency, ML model or cloud service was added.

Draw diagnostics now record route-ground Y, camera Y and their height difference
with the existing horizontal registration record. This supplies missing
height-at-draw evidence without another polling loop.

## What the capture cannot prove

- At 16:16:02, A* places the road approximately 4.7 m from its acquisition camera
  pose: GPS EN `(0.602,-2.591)`, road EN `(0.190,2.059)`, accuracy 11.51 m,
  yaw −56.581°. The registered world reference `(3.780,-2.570)` agrees with the
  published geometry. Later GPS accuracy is 7 m. This is a plausible lateral
  GPS/road offset inside the reported uncertainty, not proof of physical accuracy.
- Six stable magnetic heading samples measure repeatability, not absolute
  compass accuracy. Later orientation/AR comparisons differ from the initial
  calibration. The capture has no surveyed road/camera heading that establishes
  the correct absolute yaw. The existing contradiction gates remain intact.
- The cadence defect is a confirmed recovery bug, **not a proven complete cause
  of this recording's skew**. In the early MLD group, residuals fall below the
  existing 2 m correction threshold before eight seconds. A* later reports about
  1.2 m residual agreement. These samples do not justify moving the line to an
  assumed physical road center. Tests that reproduce logged placements verify
  calculations, not where the real road lies.
- Map matching already interpolates onto route segments. Snapping that position
  to a discrete road node or putting the road under the camera would not resolve
  an incorrect absolute GPS/heading registration and could create false guidance.

## Checks and field validation

The new frequent-update registration check fails against the preceding core.
The corrected core passes 112 registration checks, 60 PDR/map-matching checks
and 43 Camera map presentation checks. These cover both provider labels, the
October 30 measured placements, metric/bearing parity, ground-origin changes,
fast and slow update cadence, stale/duplicate/noisy fixes, ownership reset and
arrival safeguards. The first local check needed access to the installed
Evergine tooling; it ran successfully with that access. An initial longer
history window failed an older field replay; the final interval preserves that
recovery and passes the affected checks. No application/UI, integration or E2E
testing was performed.

Final Release/diagnostic Android build: PASS, 0 warnings / 0 errors (1m 05.69s).
ARCore readiness and ARM64 package inspection: PASS. The package inspection
also required access to the installed Android tools. Existing pinned native
bridge hash `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`
is unchanged. Candidate:
`artifacts/field-test-20261030-ar-registration/RescuAR-20261030-AR-Registration.apk`
(155,969,567 bytes), SHA-256
`890442b4876118f5e8fea9ccde208b6fa9df6741b68c438134d7cc27e5b44b2a`.
Build/test logs are in `artifacts/field-review-20261030-1610`; package evidence is
beside the candidate. A successful build does not establish physical road alignment.

On each available physical device, enable capture before the following sequence
and repeat it for A* and MLD:

1. Select a nearby destination in Camera 2D Map. While stationary for 20–30
   seconds, compare the blue observation/accuracy circle with the road, then
   switch to AR and slowly pan toward/away from the road. Expected: the same
   route, stable distance while held, no road rotation caused by phone turning;
   any qualified origin correction remains bounded. Initial walking is not
   required to acquire the existing magnetic alignment.
2. Walk safely along 20–30 m of the sidewalk, then stand still. Switch between
   2D and AR. Expected: the cyan window advances along the same route and stays
   aligned with the road; genuine tracking/position uncertainty retains the
   existing explanatory status and safety behavior.
3. Stop guidance, choose a different center and restart without closing the app.
   Expected: the new route replaces the old one, with no stale origin/geometry.

Return the capture and recording with the actual results before further
behavior-dependent alignment changes. If the line remains on a building, the
new draw-height record and synchronized GPS/heading/pose evidence must determine
the next correction; this candidate is not a physical-alignment acceptance pass.
