# Enhanced Southwest Monsoon / Carina — Marikina 2024

- **Original source:** `Carina Map.jfif` supplied within 2026-10-19 MCDRRMO dataset collection.
- **Stored source:** `source_map.jfif` (original image bytes unchanged).
- **Event period:** 2024-07-24.
- **Image:** 7200 × 9900 pixels.
- **SHA-256:** `053a4990084f7a8d39cdd0952614269ff5689862a28edd20957c4788af87b556`.
- **Priority:** high.
- **Status:** source only; no approved observed polygons or verified observation-domain geometry.

## Interpretation and caution

Map legend shows inundation depth bins approximately 0.1 to above 3.51 metres; confirm surveyed vs modelled method with issuing agency.

Map title names Enhanced Southwest Monsoon (Carina); preparation date visible as July 27, 2024.

The displayed geographic boundary must not automatically be treated as the complete observation-coverage mask. The map must first be verified as **observed inundation rather than simulated or river-stage scenario mapping**. Ask the source agency for native flood-extent polygons, a documented survey boundary, event-level river-stage information, georeferencing and map methodology.

## Optional digitization workflow

1. Prefer original GIS flood polygons and coverage polygons from MCDRRMO.
2. Otherwise, georeference independently using road/river features and at least six withheld checkpoints. Do **not** align using PREP/LiPAD hazard output.
3. Trace inundated areas separately from river channels, roads and legend symbols; preserve the original reference image.
4. Place `candidate_observed_flood.gpkg` (layer `observed_flood`) and `candidate_observation_domain.gpkg` (layer `observation_domain`) in this event folder.
5. Complete `registration_checkpoints.csv` and `qc_review.json` using the templates in `validation/events/mcdrrmo_intake_templates/`.
6. Run `08_approve_mcdrrmo_reference.R` with `PREP_EVENT_ID=mcdrrmo_carina_2024`; approval will update the event registry *only if all checks pass*.

Until approval, this event stays `reference_only` and cannot affect current Ulysses metrics or production flood-hazard layers.
