# Trace markers and historical-event digitization

Two additional tracing aids are implemented.

## Ulysses tracing (`01_rescuar_restart_from_tracing.R`)

The tracing window now supports:

- a 250 m UTM coordinate grid;
- numbered **high-confidence boundary markers** selected before polygon tracing;
- the existing automated lime flood-edge guide;
- cyan barangay boundaries.

The yellow markers are chosen only from unambiguous locations on the independent NAMRIA reference map. They are saved to `validation/work/manual_trace_anchor_markers.csv` and displayed during the actual trace and final QC. They improve repeatability; they are not new ground truth and do not modify the flood reference automatically.

## Espina historical maps (`00_prepare_historical_event_reference.R`)

The utility uses two kinds of markers:

1. **Paired GCP markers** — a point is clicked on the source figure and then on the corresponding EPSG:32651 reference-map landmark. An affine transformation is fitted and RMSE/leave-one-out RMSE are reported.
2. **Flood-boundary markers** — numbered yellow X markers placed on clear flood-edge locations before tracing.

The historical geometry is only marked `ready` after the GCP QC passes and the evaluator explicitly types `YES` after reviewing the final transformed polygon.

These aids reduce coordinate-placement and repeatability error but cannot compensate for uncertainty in the original published map.
