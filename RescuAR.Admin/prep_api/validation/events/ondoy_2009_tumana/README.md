# Ondoy 2009 — Tumana historical event source

This folder contains source evidence extracted from Espina (2018), Figure 4.1.1.7.

**Current status:** `source_ready_digitization_pending`.

The PNG is not natively georeferenced. It must be georeferenced and the colored flood extent digitized before this event can be evaluated as an `observed_flood` geometry. Do not infer areas outside the mapped Tumana coverage as dry.

After QC:

1. Save the independent geometry as `observed_flood.gpkg` with layer `observed_flood`.
2. Save the mapped/surveyed coverage as `observation_domain.gpkg` where possible.
3. Update `../events.csv` for this event with the paths and set `event_status=ready`.
4. Keep `role=reference_only` unless an independent historical trigger state and a defensible calibration/holdout split are established.

The Ecological Profile context files are modern contextual/cross-check data. They must not be used as if they were 2009 household/population ground truth.
