# Historical PAGASA research pipeline

This folder is intentionally separate from the production `syncWorker.js` flow.
The live worker maintains current telemetry in Supabase every five minutes; these scripts are manual research utilities for collecting and preparing historical sensor observations for model development.

## 1. Test archival retention first

From `RescuAR.Admin/scraper`:

```bash
npm run test:archive -- 202011121020 sto_nino_mmda sto_nino_secondary nangka montalban rodriguez
```

The target timestamp is in PAGASA `YYYYMMDDHHmm` format. If the endpoint does not return observations from 2020, do not fabricate them; use PAGASA/MMDA eFOI or another documented source instead.

## 2. Collect Ulysses 2020

```bash
npm run collect:ulysses
```

The collector:

- queries `/water/detail_list.do` at overlapping 12-hour windows;
- stores each raw source response under `raw/ulysses_2020/`;
- normalizes and deduplicates by `station_code + observed_at`;
- keeps only observations inside the requested event period;
- writes `processed/ulysses_2020_water_levels.csv` and JSON;
- preserves raw PAGASA `(*)` markers through `quality_flag` and `raw_water_level`.

Generated raw/processed/validation files are git-ignored by default because they are research outputs, not application source code.

## 3. Validate the collected sensor history

```bash
npm run validate:ulysses
```

This checks duplicates, timestamps, water-level ranges, interval gaps and documented Ulysses peak values. It produces reports under `validation/`.

The peak check is a **sensor-data sanity check**, not inundation-model accuracy.

## 4. Build hydrological model features

```bash
npm run build:ulysses-training
```

Outputs include:

- `ulysses_2020_hydrology_features.csv`
- `ulysses_2020_event_peak_features.csv`
- `ulysses_2020_barangay_training_candidates.csv`
- `ulysses_2020_barangay_training_labeled.csv`

Engineered features include 30-minute, 1-hour and 2-hour lags, level changes, rise rates and distance to source thresholds where available.

## Labels and model readiness

`labels/ulysses_2020_barangay_labels_PROVISIONAL.csv` contains only confirmed positive observations gathered during the earlier Ulysses dataset review. Unknown barangays remain blank and **must not be converted to `flooded=0`** merely because a report does not mention them.

A binary classifier is not ready until you have both positive and explicit negative ground-truth labels across multiple independent flood events. Do not use random timestamp splits from one typhoon as an accuracy test; use event-separated or leave-one-event-out validation.

## Optional PREP static features

If you have a PREP barangay feature CSV, merge it while building:

```bash
npm run build:training -- --event ulysses_2020 --barangay-features "C:/path/to/barangay_features_baseline.csv"
```

The script prefixes imported fields with `static_` to keep hydrological and spatial features distinguishable.
