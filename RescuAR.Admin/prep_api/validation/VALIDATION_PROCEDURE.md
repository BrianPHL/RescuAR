# PREP Flood Hazard Exposure Validation Procedure

This validation procedure separates **implementation fidelity** from **model validity**. A dashboard can reproduce PREP perfectly while the underlying reference-hazard exposure model still has uncertainty.

## 1. What can be validated

### A. Internal consistency / implementation fidelity
Expected result: **100% pass**.

Checks performed by `validate_exposure.R`:
- city estimated exposed population equals the sum of barangay estimates;
- city estimated exposed children equals the sum of barangay estimates;
- mapped barangay count reconciles with the barangay rows;
- all baseline barangays are represented;
- facility results are unique by OSM ID;
- hazard-area percentages remain between 0 and 100%;
- exposure estimates are non-negative.

These tests prove the calculations are internally consistent. They do **not** prove that a historical flood actually matched the hazard map.

### B. Historical spatial validation
Use an **independent georeferenced observed flood extent** for a historical event. Examples include field-survey inundation polygons, an independently produced post-event flood map, or a validated satellite-derived flood extent.

The script calculates:
- **Precision** = TP / (TP + FP)
- **Recall** = TP / (TP + FN)
- **F1** = 2 × Precision × Recall / (Precision + Recall)
- **IoU / CSI** = TP / (TP + FP + FN)

For disaster-response use, Recall is particularly important because false negatives represent observed flooded areas omitted by the model/reference scenario.

### C. Population exposure validation
Provide an independent barangay-level reference dataset measuring the same concept as PREP: **persons spatially exposed to flooding/inundation**.

The script calculates:
- MAE;
- RMSE;
- WAPE;
- Spearman rank correlation;
- Top-N ranking overlap.

Do not directly treat evacuees, casualties, displaced persons, or generic incident-report “affected persons” as ground truth for PREP's area-weighted exposed-population estimate unless the definitions are demonstrably equivalent.

## 2. Project acceptance targets

The following are recommended **project-defined targets**, not universal Philippine regulatory requirements:

| Metric | Project target |
|---|---:|
| Internal consistency | 100% |
| Flood-extent Recall | >= 0.90 |
| Flood-extent Precision | >= 0.80 |
| F1 | >= 0.85 |
| IoU / CSI | >= 0.70 |
| Population WAPE | <= 20% |
| Spearman rank correlation | >= 0.80 |

Declare the targets in the validation protocol/manuscript **before** reviewing the final scores. A result below target should trigger investigation rather than being silently reclassified as acceptable.

## 3. Prepare independent population reference data

Copy:

`validation/input/observed_barangay_exposure_TEMPLATE.csv`

to:

`validation/input/observed_barangay_exposure.csv`

Fill `observed_exposed_population` for the historical event. Fill `observed_exposed_children` only if a comparable child-exposure reference exists.

Required columns:
- `barangay_name`
- `observed_exposed_population`

Optional provenance columns are already included in the template.

## 4. Prepare observed flood extent

Place a georeferenced file such as:

`validation/input/observed_flood.geojson`

or a GeoPackage/Shapefile in the same folder.

Requirements:
- valid CRS must be defined;
- geometry must represent **observed flood extent**, not another copy of the UP DREAM/LiPAD reference hazard layer;
- the event should be sufficiently comparable to the 5-, 25-, or 100-year reference scenario being tested;
- document event date, mapping method, source organization, spatial resolution, and known limitations.

## 5. Run validation in PowerShell

From `RescuAR.Admin\prep_api`:

```powershell
$env:VALIDATION_RETURN_PERIOD="25"
$env:VALIDATION_MINIMUM_HAZARD="Medium"
$env:VALIDATION_OBSERVED_BARANGAY_CSV="validation/input/observed_barangay_exposure.csv"
$env:VALIDATION_OBSERVED_FLOOD_PATH="validation/input/observed_flood.geojson"

Rscript .\validation\validate_exposure.R
```

For a GeoPackage layer, also set:

```powershell
$env:VALIDATION_OBSERVED_FLOOD_LAYER="observed_flood"
```

If only internal consistency is being checked, leave the independent-reference paths absent and run:

```powershell
Rscript .\validation\validate_exposure.R
```

## 6. Review generated outputs

The script writes to `validation/output/`:
- `internal_consistency_checks.csv`
- `internal_consistency_summary.csv`
- `barangay_population_comparison.csv` when population reference data are available
- `population_validation_metrics.csv` when population reference data are available
- `spatial_validation_metrics.csv` when an observed flood extent is available
- `spatial_validation_layers.gpkg` for GIS inspection
- `validation_report.txt`

Open `spatial_validation_layers.gpkg` in QGIS to inspect:
- model/reference hazard footprint;
- observed flood footprint;
- true-positive area;
- false-positive area;
- false-negative area.

## 7. Repeat across multiple events

A single historical flood only establishes event-specific agreement. Repeat the procedure across multiple independent events when possible, preferably covering different severities. Report each event separately and then summarize the distribution/average of metrics.

## 8. How to describe the result

Use wording such as:

> For the historical event tested, the selected reference hazard scenario achieved a Recall of X, Precision of Y, F1 of Z, and IoU/CSI of W against the independent observed flood extent. Barangay-level exposed-population estimates had a WAPE of P% and Spearman rank correlation of R. These values represent validation against the selected historical reference and do not imply a universal live-prediction accuracy rate.
