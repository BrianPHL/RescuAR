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
