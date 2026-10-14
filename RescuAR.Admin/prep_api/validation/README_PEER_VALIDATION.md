# RescuAR / PREP Peer Validation Guide

## Purpose

This package is for **independent peer validation of the PREP flood-exposure workflow** using the NAMRIA Typhoon Ulysses flood map as the historical reference.

This is **not machine-learning training**. The workflow is a GIS-based validation process that compares:

- an independently traced NAMRIA Ulysses flood extent,
- LiPAD flood-hazard scenarios stored in `baseline_spatial.gpkg`, and
- PSA 2020 barangay demographic data.

Each evaluator should perform the tracing and validation independently.

---

## Required Folder Structure

The peer-validation package should contain:

```text
RescuAR.Admin/
└── prep_api/
    ├── data/
    │   └── spatial/
    │       └── baseline_spatial.gpkg
    │
    └── validation/
        ├── README_PEER_VALIDATION.md
        │
        ├── scripts/
        │   ├── 01_rescuar_restart_from_tracing.R
        │   └── 02_rescuar_validation_steps_3_to_5.R
        │
        ├── input/
        │   └── demographics_2020.csv
        │
        ├── work/
        │   └── ulysses_namria_georeferenced.tif
        │
        └── output/
            └── [initially empty]
```

Optional audit files may also be present inside `validation/work/`:

```text
namria_control_points.csv
ulysses_namria_gcps.tif
```

These are not required to run the peer validation.

---

## Files You Must Not Reuse From Another Evaluator

Do not copy or reuse another person's tracing outputs.

The following files should be generated independently by each evaluator:

```text
validation/input/observed_flood.gpkg
validation/input/observed_flood.geojson
validation/input/observed_barangay_flood_fraction.csv
validation/input/observed_barangay_exposure.csv

validation/work/observed_flood_manual_raw.gpkg

validation/output/spatial_validation_scenarios.csv
validation/output/population_validation_scenarios.csv
validation/output/population_validation_by_barangay.csv
validation/output/ulysses_validation_report.txt
```

If any of these already exist in your copy, the tracing script will remove stale validation outputs before starting.

---

# 1. Software Requirements

Use:

- Windows
- R
- RStudio

The tracing process is interactive and uses an R graphics window.

Install the required R packages once:

```r
install.packages(c(
  "sf",
  "terra",
  "dplyr",
  "stringr"
))
```

After installation, restart RStudio if necessary.

---

# 2. Open the Project Correctly

Extract the `RescuAR.Admin` folder anywhere on your computer.

Example:

```text
C:/Users/YourName/Documents/RescuAR.Admin/
```

or:

```text
D:/Projects/RescuAR.Admin/
```

Open RStudio from inside the project or one of its subfolders.

The scripts should be configured to locate the project root automatically.

If your copy still contains a hardcoded path such as:

```r
C:/Users/Josh/Documents/Project/RescuAR/RescuAR.Admin
```

replace it before running the validation.

---

# 3. Stage 1 — Independent Flood Tracing

Open:

```text
prep_api/validation/scripts/01_rescuar_restart_from_tracing.R
```

Run the **entire script** using:

```text
Source
```

Do not run only selected lines unless instructed.

The script loads:

```text
prep_api/validation/work/ulysses_namria_georeferenced.tif
```

and:

```text
prep_api/data/spatial/baseline_spatial.gpkg
```

---

## 3.1 Review the Automated Guide

A map window will appear.

The important map colors are:

```text
Lime  = automated tracing guide
Cyan  = Marikina barangay boundaries
```

The lime guide is only a **visual aid**.

Do not blindly trace the guide if it disagrees with the visible NAMRIA flood shading.

Trace the actual yellow/orange/red flood area visible on the NAMRIA map.

Press **Enter in the R console** when ready to continue.

---

## 3.2 Trace the Outer Flood Polygon

The next graphics window asks you to trace the observed Ulysses flood extent.

Use the mouse to click along the outside boundary of the visible flood shading.

When finished:

- right-click, or
- press `Esc`

depending on your RStudio/R graphics configuration.

The traced line should follow the actual NAMRIA flood extent as closely as reasonably possible.

---

## 3.3 Internal White or Unshaded Areas

If the outer flood boundary contains obvious internal white/unshaded areas that should not be treated as flooded, they may be traced as exclusion polygons if the script is configured for exclusion holes.

At the top of the tracing script:

```r
NUMBER_OF_OUTER_POLYGONS <- 1
NUMBER_OF_EXCLUSION_HOLES <- 0
```

Change `NUMBER_OF_EXCLUSION_HOLES` only when clearly needed.

Example:

```r
NUMBER_OF_EXCLUSION_HOLES <- 2
```

means two internal exclusion polygons will be traced.

Do not exclude areas simply because they are difficult to interpret.

---

# 4. Stage 1 Output Check

After tracing is complete, the script creates:

```text
prep_api/validation/input/observed_flood.gpkg
prep_api/validation/input/observed_flood.geojson
prep_api/validation/input/observed_barangay_flood_fraction.csv
```

A final QC map will also appear.

The final display uses:

```text
Cyan     = barangay boundaries
Magenta  = final observed flood extent
```

Before continuing, visually check that the magenta outline reasonably follows the NAMRIA flood shading.

The console will also print the observed flood percentage for each barangay.

---

# 5. Stage 2 — Check the Demographic Reference

Open:

```text
prep_api/validation/input/demographics_2020.csv
```

It should contain PSA 2020 values for all 16 Marikina barangays.

Required fields:

```text
barangay_name
population_2020
children_2020
population_source
children_source
notes
```

Do not replace these values with PREP-generated estimates.

The demographic file is an independent reference input.

---

# 6. Stage 3 — Run the Validation

Open:

```text
prep_api/validation/scripts/02_rescuar_validation_steps_3_to_5.R
```

Run the **entire script** using:

```text
Source
```

This script:

1. recalculates the NAMRIA observed flood fraction per barangay;
2. creates the derived observed population-exposure reference;
3. evaluates LiPAD 5-year, 25-year, and 100-year flood scenarios;
4. computes spatial validation metrics;
5. computes population-exposure validation metrics; and
6. writes the final validation report.

---

# 7. Validation Outputs

After the script completes, review:

```text
prep_api/validation/output/spatial_validation_scenarios.csv
```

This contains:

- Precision
- Recall
- F1 score
- IoU / CSI
- True-positive area
- False-positive area
- False-negative area

The project now defines **85% as the acceptable flood-extent accuracy rate**, operationalized as **F1 >= 0.85**. F1 is used instead of raw pixel accuracy because flooded and non-flooded areas are spatially imbalanced.

Primary acceptance criterion:

| Metric | Acceptance target |
|---|---:|
| F1 (primary spatial accuracy measure) | **>= 0.85 (85%)** |

Supporting spatial diagnostics are still reported to show *why* a scenario performs well or poorly:

| Metric | Supporting target |
|---|---:|
| Precision | >= 0.80 |
| Recall | >= 0.80 |
| IoU / CSI | >= 0.70 |

A scenario is considered to have reached the project's acceptable spatial accuracy rate when **F1 >= 0.85**. The supporting diagnostics do not override that primary PASS/FAIL decision. These are project-defined evaluation thresholds, not official NAMRIA standards.

---

Open:

```text
prep_api/validation/output/population_validation_scenarios.csv
```

This contains:

- MAE
- RMSE
- WAPE
- Spearman correlation
- Top-5 barangay overlap
- equivalent child-population metrics

Supporting population-exposure quality targets include:

| Metric | Target |
|---|---:|
| Population WAPE | <= 20% |
| Population Spearman | >= 0.80 |

These population metrics are reported as secondary exposure-quality diagnostics and do not replace the primary F1 >= 0.85 spatial-accuracy acceptance gate.

---

Open:

```text
prep_api/validation/output/population_validation_by_barangay.csv
```

This shows the comparison per barangay and helps identify where modeled and reference exposure differ most.

---

Finally, open:

```text
prep_api/validation/output/ulysses_validation_report.txt
```

This contains the summarized validation result.

---

# 8. Important Interpretation Rules

The Ulysses validation must be interpreted carefully.

## Do not call this machine-learning accuracy

PREP is currently using deterministic GIS-based flood exposure calculations.

The results describe **historical spatial and exposure agreement**, not ML training accuracy.

## Do not interpret the best scenario as the true Ulysses return period

The script evaluates combinations of:

```text
5-Year
25-Year
100-Year
```

with:

```text
Low+
Medium+
High
```

The best-scoring combination is a descriptive sensitivity result.

It does not prove that Ulysses was actually that return-period scenario.

## Derived population exposure is not a direct count of victims

The observed population reference is calculated as:

```text
PSA 2020 barangay population
×
NAMRIA observed flood-area fraction
```

and:

```text
PSA 2020 child population
×
NAMRIA observed flood-area fraction
```

This assumes population is uniformly distributed within each barangay.

It is not a direct count of:

- victims,
- evacuees,
- casualties,
- households actually flooded, or
- officially reported affected persons.

## Areas outside the NAMRIA mapped flood extent are not automatically safe

NAMRIA survey/map coverage is incomplete.

A zero overlap means:

```text
No overlap with the digitized NAMRIA mapped extent
```

It does not necessarily mean:

```text
Confirmed dry or safe
```

---

# 9. What Each Peer Must Return

Return the following files after completing the validation:

```text
validation/input/observed_flood.gpkg
validation/input/observed_barangay_flood_fraction.csv

validation/output/spatial_validation_scenarios.csv
validation/output/population_validation_scenarios.csv
validation/output/population_validation_by_barangay.csv
validation/output/ulysses_validation_report.txt
```

Rename them using your evaluator identifier.

Example:

```text
Peer01_observed_flood.gpkg
Peer01_observed_barangay_flood_fraction.csv
Peer01_spatial_validation_scenarios.csv
Peer01_population_validation_scenarios.csv
Peer01_population_validation_by_barangay.csv
Peer01_combined_validation_scenarios.csv
Peer01_validation_decision.csv
Peer01_ulysses_validation_report.txt
```

---

# 10. Recommended Peer Notes

Along with the files, provide a short note containing:

```text
Evaluator:
Date:
R version:
RStudio version:

Number of outer polygons traced:
Number of exclusion holes traced:

Observed flood area reported by script:

Any difficult or uncertain tracing areas:

Any warnings/errors encountered:
```

This helps compare results between independent evaluators.

---

# 11. Complete Run Order

```text
1. Extract RescuAR.Admin
        ↓
2. Open RStudio
        ↓
3. Install required packages
        ↓
4. Run:
   01_rescuar_restart_from_tracing.R
        ↓
5. Independently trace NAMRIA Ulysses flood extent
        ↓
6. Check final magenta outline
        ↓
7. Verify demographics_2020.csv
        ↓
8. Run:
   02_rescuar_validation_steps_3_to_5.R
        ↓
9. Review generated validation CSVs
        ↓
10. Review ulysses_validation_report.txt
        ↓
11. Rename and return required files
```

---

## Validation Scope

This peer exercise primarily evaluates the **reproducibility of the manual tracing and downstream PREP validation results**.

Because all peers use the same already-georeferenced NAMRIA TIFF, this exercise does **not independently repeat the original GCP/georeferencing process**.

The accepted georeferencing is treated as a common starting point for all evaluators.

---

# Optional Improvement Workflow

## A. Build a peer-consensus reference

After two or more peers finish their own independent traces, copy only their final flood geometries into:

```text
validation/peer_traces/
```

Recommended filenames:

```text
Peer01_observed_flood.gpkg
Peer02_observed_flood.gpkg
Peer03_observed_flood.gpkg
```

Run:

```text
validation/scripts/03_build_peer_consensus.R
```

It calculates pairwise IoU between evaluators and produces a strict-majority consensus flood extent. The consensus is **not** automatically promoted to the active reference. Review the agreement results first.

## B. Add multiple historical events

Register independent events in:

```text
validation/events/events.csv
```

See `validation/events/README.md` for required fields and the required separation between `calibration`, `validation`, and `test` events.

## C. Controlled scenario-rule calibration

After multiple independent historical events are available, run:

```text
validation/scripts/04_calibrate_scenario_rules.R
```

This evaluates all legitimate LiPAD 5/25/100-year × Low/Medium/High combinations. It **does not** synthesize ground truth and does not repeatedly change observations until a target score is reached.

The selected candidate mapping is saved as:

```text
validation/calibration/selected_rules_unapproved.json
```

It remains unapproved and does not alter production.

## D. Production promotion gate

Only after adequate calibration and independent holdout events exist, review:

```text
validation/calibration/multi_event_calibration_report.txt
validation/calibration/holdout_selected_rule_scores.csv
```

Then use:

```text
validation/scripts/05_promote_calibrated_rules.R
```

The script defaults to:

```r
APPROVE_PROMOTION <- FALSE
```

and blocks promotion if the configured minimum event counts or holdout requirements are not satisfied. An approved configuration is written to:

```text
prep_api/config/exposure_scenario_rules.json
```

The PREP API exposes that mapping through:

```text
GET /v1/exposure/rules
```

and the Admin Flood Hazard Exposure page loads it automatically. If no approved configuration exists, the application remains explicitly labeled as using the provisional mapping.

## E. Optional observation-domain mask

If the NAMRIA/post-event reference only covers a documented survey area, place either:

```text
validation/input/observation_domain.gpkg
```

or:

```text
validation/input/observation_domain.geojson
```

The main validation script will restrict spatial comparisons to that domain so unobserved areas are not automatically treated as confirmed dry.

## F. Optional population-distribution weighting

The PREP API now supports an optional layer inside `data/spatial/baseline_spatial.gpkg` named one of:

```text
population_weights
population_grid
population_density
```

The layer must contain a population weighting field such as `population_weight`, `population_2024`, or `population`. A child weighting field is optional. When present, PREP uses within-barangay spatial weighting instead of uniform area weighting. When absent, the existing area-weighted method remains the fallback.
