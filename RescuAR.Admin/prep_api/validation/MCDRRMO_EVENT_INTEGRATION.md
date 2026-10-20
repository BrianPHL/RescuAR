# MCDRRMO historical maps — independent reference intake

The MCDRRMO historical map package is integrated into the **validation event registry only**. It is NOT an input to production PREP flood-hazard predictions, and no observed polygons are synthesized from image colouring or LiPAD output.

## Map classification and evidence uncertainty

| Event ID | Source | Year | Observed or scenario? | Ready? |
|---|---|---:|---|---|
| `mcdrrmo_carina_2024` | Carina Map.jfif | 2024 | Must be confirmed with source agency | No |
| `mcdrrmo_falcon_2011` | FALCON.jpg | 2011 | Must be confirmed with source agency | No |
| `mcdrrmo_crising_habagat_2025` | CRISING HABAGAT MAP 072225.PNG | 2025 | Must be confirmed with source agency; low-resolution image | No |
| `mcdrrmo_maymay_2026_08_09` | SOUTHWEST MONSOON.jpeg | 2026 | Map may represent river-stage scenario | No |
| `mcdrrmo_swm_2026_08_17` | SOUTHWEST MONSOON HABAGAT MAYMAY.jpeg | 2026 | Map may represent river-stage scenario | No |

Source map filenames are preserved in metadata; stored filenames are normalized within event directories. Hash digests in `mcdrrmo_source_catalog.csv` allow future source-integrity checks. There is one byte-for-byte duplicate August 17 map in the original archive; it is intentionally included only once.

## Proper research workflow

1. Ask MCDRRMO if the flood polygons were **surveyed/observed** or generated from scenario water levels, and ask for original GIS layers (and survey coverage) first.
2. If GIS exists, verify CRS, source/dates, map purpose, observed coverage and geometric validity. If only screenshots exist, use QGIS Georeferencer, independently matched stable landmarks and withheld control-point QC; do not use PREP predictions to guide placement.
3. Trace only reliably identifiable flooded areas, avoiding rivers, legends and backgrounds. Record uncertainty explicitly.
4. Create a separate valid surveyed/confirmed-dry `observation_domain` polygon; if no observation-domain evidence exists, **do not activate quantitative spatial evaluation**.
5. Complete `qc_review.json` and use script 08 to approve the reference. Prior to approval, the source remains pending and scripts cannot consider it ready.
6. Once reference-only event is ready, use script 04 to produce per-event **descriptive** hazard scenario comparisons. To use an event for calibration or test, first adopt a documented independent split and role assignment protocol.

## Validation thresholds (unchanged)

- Primary: F1 ≥0.85.
- Supporting: Precision ≥0.80, Recall ≥0.80, IoU/CSI ≥0.70, Population WAPE ≤20%, Population Spearman ≥0.80.
- Population references remain separate: these five maps do **not** supply observed affected-person counts.

## Why adding five maps does not automatically raise F1

PREP is a deterministic GIS hazard-exposure assessment, and its scenario polygons remain exactly as they were. Historical maps supply potential **independent reference** observations. Only after verifying source validity, geographic registration and coverage can those maps be used to calculate new event-specific F1 or confusion matrices. Do not aggregate flood maps from different dates into a synthetic ground truth. Any future calibration must be evaluated on a separate untouched event.
