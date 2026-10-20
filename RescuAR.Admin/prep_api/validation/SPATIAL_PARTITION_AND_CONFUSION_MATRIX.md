# Ulysses spatial partition + confusion matrix (PREP)

## Run order (RStudio, working directory inside RescuAR.Admin)

1. Apply the patch, preserving directory paths. It includes a **frozen spatial partition**, generated **before evaluating the partition metrics** using a coordinate-only rule. Do **not** regenerate it to improve scores.
2. Run `prep_api/validation/scripts/02_rescuar_validation_steps_3_to_5.R` from the **beginning** using Source (not a highlighted fragment).
3. Read `prep_api/validation/output/ulysses_validation_report.txt` and the CSVs below.
4. Run `06_create_spatial_partition.R` **only** when creating a new, explicitly versioned partition for an appropriately documented new study. By default, it refuses to replace the existing frozen partition. If inputs change, investigate rather than silently regenerate.
5. Do **not** use `04_calibrate_scenario_rules.R` or `05_promote_calibrated_rules.R` based on these partition scores; true future-event independent test data are still missing.

## Files

- `partitions/ulysses_spatial_partition.gpkg`: layer `spatial_partition`, individual 1 km UTM tiles clipped to the city. Fields: `block_id`, `grid_col`, `grid_row`, `subset`, `area_km2`.
- `partitions/ulysses_spatial_partition_manifest.csv`: coordinate-only selection method, fixed seed, observation domain, source checksum, and retrospective status.
- `output/spatial_partition_confusion_matrix.csv`: **27 rows** (9 scenarios × full-city/development/holdout), TP/FP/FN/TN in **km²**, F1, precision, recall, IoU, specificity and conditional area accuracy, pass flags, threshold gaps.
- `output/spatial_partition_acceptance_review.csv`: explicit PASS/FAIL and target gaps for each scenario and geographic subset.
- `output/spatial_partition_population_proxy.csv`: **18 rows**, development/holdout population *proxy* WAPE and Spearman. Not independently observed affected-person counts. Not comparable to event-year headcounts.
- `output/spatial_partition_snapshot_review.txt`: checked against the supplied 2026-10-14 data snapshot, not a claim of new independent-event validation.

## Method

- Fixed projected coordinate system EPSG:32651 (metres).
- Block size: **1 km × 1 km** aligned to UTM kilometre coordinates.
- Holdout blocks selected by deterministic hash-like ranking of grid row/column and a **predeclared seed**. Allocation does **not** use flood masks, hazard predictions, or validation results. The selected holdout is about **20.15% by area**, not 20% of barangays or random pixels.
- 33 development blocks, 9 holdout blocks. Geographies do not overlap in positive area and cover 100% of the analysis domain.
- Full-city results are retained unchanged. They MUST match recalculated TP/FP/FN/F1/IoU from the 2×2 confusion-matrix procedure.
- For each subset, model and reference polygons are clipped to the *same* analysis polygon. `TN = domain area - (TP + FP + FN)`.

### Interpretation and limitations

**This is a retrospective within-Ulysses spatial robustness check, not an untouched independent test.** The original Ulysses data were previously used to identify 100-Year/Low+ as the best descriptive scenario; examining this event again cannot prove generalization to independent events. Do not select a new winning rule using holdout performance. More robust future validation requires independent event data kept separate from calibration.

If no independently documented observation-domain layer exists, this pipeline uses the full city. **TN and overall area accuracy are conditional quantities**, not confirmed absence of flooding outside mapped NAMRIA observations. F1/precision/IoU also depend on the chosen coverage; document the limitation.

Population exposure comparisons here use PSA 2020 counts allocated by each barangay's intersected flooded fraction, **not field-measured exposed residents**. WAPE/Spearman measure *agreement between derived spatial proxies*. A new population raster/building distribution and independent exposure counts are needed for stronger conclusions.

### Study-defined acceptance thresholds

| Metric | Required | Interpretation |
|---|---:|---|
| F1 | ≥ 0.85 | **Primary** spatial acceptance |
| Precision | ≥ 0.80 | Supporting |
| Recall | ≥ 0.80 | Supporting |
| IoU/CSI | ≥ 0.70 | Supporting |
| Population WAPE | ≤ 20% | Supporting, **derived proxy** |
| Population Spearman | ≥ 0.80 | Supporting, **derived proxy** |

`accuracy_rate_met` is the **primary F1 result**. `supporting_targets_met` shows combined supporting diagnostics. `all_targets_met` now means **all** six criteria pass. A primary F1 PASS must not be described as passing all supporting criteria.

The original Ulysses 9-scenario comparison is a *sensitivity analysis*: a good match does **not** independently identify Ulysses as a 100-year flood event.
