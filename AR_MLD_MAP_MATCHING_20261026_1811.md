# MLD map matching, distant start and window flicker — 18:11 capture

Reviewed checkout `de687d0` with the preceding restart-recovery changes committed. The supplied `RescuAR_FieldTest_20261026_181106.zip` is a complete A54/Android 14 capture from 18:11:09 to 18:16:45. Its installed hash `a02ab28eba04991be156cc0dd1aecf50788154971564e1172acadb28ac754246` matches the last delivered APK. Analysis files and verification logs are under `artifacts/field-review-20261026-1811/`.

## Direct answer: the route is already map-matched

OSRM snaps requested endpoints to its mapped road network. `RouteProgressTracker` then matches each observation to an interpolated point on a segment of the selected route, using route continuity, accuracy and available direction evidence. It does not simply choose the closest discrete node. The first route's initial endpoint snap is about 0.43 m from the requested geographic origin, while its next route vertex is 94.212 m farther along. A nearest-vertex replacement could therefore make the apparent start jump much farther.

The capture contains **three selected-route downloads**, all `reason=START`, and **zero automatic dynamic reroute requests**. The route lengths are 264.4 m, 20.3 m and 261.6 m, corresponding to destination selections. Within each trip the geometry fingerprint stays the same while local window versions change. The first trip's published yaw stays `-67.42` degrees. This is a moving subsection of a real mapped polyline, not a new left-pointing line generated without geographic geometry. Its apparent movement relative to the view also depends on the camera pose and ground-reference handoffs.

This is geographic matching. It does not identify the physical road surface in camera pixels. Google's documented [Snap to Roads API](https://developers.google.com/maps/documentation/roads/snap) likewise describes matching a sequence of GPS observations to likely road geometry; it is not evidence of how Google Maps internally implements its AR navigation. No Google service, subscription or new dependency is introduced here.

## Why the start is far away

At 18:12:50.357, the initial registration is seeded from GPS with **9 m reported accuracy**. The downloaded route starts at local geographic EN `(-0.043,0.423)`, while that GPS reading is `(8.281,-6.153)`. Applying the retained heading and the acquisition-time camera pose maps the route start approximately **10.7 m from the camera**. The renderer is receiving this displacement; it is not adding a fixed visual stand-off.

Actual GPS accuracy spans **7–49.37 m**, median **11 m**, across 31 position-pipeline observations. Ten exceed 20 m. All **44** alignment snapshots report `visual=False`; all **23** native road-surface observations report zero qualified fits. Some semantic boundaries do reach the ground fitter (for example, 53 image edges and 53 ground edges at 18:13:01), so this is not the earlier semantic-image acquisition/aspect-ratio failure. The available edge observations never establish a qualified physical corridor.

Consequently, the physical start inside/near the house is not explained away by geographic map matching. A GPS estimate can match the correct mapped road while the map-to-AR origin or heading remains inaccurate. Moving the entire road onto the camera would make it look nearby but could draw it through the house. The initial 10.7 m displacement is **not certified corrected by this patch**; a reliable physical reference is still missing.

## Two confirmed update bugs

| Capture | Raw matched progress | Accepted trip progress | Visible start distance |
| --- | ---: | ---: | ---: |
| 18:12:50.438, window 2 | 0 m | 0 m | 10.69 m |
| 18:12:53.526, window 3 | 10.77 m | 0.595 m | 18.99 m |

1. **Held GPS still advanced the visible window.** At 18:12:53.507 (`logcat-001.txt:2896`), fusion action is `HoldStationary`, but the display policy still used raw matched progress `10.771`. At line 2920 the new window starts roughly 19 m away. The number/movement filter held GPS movement while the visualization independently accepted it. A similar case occurs at 18:14:41: raw progress 7.52 m versus accepted progress 0.02 m. This accounts for an additional distant-start jump; it is distinct from the original registration offset.
2. **The preview was cleared during the next-location polling gap.** Fresh preview publication allows fixes up to five seconds old. At 18:14:46.093 the last fix is 5.1 s old; the moving-window publisher clears the bridge before its outer retention logic can preserve it. At 18:14:46.334 the next valid location publishes another window. The same pattern occurs at 18:15:04.412 (age 5.4 s) followed by a new window at 18:15:05.344. These are avoidable clear/rebuild interruptions, not proof of road recognition or a changed route.

Other disappearances are intentional confidence handling: 15 placement snapshots report uncertain geographic positioning, 16 report direction recovery, and some involve ground/tracking recovery or an inactive destination. Depth occlusion hides one segment in only two render samples, with no hidden prefix; it does not explain most of the fading. All renderer snapshots have zero arrow wings, confirming the solid-ribbon presentation.

When geometry reappears at a shifted position, a fresh matched window and the current camera/anchor explain its screen placement. There is **no qualified visual road correction anywhere in this capture**. The leftward reappearance therefore cannot be claimed as successful snapping to the actual visible road.

## Focused changes

- For MLD updates whose GPS movement is held (`HoldStationary`, `KeepPdrLead`, or pending backward confirmation), choose the visible subsection from tracked motion in the already qualified registration. A held raw GPS point cannot advance it independently. Accepted forward fixes keep their normal GPS-backed behavior; physical-road registration retains priority. This does not create a location reading or change trip progress, arrival evidence, route geometry or routing requests.
- Retain an already published MLD preview over the **five-to-eight-second** gap using the existing registration-support budget. Ownership, destination, placement frame, tracking, heading, geographic confidence and hazard checks must still qualify. This prevents the inner publisher from clearing a preview before retention is considered. Expired or invalid registration, new trips, uncertain position and genuine route changes still cannot retain it.
- Name the existing five-second fresh-fix budget so new-publication and retention rules refer to the same boundary. Log the GPS movement action alongside display progress.

The fixes remove the extra raw-GPS window jump and the polling-boundary clear/rebuild cycle. They do not extend the route backward through an unmapped property, add a guessed camera-to-road connector, or snap the road to the camera. Normal genuine-deviation rerouting and access/barrier checks remain intact.

## Verification

- Android Release diagnostic build: passed, **0 warnings, 0 errors**, 67.64 seconds.
- Focused alignment/recovery runner: **864 checks passed**, including 13 new continuity checks using the captured geometry/GPS/pose values and five-second polling boundary. An initial test-fixture parser error caused by a contextual variable name was corrected before the passing run; application compilation passed.
- The captured-case check reproduces the initial approximately 10.7 m GPS-derived gap and demonstrates that the additional raw-GPS jump is removed while retaining the original world registration. It is not a physical road-accuracy pass.
- ARCore release-readiness and whitespace checks passed. No dependency or native bridge changes; no interactive app/browser/device acceptance or E2E testing performed.
- Candidate/source hashes are recorded under `artifacts/field-test-20261026-mld-window-continuity/`.

## Smallest next device check

Start capture before opening MLD. Aim down the road while standing for 20–30 seconds, walk 20–30 m, then stop again. Expected: a GPS movement marked held must not jump the window ahead; ordinary five-second polling must not clear a still-qualified preview. Geometry may still be withheld for genuinely poor positioning, heading or tracking.

Record a road-facing image and its time if the initial line is far away or inside the house, and collect the archive. A map screenshot helps verify the selected geographic road independently of its camera placement. Correct physical alignment remains a separate acceptance requirement; a short, stable but displaced line does not pass it. All native road fits were rejected in this run, so that physical-registration limitation remains explicit.
