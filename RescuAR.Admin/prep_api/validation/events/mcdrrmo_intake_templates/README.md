# MCDRRMO map QC templates

Copy `qc_review_TEMPLATE.json` to the chosen event's `qc_review.json` and fill all fields with **independently verified evidence**. False values are intentional; do not change to true just to enable script execution.

For image-based georeferencing, create `registration_checkpoints.csv` by copying the CSV template and adding **at least six** independently checked road/landmark locations. Use EPSG:32651 metre coordinates: `reference_*` = independently known real location; `registered_*` = the same location as transformed from the registered map. The checkpoints must **not** be points used to fit the transformation. Script 08 requires RMSE ≤50 m and maximum checkpoint error ≤80 m (project-level QC gates; not official source standards). Failed checks cannot be bypassed by changing the observed flood outline.

For original GIS source geometry, set `source_method` to `native_gis`, document the agency's GIS provenance and verified native CRS, and set `native_crs_verified` to true; checkpoint CSV not required. Read source materials and coverage evidence before confirming observed vs scenario status.

Create two independent polygon layers (EPSG:32651 preferred):
- `candidate_observed_flood.gpkg` layer `observed_flood` — manually traced observed flooded areas or verified agency polygons, no predicted LiPAD input.
- `candidate_observation_domain.gpkg` layer `observation_domain` — geographical area independently surveyed/verified to include observations of flooded and **confirmed dry** locations. Do not infer the domain is citywide based on a map's paper extent.

Do **not** substitute a barangay or city administrative polygon as coverage without evidence. If the observation footprint is unknown, leave the event pending, and avoid interpreting pixels outside blue inundation as confirmed dry.

After an independent review, set each boolean gate in `qc_review.json` to true **only with documented evidence**, then run script 08. Script 08 never evaluates PREP predictions or changes LiPAD hazard polygons.
