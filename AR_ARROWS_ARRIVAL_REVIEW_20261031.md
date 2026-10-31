# Navigation arrows and arrival review — 2026-10-31

Baseline: `7fbd3b6` (`fix(ar): prevent GPS registration correction starvation`).
No tester logs or installed-APK identities accompanied this feedback. All three
videos and four screenshots were accessible. Video evidence below comes from
offline frame extraction, including closer sampling around UI transitions;
the tester's report of working voice was not independently verified from audio.

## Uploaded evidence

Video aliases follow the order in the request:

| Clip | Filename prefix / duration | Useful observations |
| --- | --- | --- |
| V1 | `AQMxQ6KiQv-…` / 1:33.8 | At 0:00–0:05, “Turn right in 5 meters” and about 103 m to the destination. By approximately 0:10 the manual-arrival button and near notice replace that instruction. Cyan route geometry is visible around 0:15–0:35. Tracking/alignment warnings and missing geometry follow. The completion overlay appears around 1:20. |
| V2 | `AQMUIjUDNW…` / 1:48.8 | Guidance around 0:10–0:20 includes the turn and about 102 m to the destination. The manual-arrival action appears by about 0:25. A moving cyan line and a mapped corner are visible around 0:35–0:55; the bend alone is not proof of an arrowhead. Alignment warnings accompany later missing geometry. Completion appears around 1:45. |
| V3 | `AQPBKFCOw6…` / 1:46.8 | Starts from the Cordis Mariae School directory entry. Around 0:10–0:20 it shows about 114/105 m remaining, then the manual action by about 0:25. Repeated “Checking direction” / alignment warnings accompany no clearly visible cyan route in the sampled frames. Completion appears around 1:30. |

The clips do not display a routing-provider label. A* attribution comes from
the tester's explicit report; it cannot be independently assigned to each clip.
No clip establishes MLD's arrow visibility or its absolute arrival distance.

Screenshots:

- `882618984_…jpg`: a bent solid cyan route and the manual-arrival button near
  the intersection. This demonstrates line rendering, not verified entrance
  arrival or a distinct arrowhead.
- `…efefa4d1….png`: completion says “You're Near the Evacuation Center”. The
  displayed 12 m is **distance traveled**, not distance to the destination.
  This still image cannot establish the arrival trigger or its timing.
- `…a123be38….png`: 2D shows a 46 m route, about 34 m remaining and a GPS
  observation with ±4 m accuracy. This is separate evidence from completion.
- `…568c99fa….png`: the planner says “Current location unknown” and displays
  “Start Routing”. The labeled Parang school pin is a directory place, not a
  validated user position or evidence of an active route failing to render.

## Confirmed causes and focused changes

### Mapped-route arrowheads were disabled

A* and MLD route results share the route-progress/registered-window publisher,
`MLDARIntegrationService`, `ARRouteBridge` and `ARRouteRenderer`. Normal cyan
geometry is a `RouteWindow`. Before this change,
`ARRouteVisualPolicy.ShouldRenderForwardArrow` allowed only an
`ApproachConnector`, so the renderer explicitly disabled its two arrow wings on
every normal mapped route. Ribbon rendering was independent and could succeed.

The separate `ARRoadApproachRenderer` can display a qualified road-access cue,
and the UI also has turn icons. Those are different from the normal route's
arrowhead. This explains why reports of arrows and reports of missing arrows
need not contradict each other. It does not prove which cue a tester saw.
Heading/tracking warnings in V3 also show why this is not a universal failure of
all rendering; logs are needed to identify which qualification failed there.

Normal qualified route windows now retain their solid ribbon and a forward
arrowhead. Its tip is interpolated about 6 m along the current mapped window,
rather than placed at the potentially distant 40 m endpoint. It follows the
actual incoming/outgoing leg at a corner, stays inside short mapped prefixes,
and scales down on short legs. It does not point straight at a destination,
alter geographic geometry or move the registration. Existing ownership,
heading, tracking, hazard and occlusion gates still control visibility. The
existing qualified access-connector endpoint behavior is retained.

### Manual arrival was much broader than automatic confirmation

`UserArrivalConfirmationPolicy` previously exposed the action within 75 m,
with accuracy up to 50 m and an observation up to 2 minutes old. Its notice
also took priority over turn instructions. This supports the premature button
and premature near-message behavior in the clips; the precise tester GPS
readings are unavailable.

The manual action and its priority notice now require a position within 15 m,
reported accuracy at most 15 m, and fix age at most 5 seconds. Held observations
and an unknown/mapped-major-road access barrier block the action. Existing trip,
page and destination checks, explicit confirmation dialog, and revalidation
after that dialog remain. This is a stricter approximate GPS gate, not proof
that a phone is physically inside the entrance.

### Automatic arrival and voice have separate conditions

`SafeZoneConfirmationService` remains responsible for automatic **vicinity**
confirmation: default radius 30 m, facility-specific radius capped at 40 m,
accuracy at most 25 m, fix age at most 5 seconds, no mapped major-road barrier,
and distinct qualifying fixes over at least 3 seconds. Counts expire/reset for
invalid evidence, long gaps or destination changes. Remaining route distance is
diagnostic; it does not independently establish or block vicinity. These rules
can complete guidance before reaching a physical doorway. The uploads cannot
establish whether the reported automatic completion was too early.

The manual button does not confirm arrival merely by appearing. A user must
accept its dialog. Both actual confirmation paths reach `HandleSafeZoneDecision`,
which sets `safeZoneConfirmed`, clears route visuals and publishes feedback.
`NavigationFeedbackController` announces confirmed arrival from that flag and
deduplicates it. Near text and ordinary “Destination ahead” guidance do not set
the flag. Automatic thresholds and the working voice implementation were
preserved; focused checks retain valid automatic confirmations and one arrival
announcement. Physical entrance timing still needs logs and a synchronized test.

## Verification

277 focused checks passed using `--arrival-arrows`, covering arrow placement,
corners/short prefixes, both provider paths, retained registration and map
geometry, manual eligibility, valid/invalid automatic arrival, trip changes,
map presentation and voice cancellation/deduplication. Core compilation passed
with 0 warnings/errors. Final Android Release/diagnostic build passed with
0 warnings/errors in 1m 52.49s. ARCore readiness and ARM64 package inspection
passed; the pinned native bridge is unchanged. No app, browser, integration or
E2E test was run, and these results do not establish device pixel visibility or
physical arrival accuracy.

Candidate: `artifacts/field-test-20261031-arrows-arrival/RescuAR-20261031-Arrows-Arrival.apk`.
SHA-256: `1d9b1e86b1af7ff369fe8226b5fb4cfb06993b7b7bbfdcdf612303b058f31599`.
Build/check logs are under `artifacts/field-review-20261031-feedback`;
package evidence is beside the candidate.

## Device retest

Enable capture before starting. For each device, record its model, APK and
explicitly selected algorithm, since the supplied clips do not identify them.

1. **A*:** start at least 80 m away and walk a safe route with a bend. With
   qualified tracking, expect the continuous cyan line and nearby forward
   arrowhead to advance along the same route. Turn the phone and switch 2D/AR;
   expect the existing world alignment and real-time progress to remain intact.
   Genuine tracking/heading/occlusion warnings may still hide guidance.
2. **Arrival:** remain well outside the final 15 m, then approach the mapped
   destination/entrance. Expect no manual button or priority manual-arrival
   notice outside its fresh/accurate 15 m gate. Automatic completion may occur
   earlier inside the existing 30–40 m vicinity gate after repeated fixes.
   Expect one arrival announcement when sound is enabled, and no announcement
   solely because a button appears. Record the actual doorway, pin position and
   completion time to assess any premature automatic completion.
3. **Repeat / MLD:** stop, select another destination, repeat A*, then select
   MLD explicitly. Expect new route ownership, updating solid lines and mapped
   arrowheads under the same qualification gates, without stale visuals. Check
   manual and automatic arrival timing separately for MLD.

Return the actual results with the promised logs before further changes that
depend on device tracking or absolute arrival timing. No commit was created.
