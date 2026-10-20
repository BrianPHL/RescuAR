# Typhoon Falcon — Marikina 2011

- **Original source:** `FALCON.jpg` supplied within 2026-10-19 MCDRRMO dataset collection.
- **Stored source:** `source_map.jpg` (original image bytes unchanged).
- **Event period:** 2011-06-24/2011-06-25.
- **Image:** 7200 × 9897 pixels.
- **SHA-256:** `3e875258d03ccce410254cb1222038e0265473ef92809616985235431448f8a2`.
- **Priority:** high.
- **Status:** source only; no approved observed polygons or verified observation-domain geometry.

## Interpretation and caution

Multi-colour flood-level classes. Legend categories are heterogeneous; do not map colours to standardized LiPAD classes without source verification.

Map title says Marikina Flood Map Due To Typhoon Falcon, June 24-25, 2011.

The displayed geographic boundary must not automatically be treated as the complete observation-coverage mask. The map must first be verified as **observed inundation rather than simulated or river-stage scenario mapping**. Ask the source agency for native flood-extent polygons, a documented survey boundary, event-level river-stage information, georeferencing and map methodology.

## Optional digitization workflow

1. Prefer original GIS flood polygons and coverage polygons from MCDRRMO.
2. Otherwise, georeference independently using road/river features and at least six withheld checkpoints. Do **not** align using PREP/LiPAD hazard output.
3. Trace inundated areas separately from river channels, roads and legend symbols; preserve the original reference image.
4. Place `candidate_observed_flood.gpkg` (layer `observed_flood`) and `candidate_observation_domain.gpkg` (layer `observation_domain`) in this event folder.
5. Complete `registration_checkpoints.csv` and `qc_review.json` using the templates in `validation/events/mcdrrmo_intake_templates/`.
6. Run `08_approve_mcdrrmo_reference.R` with `PREP_EVENT_ID=mcdrrmo_falcon_2011`; approval will update the event registry *only if all checks pass*.

Until approval, this event stays `reference_only` and cannot affect current Ulysses metrics or production flood-hazard layers.
