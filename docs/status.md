# Known implementation and evidence gaps

Source review: 2026-11-03, starting at `dev` revision `b3090cc`.
This is a source-based status register. It is not a fresh build, deployment,
database or device acceptance report. Update a row only with the corresponding
source fix and evidence; link the task record that establishes the new state.

| Area | Known gap and source evidence | Required closure |
| --- | --- | --- |
| Admin build | [Sidebar.jsx](../RescuAR.Admin/src/components/Sidebar.jsx) still contains committed conflict markers at lines 2 and 196. The earlier documentation task reached Vite and failed there; this review reconfirmed the markers without rerunning a build. | Resolve the intended source merge and complete the relevant frontend checks. See [Admin setup](../RescuAR.Admin/README.md). |
| Database bootstrap/fixtures | Audit foundation migration 001, baseline fixture and original inspection/verification/review artifacts are absent. Migration 002 requires specific foundation contracts. | Recover/review the real prerequisites and validate against the intended target; a guessed replacement or PGlite installation alone is insufficient. See [database guide](../RescuAR.Admin/database/README.md). |
| PREP automatic rule loading | The frontend requests `/v1/exposure/rules`, absent from the checked-in `plumber.R`. Definitions/scenario metadata contains configuration, but that does not complete the dedicated loader. | Align the API/client contract and establish acceptance for the actual service/configuration. See [API guide](../RescuAR.Admin/prep_api/README_IMPLEMENTATION.md). |
| Admin operational claims | System Settings and the general Notify Residents modal use local simulation/state. System Logs shows sample entries. Outbound SMS uses a separate queue/function/handset path. | Treat each path according to its actual implementation and confirmed outcome; queueing/submission is not recipient delivery. See [task inventory](../RescuAR.Admin/database/audit-task-inventory.md). |
| AR/device acceptance | Local source, checks and package evidence do not establish sustained multi-device recovery or physical placement accuracy. | Retest the exact candidate with the [field procedure](field-testing/README.md) and close findings with the required evidence. |
| Runtime/provider state | Deployed routing-data parity, authenticated database behavior, SMS delivery and AI-client instruction discovery are not certified by documentation edits. | Collect evidence from the actual target when authorized; record source, package, device and hosted results separately. |

The [documentation-alignment record](changes/2026-11-03-documentation-alignment.md)
preserves the earlier check results. Historical passing counts must not be
presented as a new test run or silently used to close these rows.
