# Regional routing dataset import

Marikina remains the primary dataset. All named regional datasets present at
build time are included automatically. Local QC and other test areas can stay
disconnected from Marikina; no intervening roads are required for local routes.

## 1. Offline A* and map display

1. Keep backups outside `RescuAR/Navigation/Data/Resources/`.
2. Put the active files in that folder:

   ```text
   ROADS.geojson              primary Marikina roads
   POINTS.geojson             primary Marikina points
   QC_ROADS.geojson
   QC_POINTS.geojson
   OTHER_CITY_ROADS.geojson
   OTHER_CITY_POINTS.geojson
   ```

   Use uppercase `ROADS` / `POINTS` and the `.geojson` extension. The primary
   files load first, followed by regional files in filename order. Files outside
   this naming convention are not loaded. Include matching point data when
   available so pedestrian restrictions, walls and locked gates are recognized.
3. Each file must be a `FeatureCollection`. Roads must have `LineString`
   geometry and a flat `highway` property; points must have `Point` geometry.
   Preserve `osm_id`, `other_tags` in GDAL's default HSTORE text format, and
   `[longitude, latitude]` coordinates in WGS84 (EPSG:4326).
   Scalar tags exported as separate columns (`foot`, `access`, `bridge`, etc.)
   are also read; populated columns override the corresponding `other_tags`
   entries. Preserve matching POINTS data: barriers and locked gates on shared
   vertices are used by offline routing. Malformed road coordinates reject the
   whole feature rather than creating a shortcut across the missing vertex.
4. Routing, AR road visualization, and map display use the embedded datasets
   from this folder. No copy in `RescuAR.MAUI/Resources/Raw/` is needed; its old
   road/point copies are preserved but excluded from packaging. Regional map
   layers appear in blue; Marikina remains the initial map center.
5. From the repository root, build and install/deploy the app normally:

   ```powershell
   dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj
   ```

   No dataset flags or QC server address are required. Adding or removing data
   requires rebuilding and reinstalling the app. Including a dataset does not
   repair missing crossings or walking connections within that dataset.

### Shared evacuation policy

`evacuation-policy.json` is the source of truth for offline A* and the Docker
MLD profile. The app embeds it; Docker generates its Lua equivalent and runs
the same access/direction/cost regression cases before preparing the map.
Docker is pinned to the official `ghcr.io/project-osrm/osrm-backend:v5.27.1`
image so a changing `latest` profile cannot silently alter the rules.
See the [official image](https://github.com/project-osrm/osrm-backend/pkgs/container/osrm-backend/45826644?tag=v5.27.1).

| Rule | Both implementations |
| --- | --- |
| Ordinary roads | Footways, paths, pedestrian ways, steps, residential, service, living streets, corridors, minor/unclassified roads, tracks, generic roads, cycleways, primary/secondary/tertiary roads and links, platforms and piers are eligible. |
| Restricted motor roads | Motorways, trunks and their links, busways, and `motorroad=yes` require explicit `foot=yes/designated/permissive/official`. |
| Private access | `access=private` is eligible; `foot=no/private/agricultural/forestry/delivery/use_sidepath` remains excluded. |
| Access hierarchy | Explicit permitted pedestrian access overrides general `access=no/agricultural/forestry/delivery`. Directional `foot:*` and `access:*` are respected. |
| Building passages | Service building passages require explicit permitted pedestrian access. Mapped footway passages remain eligible. |
| Mapped junctions | Shared source vertices connect without requiring zebra/signal markings. Geometric intersections without shared vertices do not manufacture connections. Bridge/tunnel interiors stay separate from roads beneath/above. |
| Barriers | Mapped walls/fences/retaining walls, explicit pedestrian prohibitions, impassable features and locked gates block traversal. Gate/bollard presence alone does not block pedestrians. POINTS must be preserved for offline checks. |
| Unusable geometry | Proposed/construction ways, impassable ways and `area=yes` boundaries are excluded. Walking paths through open areas remain eligible. |
| Direction | Vehicle `oneway` is ignored; `oneway:foot` and directional pedestrian access control permitted travel. |
| Preferences | Private roads cost 1.15 times normal; major roads 1.35; unmarked mapped crossing ways 1.25. Rough surfaces add their documented factors. Factors multiply, and do not remove the road. |
| Distances | A* stores real physical distance separately from preference cost. MLD applies equivalent walking speeds at a 5 km/h baseline. AR uses actual geometry/distance. |
| Endpoint matching | Origin match is bounded to 20 m; destination match to 50 m. MLD sends these bounds to OSRM. Hazard bypass waypoints retain an unrestricted search and are checked against hazards afterward. |
| Small networks | Docker sets the small-component threshold to one node, so local test areas and private road fragments are not deprioritized solely for being small. Disconnected areas still cannot route to each other. |
| Detours | No blanket three-times-direct-distance rejection. Mapped connected detours remain eligible even when long. |
| Hazards | Both app providers use the same segment validation: outward escape from a starting hazard is allowed, movement deeper inside and re-entry are rejected. |

A* and MLD share eligibility and preferences; exact routes can still differ
because A* chooses nearby facility vertices and OSRM projects endpoints onto
segments. Different source snapshots, absent POINTS and conditional OSM
restrictions can also affect results. Conditional restrictions are not evaluated
by this static policy. An eligible mapped road is not a guarantee of current
disaster-time passability.

Private, major-road and unmarked-crossing preferences are routing cost penalties,
not a new UI confirmation flow. Road-centerline geometry remains an approximation
of a walking route unless the source supplies a separate footway/sidewalk.
Gravel/fine gravel/pebblestone multiply cost by 4/3; mud and sand multiply it by 2.
MLD returns `NoSegment` when the bounded endpoint search finds no eligible road;
the client treats it as no route so offline fallback can proceed normally.
OSRM's [small-component option](https://github.com/Project-OSRM/osrm-backend/blob/v5.27.1/src/tools/extract.cpp#L49)
affects endpoint matching; the Docker build supplies it automatically.

Rebuild/install the app and rebuild/redeploy the Railway routing service after
changing the policy or source data. Repeat the QC Balboa Bend request after
deployment; private access alone cannot connect an isolated source network.

### Parks, courtyards and other open spaces

Both engines can follow mapped `highway=path/footway/pedestrian` passages through
these areas. Include them and their connected entrances in the original OSM
source, then derive the app GeoJSON and Docker PBF from that same source.
For private evacuation passages retain the original access tags and record
explicit pedestrian permission where appropriate. A building service passage
also needs explicit permitted `foot` access.

An open-space polygon or an empty patch on the map supplies no obstacle-free
route. This implementation does not generate routes across polygon interiors
or add straight connectors across buildings, water or missing entrances. New
verified passages must be added to the common source data to be available in
both engines; a GeoJSON-only edit will not reach MLD. There is no general
free-terrain navigation engine in this change.

### Exporting another region

Use the original OpenStreetMap PBF from [BBBike Extracts](https://extract.bbbike.org/extract.html),
or a GeoPackage that preserves the required OSM fields and tags. Do not use the
reduced BBBike Shapefile export to reconstruct missing access/crossing tags.
For PBF, GDAL can produce compatible layers:

```sh
ogr2ogr -f GeoJSON CITY_ROADS.geojson city.osm.pbf lines -where "highway IS NOT NULL" -t_srs EPSG:4326 -explodecollections -nlt LINESTRING
ogr2ogr -f GeoJSON CITY_POINTS.geojson city.osm.pbf points -t_srs EPSG:4326 -nlt POINT
```

Keep GDAL's default `other_tags` encoding; do not select `TAGS_FORMAT=JSON`.
See the [GDAL OSM driver](https://gdal.org/en/stable/drivers/vector/osm.html).

## 2. Online MLD with Docker

MLD reads original OSM PBF, not the app's GeoJSON. Its input areas can also stay
disconnected. The Dockerfile merges supplied extracts internally and builds one
server containing them all.

1. Put original regional `.osm.pbf` files in
   `RescuAR/Navigation/Data/routing-inputs/`:

   ```text
   marikina.osm.pbf
   qc.osm.pbf
   other-city.osm.pbf
   ```

   Existing extracts can come from different source dates. Derive each area's
   GeoJSON from its original source. Do not combine the old GeoJSON-derived
   `marikina-routing.osm.pbf` with current exports. Keep obsolete extracts and
   backups outside `routing-inputs/`. See [Osmium's merge rules](https://docs.osmcode.org/osmium/latest/osmium-merge.html).
   The build excludes `type=route` and `type=route_master` relations (records
   grouping bus, numbered-road, hiking, or other routes), which the walking
   profile does not use. It keeps all nodes, road lines, and other relation
   types. For overlapping objects, it merges the available history and selects
   the latest available version of each object; re-exporting all areas from
   one snapshot is not required. It still checks that only one version remains
   per object and that every road node exists. See [Osmium's time filter](https://docs.osmcode.org/osmium/latest/osmium-time-filter.html).
2. On your local computer with Docker Desktop running, open PowerShell in
   `D:\Projects\RescuAR\RescuAR\Navigation\Data\` and run (not Railway SSH):

   ```sh
   docker build -t rescuar-mld .
   docker run --rm -p 5000:5000 rescuar-mld
   ```

   No region build arguments are required. The build excludes unused route
   groups, sorts and merges all `.osm.pbf` inputs, resolves overlapping objects
   to their latest available versions, checks road-node references, and prepares
   OSRM using the evacuation foot profile and MLD algorithm.
   An empty input folder fails with an instruction to add extracts.
   Policy/Lua test failures also stop the build. The extra Python/Lua build
   tools stay in the intermediate merger stage, outside the deployed image.
3. Check one local route in each area. Substitute real road coordinates:

   ```sh
   curl 'http://localhost:5000/route/v1/foot/LON1,LAT1;LON2,LAT2?overview=full&geometries=geojson&steps=true'
   ```

   Expect `"code":"Ok"` and plausible route geometry. No route between
   disconnected areas is expected.
4. Deploy the rebuilt image to `MLDRoutingService.PrimaryBaseUrl`
   (`https://rescuar-production.up.railway.app`), or change that constant to the
   HTTPS address of the deployed regional server and rebuild the app. The
   primary address is the sole default endpoint; the older fallback is not
   automatically used. Deployment is a separate step from building the app.
5. Rebuild and redeploy Docker whenever its PBF inputs change. Compare A* and
   MLD routes in each area and field-check crossing locations and AR placement.
