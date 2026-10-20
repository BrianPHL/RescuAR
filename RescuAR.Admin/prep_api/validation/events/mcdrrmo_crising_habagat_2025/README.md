# Crising–Habagat — Marikina 2025

- **Original source:** `CRISING HABAGAT MAP 072225.PNG` supplied within 2026-10-19 MCDRRMO dataset collection.
- **Stored source:** `source_map.png` (original image bytes unchanged).
- **Event period:** 2025-07-21.
- **Image:** 566 × 792 pixels.
- **SHA-256:** `f251f98e8c705797f9535143eb1f80e3c35e1b62f17123190a607bd4190767c0`.
- **Priority:** medium.
- **Status:** source only; no approved observed polygons or verified observation-domain geometry.

## Interpretation and caution

Very small raster; flood categories visible but difficult to trace precisely. Request original GIS or higher-resolution version.

Title says Flood Map (Typhoon Crising-Habagat), July 21, 2025; filename 072225 may represent July 22, 2025 and needs clarification.

The displayed geographic boundary must not automatically be treated as the complete observation-coverage mask. The map must first be verified as **observed inundation rather than simulated or river-stage scenario mapping**. Ask the source agency for native flood-extent polygons, a documented survey boundary, event-level river-stage information, georeferencing and map methodology.

## Optional digitization workflow

1. Prefer original GIS flood polygons and coverage polygons from MCDRRMO.
2. Otherwise, georeference independently using road/river features and at least six withheld checkpoints. Do **not** align using PREP/LiPAD hazard output.
3. Trace inundated areas separately from river channels, roads and legend symbols; preserve the original reference image.
4. Place `candidate_observed_flood.gpkg` (layer `observed_flood`) and `candidate_observation_domain.gpkg` (layer `observation_domain`) in this event folder.
5. Complete `registration_checkpoints.csv` and `qc_review.json` using the templates in `validation/events/mcdrrmo_intake_templates/`.
6. Run `08_approve_mcdrrmo_reference.R` with `PREP_EVENT_ID=mcdrrmo_crising_habagat_2025`; approval will update the event registry *only if all checks pass*.

Until approval, this event stays `reference_only` and cannot affect current Ulysses metrics or production flood-hazard layers.
