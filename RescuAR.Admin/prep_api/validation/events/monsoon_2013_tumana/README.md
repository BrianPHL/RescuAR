# 2013 Monsoon — Tumana historical event source

This folder contains source evidence extracted from Espina (2018), including the event-specific flood-depth map and a separate supporting map of frequently flooded areas.

**Current status:** `source_ready_digitization_pending`.

The exact 2013 monsoon date is not stated in the source. Page 9 contains two related map panels whose figure titles and map captions are internally inconsistent: one panel carries flood-depth classes and a bottom caption referring to the 2013 monsoon, while the other shows categorical flooded areas and a bottom caption referring to frequently flooded areas. Both panels are retained for evaluator review. Do not merge them automatically into one observed geometry.

After QC:

1. Georeference `source_map_monsoon_2013_depth.png`.
2. Digitize only the event-specific flood extent into `observed_flood.gpkg` / layer `observed_flood`.
3. Create `observation_domain.gpkg` for the mapped Tumana coverage when possible.
4. Update `../events.csv` and set `event_status=ready`.
5. Keep this event spatial-only unless historical demographic data for 2013 are obtained.

The Ecological Profile 2020 data are contextual/cross-check information, not 2013 demographics.
