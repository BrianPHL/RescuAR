# RescuAR routing

`IRoutingService` is the geographic routing contract. `HybridRoutingService`
prefers online OSRM/MLD and initializes offline A* only when required.
`MLDARIntegrationService` returns complete routes and publishes short AR windows
separately. The selected provider's result does not establish trustworthy AR placement.

## Inputs and policy

A* builds one cached `RoadGraph` from embedded road **and point** GeoJSON in
`Navigation/Data/Resources/`. Named regional files are supported; they are not
rejected because there is more than one file. Restriction points are required
for locked gates, walls and explicit pedestrian prohibitions.

When `CENTRAL_ROADS.geojson`, `CENTRAL_POINTS.geojson` and `routing-manifest.json`
are present, only that pair is active. Missing partners or mismatched data/policy
hashes fail validation. Otherwise primary and regional resources load together.
The exact embedded data/policy identity appears in startup diagnostics.

MLD reads original OSM PBF through `Navigation/Data/Dockerfile`, using
`profiles/evacuation-foot.lua` and the shared `evacuation-policy.json`.
A central PBF and manifest take precedence over historical regional inputs.
The server prints its dataset identity at startup. This is operator evidence;
OSRM route responses currently provide no app/server identity handshake.
Map updates require a new extraction, image build and deployment.

See [QC_IMPORT.md](../Data/QC_IMPORT.md) for the common-source export and
activation procedure. A merged file cannot supply unmapped subdivision roads
or missing connecting corridors. Verified additions must reach both engines.

## Endpoint matching and routes

Both engines bound ordinary origin matching to 20 m and destination matching
to 50 m. A* uses a spatial segment index for both endpoints, temporary projected
endpoints, and multiple nearby candidates in one directed search. Weak components
prune candidates; directed traversal establishes reachability. Pedestrian one-way,
barrier, grade and hazard rules remain enforced. Same-segment endpoints can route
without detouring through stored vertices.
Central graphs use source OSM node IDs for junctions and point restrictions;
legacy resources retain coordinate matching. Colocated distinct source nodes
do not create a central routing connection.

Preference cost selects a route; physical geometry meters determine cumulative
progress and total distance in both adapters. No straight off-network connector
is added to the returned route. A checked road approach may retry from mapped
access within 50 m of the actual origin. Access lookup runs only after a direct
route fails, so offline graph construction does not delay valid online routing.

## Provider outcomes

| Outcome | Hybrid action |
|---|---|
| Internet unavailable | Use embedded A* immediately. |
| Transport failure or six-second online budget expires | Try A*. Caller cancellation propagates. |
| OSRM `NoSegment` | Report unmatched endpoint, then try bounded offline matching. |
| OSRM `NoRoute` | Return no route; do not override a disconnected online result. |
| Invalid server JSON/geometry | Report invalid response, then independently validate an offline route. |
| No safe online hazard alternative | Try hazard-aware A*, then validate the returned geometry. |

The primary Railway URL is the only default MLD endpoint. The historical
`FallbackBaseUrl` declaration does not enable a second server.

## Offline handoff during a trip

Network loss selects the local A* controller immediately when Android reports
the change. It preserves the downloaded route object, geometry provenance,
progress and established AR registration. The banner/map label reads
`A-STAR · saved MLD path` until validated A* geometry replaces the saved route;
the original route remains owned/rendered as MLD geometry. Accurate downloaded
geometry does not imply the API is still being contacted.

The saved path expires five minutes after the offline handoff. A confirmed
deviation, reverse walk, new hazard or changed destination can require a route
earlier. Expiry requests a background A* replacement only with a fresh qualified
measured position. The existing route stays active while calculation runs or a
candidate fails validation; expiry retries are at least 30 seconds apart.
Hazard restrictions still apply immediately. Reconnection alone does not select
MLD again within that trip. Explicit offline selection also controls hazard
replanning, regardless of the retained geometry's original algorithm.

## Camera navigation and AR

While Camera is visible, valid geographic routes are accepted before heading
acquisition finishes. GPS progress, confirmed deviation and arrival evaluation
continue during AR startup or pauses. Heading recovery reuses the active route
without restarting geographic progress. AR windows still require current session
heading, tracking, an anchor and independent road-entry evidence.

A validated MLD replacement can carry its existing geographic-to-AR transform only
with fresh paired GPS, the same session/tracking/world/heading frame, supported
registration and consistent position. It retains road-entry status only when
already following the road and both measured and tracked position meet the
existing five-metre entry limit on the new corridor.
Otherwise normal acquisition applies. Acceptance seeds the new route's own
position/progress and attempts publication immediately; it does not wait solely
for the next GPS callback. No synthetic GPS or arrival evidence is created.

A* replacements use directed common-corridor matching to remap accepted progress
and GPS/PDR evidence. They retain existing registration uncertainty, support age
and pending corrections rather than reseed from the disputed replacement fix.
Missing registration requires two later independent observations. Geographic
acceptance can therefore succeed while AR remains held. Qualified A* previews
can bridge up to eight seconds, with trip/geometry/window/frame ownership and
position, heading, hazard and tracking checks still enforced. MLD generation and
projection do not use this replacement policy.

Ground-anchor refinement monitors both horizontal movement and sustained yaw
change in the same ground reference. A qualified correction invalidates the
old world registration and requests heading/registration requalification; it
does not infer geographic north from an anchor rotation. While a correction is
pending, current-frame tracked motion may collect recovery evidence, but route
publication stays held until the normal recovery requirements pass. Initial GPS
bias and absolute compass error still require physical field measurements.

The Android draw loop skips presentation when the camera background is absent,
stale or from a different render generation. It never samples the single camera
target during an unfinished conversion. Brief gaps retain the last presentation;
sustained gaps show the existing camera overlay after two seconds of observed
interruption, with a 2D Map fallback after 20 seconds. Recovery health uses
completed camera draws as well as current textures, avoiding false covers when
UI polling coincides with a conversion. This does not restart ARCore or remove
the renderer's separate tracking/heading/ground safety gates.

Online routing can continue if the offline graph fails. Arrival and mapped road
access are held until the restriction graph is available; missing barrier data
never authorizes check-in. GPS vicinity requires fresh, accurate independent
observations within the configured boundary, rejects held or conflicting
geographic evidence, and remains informational. Guidance continues until an
eligible explicit user arrival action; vicinity alone does not complete the trip.

Camera currently owns this navigation state. Leaving Camera stops its progress
loop. Shared background/map navigation is a separate change.

## Validation

Use the [current field procedure](../../../docs/field-testing/README.md) for
candidate packaging, current registration/Depth policy and the two-device walk
matrix. Earlier batch reports describe their own revisions. Select relevant host
checks rather than treating a full regression run as required for every edit.

From the repository root:

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-cadence
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --offline-handoff
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --ar-audit
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-replay
python tests/routing-bundle-checks.py
./build/Verify-ARCoreReleaseReadiness.ps1
```

For routing-bundle changes, run the Python check with Python 3.11+ available.
Repeat the relevant field walks with full-session logs after installing the rebuilt
app. Record app and server data identities, route result, GPS match, AR hold reason
and arrival decision. Host checks do not verify device placement, mapped coverage
or the deployed server.

Diagnostic builds automatically include exact GPS and policy-stage inputs in
ordinary logs. See [local replay](../../../docs/field-testing/navigation-replay.md)
for extraction, quality checks and machine reproduction before another walk.
Production-profile redaction remains enabled.
