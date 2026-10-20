# Enhanced Southwest Monsoon — Marikina 2026 August 17

- **Original source:** `SOUTHWEST MONSOON HABAGAT MAYMAY.jpeg` supplied within 2026-10-19 MCDRRMO dataset collection.
- **Stored source:** `source_map.jpeg` (original image bytes unchanged).
- **Event period:** 2026-08-17.
- **Image:** 2048 × 1489 pixels.
- **SHA-256:** `921366212030c214cf61d41d348d1e76e37f2268fcbbfb0beaaf7828efb12062`.
- **Priority:** conditional.
- **Status:** source only; no approved observed polygons or verified observation-domain geometry.

## Interpretation and caution

Legend includes flood levels and water-level label of 16.9 metres; distinguish river stage from inundation depth. Not assumed an independent surveyed polygon.

Map title says Flooded Map Enhanced Southwest Monsoon August 17, 2026. Duplicate - Copy.jpeg excluded (same SHA-256).

The displayed geographic boundary must not automatically be treated as the complete observation-coverage mask. The map must first be verified as **observed inundation rather than simulated or river-stage scenario mapping**. Ask the source agency for native flood-extent polygons, a documented survey boundary, event-level river-stage information, georeferencing and map methodology.

## Optional digitization workflow

1. Prefer original GIS flood polygons and coverage polygons from MCDRRMO.
2. Otherwise, georeference independently using road/river features and at least six withheld checkpoints. Do **not** align using PREP/LiPAD hazard output.
3. Trace inundated areas separately from river channels, roads and legend symbols; preserve the original reference image.
4. Place `candidate_observed_flood.gpkg` (layer `observed_flood`) and `candidate_observation_domain.gpkg` (layer `observation_domain`) in this event folder.
5. Complete `registration_checkpoints.csv` and `qc_review.json` using the templates in `validation/events/mcdrrmo_intake_templates/`.
6. Run `08_approve_mcdrrmo_reference.R` with `PREP_EVENT_ID=mcdrrmo_swm_2026_08_17`; approval will update the event registry *only if all checks pass*.

Until approval, this event stays `reference_only` and cannot affect current Ulysses metrics or production flood-hazard layers.
