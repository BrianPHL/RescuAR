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
   available so explicit crossings can be recognized.
3. Each file must be a `FeatureCollection`. Roads must have `LineString`
   geometry and a flat `highway` property; points must have `Point` geometry.
   Preserve `osm_id`, `other_tags` in GDAL's default HSTORE text format, and
   `[longitude, latitude]` coordinates in WGS84 (EPSG:4326).
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

   Use the same source snapshot for every extract and derive each area's
   GeoJSON from that source. Do not combine the old GeoJSON-derived
   `marikina-routing.osm.pbf` with current exports. Keep obsolete extracts and
   backups outside `routing-inputs/`. See [Osmium's merge rules](https://docs.osmcode.org/osmium/latest/osmium-merge.html).
2. From `RescuAR/Navigation/Data/`, on a machine with Docker, run:

   ```sh
   docker build -t rescuar-mld .
   docker run --rm -p 5000:5000 rescuar-mld
   ```

   No region build arguments are required. The build sorts and merges all
   `.osm.pbf` inputs, rejects conflicting object versions, checks road-node
   references, and prepares OSRM using the foot profile and MLD algorithm.
   An empty input folder fails with an instruction to add extracts.
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
