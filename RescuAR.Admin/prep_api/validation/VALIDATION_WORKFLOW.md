# PREP Validation and Calibration Workflow

## Current single-event validation

```text
01_rescuar_restart_from_tracing.R
        ↓
Independent NAMRIA flood trace
        ↓
02_rescuar_validation_steps_3_to_5.R
        ↓
Spatial metrics + population metrics + explicit PASS/FAIL decision
```

The main validation script now supports an optional `observation_domain.gpkg`/GeoJSON so incomplete post-event survey coverage does not automatically count as confirmed non-flooded area.


## Project acceptance rule

The project accepts **85% flood-extent accuracy**, operationalized as **F1 >= 0.85**. Raw overall pixel accuracy is not used as the acceptance metric because flood maps are spatially imbalanced. Precision, recall, IoU/CSI, population WAPE, and Spearman correlation remain supporting diagnostics and are reported separately.

## Peer reproducibility

```text
Independent Peer A trace ─┐
Independent Peer B trace ─┼→ 03_build_peer_consensus.R
Independent Peer C trace ─┘
                           ↓
                   pairwise IoU + majority consensus
```

Peer consensus is for improving reference reproducibility. It does not change the LiPAD model itself.

## Multi-event calibration

```text
Calibration events
        ↓
04_calibrate_scenario_rules.R
        ↓
Evaluate all 9 legitimate LiPAD candidates
        ↓
Select candidate mappings
        ↓
Validation/test events scored as holdout
        ↓
UNAPPROVED candidate config
```

No observed data are synthesized or altered to force a target score.

## Production promotion

```text
selected_rules_unapproved.json
        +
holdout evidence
        ↓
05_promote_calibrated_rules.R
        ↓
manual approval + minimum-event gates
        ↓
config/exposure_scenario_rules.json
        ↓
PREP API GET /v1/exposure/rules
        ↓
Admin Flood Hazard Exposure automatic mapping
```

## Population-exposure improvement

If a population weighting/grid layer is installed in `baseline_spatial.gpkg`, PREP uses it to weight within-barangay exposure. Otherwise the established uniform area-weighted method remains in use and is identified in API metadata.

## Historical event sources from Espina + Marikina Ecological Profile

The event registry now includes two additional independently published Tumana flood references:

```text
Ondoy 2009 — Tumana
    source map extracted
    ↓
source_ready_digitization_pending

2013 Monsoon — Tumana
    source map extracted
    ↓
source_ready_digitization_pending
```

The sources are stored under:

```text
validation/events/ondoy_2009_tumana/
validation/events/monsoon_2013_tumana/
validation/events/context/
```

These records are intentionally `reference_only` until the embedded map figures are georeferenced and digitized into independent `observed_flood` geometries. `04_calibrate_scenario_rules.R` now writes `calibration/event_registry_status.csv` and automatically skips source-only rows whose observed geometry is still pending.

Historical spatial events may be evaluated without population demographics. Population WAPE/Spearman remain `NA` when event-year demographics are unavailable; the calibration script no longer borrows 2020 demographics for 2009/2013 events.

The Ecological Profile context tables support cross-checking of population, households, density, topography, river conditions, and administrative flood-hazard records. They are not used to modify the independent observed flood polygon.

## Historical source-map activation

For registered Espina events whose status is `source_ready_digitization_pending`:

```text
00_prepare_historical_event_reference.R
        ↓
paired source/UTM GCP markers
        ↓
affine RMSE + leave-one-out QC
        ↓
numbered flood-boundary markers
        ↓
independent manual trace
        ↓
observed_flood.gpkg + observation_domain.gpkg
        ↓
explicit evaluator confirmation
        ↓
event_status = ready (role remains reference_only)
```

The Ulysses tracing script also now supports a UTM reference grid and manually selected numbered high-confidence trace markers. These are tracing aids only; they do not change independent reference geometry or validation thresholds.

