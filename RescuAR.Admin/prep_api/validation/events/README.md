# Historical event registry

`events.csv` is used by `validation/scripts/04_calibrate_scenario_rules.R`.

One row represents one independently observed historical flood event.

Required columns:

- `event_id`: unique ID, e.g. `ulysses_2020`
- `event_name`: readable event name
- `event_date`: ISO date (`YYYY-MM-DD`)
- `role`: `calibration`, `validation`, `test`, or `reference_only`
- `trigger_state`: `Alert`, `Alarm`, `Critical`, or blank if no defensible historical PAGASA state is available
- `observed_flood_path`: path to independent observed flood GPKG/GeoJSON, relative to `prep_api` unless absolute
- `demographics_path`: historical barangay demographics CSV used for both reference and modeled exposure
- `observation_domain_path`: optional survey/observation coverage GPKG/GeoJSON; leave blank if unavailable

Example only (do not add unless the provenance is defensible):

```text
ulysses_2020,Typhoon Ulysses,2020-11-12,validation,Critical,validation/events/ulysses_2020/observed_flood.gpkg,validation/input/demographics_2020.csv,
```

## Separation rules

- Calibration events may be used to select the best candidate mapping.
- Validation/test events must not be used to choose the mapping.
- Do not duplicate the same observed flood map across calibration and holdout roles.
- Do not create synthetic "ground truth" from PREP outputs.
- An event with no defensible Alert/Alarm/Critical historical state can still be `reference_only` for scenario sensitivity, but cannot calibrate the automatic status mapping.
