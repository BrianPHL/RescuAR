# Legacy validation files

These files belong to the earlier single-scenario validation workflow and are retained only for audit/history.

The current workflow is under `../scripts/`:

1. `01_rescuar_restart_from_tracing.R`
2. `02_rescuar_validation_steps_3_to_5.R`
3. `03_build_peer_consensus.R` (optional, when multiple independent peer traces are available)
4. `04_calibrate_scenario_rules.R` (multi-event controlled calibration)
5. `05_promote_calibrated_rules.R` (manual gated promotion only)

Do not use the legacy script as the primary peer-evaluation workflow.
