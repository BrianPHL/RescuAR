# Historical Event Data Implementation

## Implemented source events

### 1. Typhoon Ketsana / Ondoy 2009 — Tumana

Files:

```text
validation/events/ondoy_2009_tumana/
├── README.md
├── event_metadata.json
├── source_evidence.csv
└── source_map_ondoy_2009.png
```

The source map is Espina (2018), Figure 4.1.1.7. The paper reports that the eastern portion of Tumana was inundated by more than 2.5 m during Ondoy and reports a 22.8 m Marikina River level on September 26, 2009.

### 2. 2013 Monsoon Rains — Tumana

Files:

```text
validation/events/monsoon_2013_tumana/
├── README.md
├── event_metadata.json
├── source_evidence.csv
├── source_map_monsoon_2013_depth.png
└── source_map_frequently_flooded.png
```

The event-specific source is Espina (2018), Figure 4.1.1.5. The paper also reports greater than 2 m inundation in eastern Tumana during the 2013 monsoons. Figure 4.1.1.6 is stored separately as supporting recurrent-flood context.

## Shared context added from the Ecological Profile

```text
validation/events/context/
├── README.md
├── ecological_profile_population_2020.csv
├── ecological_profile_city_context.csv
├── ecological_profile_flood_hazard_inventory_2018.csv
├── espina_historical_river_levels.csv
└── espina_tumana_household_flood_reference.csv
```

The context includes 2020 barangay population/household/density data, city topographic and river descriptors, administrative flood-hazard records, historical river levels reported by Espina, and Tumana household flood-impact evidence.

## Important modeling rule

These new sources do **not** automatically alter LiPAD hazard polygons or the active NAMRIA/Ulysses reference.

The Espina source figures do not contain native GIS georeferencing metadata, so they remain `source_ready_digitization_pending`. They must be independently georeferenced, digitized, and QC-checked before being assigned an `observed_flood_path` and used for spatial metrics.

The Ecological Profile 2020 population data are modern context and cannot be treated as 2009 or 2013 event-year demographics.

## Calibration-script behavior

`04_calibrate_scenario_rules.R` now:

- registers both ready and pending historical events;
- writes `calibration/event_registry_status.csv`;
- skips source-only events until an observed geometry exists;
- supports spatial-only events with no demographics;
- leaves population metrics as `NA` when historical demographics are unavailable;
- renormalizes calibration scoring across available metrics rather than penalizing missing population metrics as zero skill.
