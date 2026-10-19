# Enhanced Southwest Monsoon / Maymay — Marikina 2026 August 9–10

- **Original source:** `SOUTHWEST MONSOON.jpeg` supplied within 2026-10-19 MCDRRMO dataset collection.
- **Stored source:** `source_map.jpeg` (original image bytes unchanged).
- **Event period:** 2026-08-09/2026-08-10.
- **Image:** 1489 × 2048 pixels.
- **SHA-256:** `f30a11a68fcaadc3e699d5e323e10fec4ecaa8d3fc697c82699b8a6c47f97f64`.
- **Priority:** conditional.
- **Status:** source only; no approved observed polygons or verified observation-domain geometry.

## Interpretation and caution

Legend identifies Flooded Areas - Level 16.00 MTRS; this likely refers to flood/river-stage scenario, not local water depth. Need confirmation before spatial evaluation.

Map title says Enhanced Southwest Monsoon (Maymay) August 9–10, 2026.

The displayed geographic boundary must not automatically be treated as the complete observation-coverage mask. The map must first be verified as **observed inundation rather than simulated or river-stage scenario mapping**. Ask the source agency for native flood-extent polygons, a documented survey boundary, event-level river-stage information, georeferencing and map methodology.

## Optional digitization workflow

1. Prefer original GIS flood polygons and coverage polygons from MCDRRMO.
2. Otherwise, georeference independently using road/river features and at least six withheld checkpoints. Do **not** align using PREP/LiPAD hazard output.
3. Trace inundated areas separately from river channels, roads and legend symbols; preserve the original reference image.
4. Place `candidate_observed_flood.gpkg` (layer `observed_flood`) and `candidate_observation_domain.gpkg` (layer `observation_domain`) in this event folder.
5. Complete `registration_checkpoints.csv` and `qc_review.json` using the templates in `validation/events/mcdrrmo_intake_templates/`.
6. Run `08_approve_mcdrrmo_reference.R` with `PREP_EVENT_ID=mcdrrmo_maymay_2026_08_09`; approval will update the event registry *only if all checks pass*.

Until approval, this event stays `reference_only` and cannot affect current Ulysses metrics or production flood-hazard layers.
