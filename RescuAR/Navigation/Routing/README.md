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

## Camera navigation and AR

While Camera is visible, valid geographic routes are accepted before heading
acquisition finishes. GPS progress, confirmed deviation and arrival evaluation
continue during AR startup or pauses. Heading recovery reuses the active route
without restarting geographic progress. AR windows still require current session
heading, tracking, an anchor and independent road-entry evidence.

Online routing can continue if the offline graph fails. Arrival and mapped road
access are held until the restriction graph is available; missing barrier data
never authorizes automatic check-in. Arrival still requires the existing fresh,
accurate independent GPS observations within the configured confirmation boundary.

Camera currently owns this navigation state. Leaving Camera stops its progress
loop. Shared background/map navigation is a separate change.

## Validation

From the repository root:

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj
python tests/routing-bundle-checks.py
./build/Verify-ARCoreReleaseReadiness.ps1
```

Repeat the three field walks with full-session logs after installing the rebuilt
app. Record app and server data identities, route result, GPS match, AR hold reason
and arrival decision. Host checks do not verify device placement, mapped coverage
or the deployed server.
