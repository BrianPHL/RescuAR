# Documentation index

Reviewed against source on 2026-11-03, starting at `dev` revision `d83d797`.
Use the guides below for current behavior. Dated investigations preserve the
observations, candidate hashes and verification limits of their own revision;
their old commands or results are not current deployment evidence.

## Current guides

| Topic | Guide |
| --- | --- |
| Repository layout, prerequisites and builds | [Project README](../README.md) |
| Frontend, telemetry worker and environment variables | [Admin README](../RescuAR.Admin/README.md) |
| Available SQL and missing database prerequisites | [Database guide](../RescuAR.Admin/database/README.md) |
| Active admin actions and audit coverage limits | [Task inventory](../RescuAR.Admin/database/audit-task-inventory.md) |
| PREP API configuration and implemented endpoints | [API guide](../RescuAR.Admin/prep_api/README_IMPLEMENTATION.md) |
| Research validation and promotion limits | [Validation workflow](../RescuAR.Admin/prep_api/validation/VALIDATION_WORKFLOW.md) |
| SIM SMS gateway setup and APK workflow | [SMS guide](../RescuAR.Admin/SMS_GATEWAY_SETUP.md) |
| A*/MLD routing policy and datasets | [Routing guide](../RescuAR/Navigation/Routing/README.md), [dataset import](../RescuAR/Navigation/Data/QC_IMPORT.md) |
| Android candidate packaging and device retest | [Field procedure](field-testing/README.md) |
| Pinned Android dependencies and Depth policy | [Compatibility matrix](../ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md) |
| AR finding closure requirements | [Finding matrix](../ARCORE_FINAL_FINDING_VALIDATION_MATRIX.md) |

## Known implementation gaps

- The admin production build fails on pre-existing conflict markers in
  `RescuAR.Admin/src/components/Sidebar.jsx` at the reviewed revision. Describing
  source call paths does not establish a runnable frontend or hosted acceptance.
- The database checkout lacks foundation migration 001, its original inspection
  and verification SQL, several fixtures/tests and four historical review pages.
  Fresh-database bootstrap and the full database fixture suite are blocked.
- The PREP frontend's `GET /v1/exposure/rules` request has no checked-in server
  route. Automatic rule loading falls back to provisional mappings.
- System Settings and the general broadcast modal simulate completion; System
  Logs contains sample entries. The SMS console is a separate real queue path.
- Physical AR accuracy, sustained multi-device recovery, deployed data parity
  and end-to-end SMS delivery require their own evidence. Local builds cannot
  close those gaps.

## Historical records

The root `AR_*` and `ARCORE_BATCH_*` reports and files under `merge-analysis/`
record earlier investigations and integration decisions. Preserve their evidence
and dates; do not apply old ZIPs or treat old passing counts as current checks.
The [November 2 cadence recovery report](field-testing/2026-11-02-samsung-honor-cadence-recovery.md)
supersedes the earlier two-observation startup candidate described in the
[device reliability report](field-testing/2026-11-02-device-reliability.md) and
[route regression report](field-testing/2026-11-02-ar-route-regression.md).

The admin patch documents are historical package instructions. Current setup is
in the Admin README; original patch folders/payloads are not in this checkout.
