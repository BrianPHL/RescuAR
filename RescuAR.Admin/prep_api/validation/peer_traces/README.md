# Independent peer traces

Place each evaluator's independently generated `observed_flood.gpkg` or GeoJSON here **after** they finish tracing in their own copy of the project.

Recommended filenames:

- `Peer01_observed_flood.gpkg`
- `Peer02_observed_flood.gpkg`
- `Peer03_observed_flood.gpkg`

Then run `validation/scripts/03_build_peer_consensus.R`.

The script calculates pairwise IoU between peer traces and creates a strict-majority consensus reference at a configurable grid resolution. It does **not** automatically replace the active validation reference unless `PROMOTE_CONSENSUS_TO_ACTIVE <- TRUE` is explicitly set in the script.
