# ARCore corrective batch 14

Apply this changed-files ZIP to repository root after batch 13 (base commit
94e6674df2fcd957d7ffa8b913cdc097ebb6d937). Archive paths are repository-relative.
The packaged COMMIT_MESSAGE.txt contains the Conventional Commit summary and body.

## Implemented corrections

- Centralize geographic-to-AR conversion: zero-yaw East is +X, North is -Z,
  and Up is +Y. Initial camera alignment, movement revalidation, mapped routes,
  raw GeoJSON lines, GPS-to-road origin offsets, mapped-route screen guidance,
  and road approach arrows use the same convention.
- Keep plane/depth session configuration fixed across workload changes.
  Ground/depth consumers still acquire outputs on demand; existing thermal
  restrictions on route depth consumption remain in place. Retained sessions
  still pause and resume. Automatic depth may remain configured on unaffected
  supported devices, so power/thermal behavior still requires a field check.
- Disable automatic depth on SM-A546E / Android API 34, in addition to the
  existing API 36+ SM-A546E and SM-A156E profiles. This addresses the profile
  with a native ms_depth abort at 2026-09-29 16:31:36.562. The existing
  diagnostic force-depth-on flag remains an explicit controlled-retest option.
- Reuse heading work when an offline route fails, keep GPS polling independent
  of asynchronous approach calibration, and retain valid approach cues during
  refresh. Each cue still expires with its original GPS fix and session.
- Serialize heading captures and bind cached calibration to its AR session.
  Six recent distinct tracked frames can establish a stable heading window;
  earlier unsettled samples no longer poison a later stable window. The
  existing six-sample / eight-degree stability thresholds remain unchanged.
- Both algorithms start with road approach. A 15-35 m GPS matching corridor
  does not establish road entry. Two distinct fresh fixes within 5 m of the
  mapped road, medium-or-better match confidence, the existing 20 m placement
  accuracy ceiling, and no major-road access barrier are required before the
  cyan navigation corridor is published. Missing entry evidence holds geometry;
  it never manufactures a straight GPS-to-road walking segment.
- Report local road-component size and eligible destination-component count
  for disconnected offline approaches, without adding unverified connections.
- Road diagnostics display the placement GPS uncertainty and log session,
  fix age, heading repeatability, and segment count without logging coordinates.
  Flood simulation height, sources, and rendering behavior are unchanged.

## Evidence and limits

The latest field archive confirmed a native ARCore depth-worker abort after
repeated spherical_rectifier camera-model failures. A depth-demand transition
occurred 64 ms before the abort, but the internal errors began earlier;
reconfiguration is a suspect, not a proven sole cause. Device retesting is
required to establish whether the mitigation stops the failure.

The MLD route completed in about 3.6 seconds. Its first match was 15.5 m from
the mapped route inside a 25 m uncertainty corridor. The old startup check
therefore skipped the approach arrow. A later 8.2 m match still did not prove
road entry. Batch 14 regression fixtures cover both observations.

A*'s first route attempt took about 49 ms; its first floor arrow appeared about
36 seconds after destination selection, largely while waiting for GPS and
heading. This batch removes redundant heading cancellation and serial waits;
it does not relax GPS placement freshness or accuracy limits.

Replaying the archive's logged MLD origin/destination against the embedded
A* graph found a road about 15.5 m away in a 40-node component disconnected
from the two eligible destination components. GeoJSON and the MLD pedestrian
map are different routing inputs. This batch exposes that disconnect and
preserves crossing/access restrictions. Correcting the dataset requires
verified pedestrian connections; an unconnected local road cannot become a
complete evacuation route merely because an arrow can point toward it.

Correct conversion does not make ordinary GPS and compass measurements exact.
The road-inspection fix reported 11 m uncertainty. Compass repeatability is
not absolute north accuracy. GeoJSON geometry must be checked against known
road centre lines before changing it to compensate for a visual offset.
Tighter global placement requires surveyed offline reference markers/control
points, or a separately configured ARCore Geospatial/VPS integration where
coverage and measured accuracy support the intended placement. Neither is
implemented or claimed as validated by this batch.

Raw diagnostics intentionally show map data without depth occlusion, so lines
can draw over a foreground fence or plant. This alone does not prove a road's
horizontal location is wrong. The two supplied screenshots were taken after
the archived capture ended, so their exact pose/GPS state is not available.

## Next field test

1. Build the diagnostic variant with RescuArDiagnosticBuild=true. Confirm
   BUILD_MANIFEST batch=ARCore-14 and the expected source revision in the log.
   On the affected Android 14 phone, confirm FORCED_DEPTH_OFF and plane finding
   ACTIVE. Do not override depth on during the first stability retest.
2. Start capture before opening the camera. Test approach from the same public
   location using online MLD and offline A*. Both should show a floor arrow
   when recent GPS, stable heading, and tracked ground are available. A*
   may still report route connection pending for the disconnected dataset.
3. Walk toward a known mapped road. Check that being 8-16 m away does not
   switch to the cyan route merely because the GPS corridor is broad.
   Two distinct near-road fixes should authorize the route corridor.
4. Minimize and reopen the camera repeatedly. Confirm retained-session resume,
   visible camera feed, fresh floor/arrow state, and no stale geometry when
   changing destination, camera sub-tab, or session.
5. Enable GeoJSON diagnostics at two known road landmarks. Rotate the phone;
   the same road should remain in its world direction. Repeat away from the
   metal gate/parked vehicle to check possible magnetic interference. Compare
   orientation separately from position offset and capture GPS uncertainty.
6. Retest indoor flood simulation, its height controls, and outdoors ground
   detection with depth disabled on this phone.
7. Keep capture running while taking screenshots and until any crash/return
   occurs. Supply that archive with the screenshots so timestamps can be
   correlated. Include phone OS and Google Play Services for AR version.

Automated build/package results are recorded in the delivery manifest.
All device tests above remain pending until performed on the phone.