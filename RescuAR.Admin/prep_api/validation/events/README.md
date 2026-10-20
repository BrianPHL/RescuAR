# Historical event registry

`events.csv` is used by `validation/scripts/04_calibrate_scenario_rules.R`.

One row represents one independently sourced historical flood event or event reference.

## Core columns

- `event_id`: unique ID.
- `event_name`: readable event name.
- `event_date`: ISO date when known; may be blank when the source gives only a year/period.
- `event_year`: explicit year for source-only records.
- `role`: `calibration`, `validation`, `test`, or `reference_only`.
- `trigger_state`: `Alert`, `Alarm`, `Critical`, or blank if no defensible historical PAGASA state is available.
- `event_status`: `ready` when an observed geometry exists, otherwise a descriptive pending state such as `source_ready_digitization_pending`.
- `spatial_scope`: city/barangay scope of the observation.
- `observed_flood_path`: independent observed-flood GPKG/GeoJSON, relative to `prep_api` unless absolute.
- `demographics_path`: optional historical demographics CSV. Spatial-only historical events may leave this blank.
- `observation_domain_path`: optional mapped/surveyed coverage polygon.

## Provenance columns

- `source_map_path`: source raster/image used to build the event reference.
- `supplementary_map_path`: non-event-exclusive supporting map, if any.
- `evidence_path`: event evidence table.
- `context_path`: supporting context/cross-check data.
- `calibration_eligible`: provenance flag only; calibration still requires `role=calibration`, a valid trigger state, and a ready observed geometry.
- `notes`: limitations and provenance notes.

## Included records

- `ulysses_2020`: current NAMRIA-derived citywide validation reference.
- `ondoy_2009_tumana`: Espina historical source map for Tumana; digitization pending.
- `monsoon_2013_tumana`: Espina historical source map for Tumana; digitization pending.

The Espina maps are intentionally registered as `reference_only` until their embedded figures are georeferenced, digitized, and QC-checked. The Marikina City Ecological Profile contributes contextual population, household, density, topographic, river, and hazard-inventory information; it does **not** replace an observed flood polygon.

## Separation rules

- Calibration events may be used to select candidate scenario mappings.
- Validation/test events must not be used to choose the mapping.
- `reference_only` events may be evaluated descriptively once their geometry is ready, but do not select automatic trigger mappings.
- Do not duplicate the same observed flood map across calibration and holdout roles.
- Do not create synthetic ground truth from PREP outputs.
- Do not substitute 2020 Ecological Profile demographics for 2009/2013 event demographics.
