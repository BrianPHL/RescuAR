# Documentation workflow

Applies to every task that modifies this repository. The goal is to keep current
instructions accurate and make each logical change traceable without duplicating
implementation details across reports.

## Required work for a change

1. Identify the affected current guides before editing. Check their claims against
   the owning source, configuration and available evidence.
2. Update the appropriate guide in the same task. If no public contract or operating
   instruction changes, say so in the change record; do not manufacture a guide edit.
3. Create or update one `docs/changes/YYYY-MM-DD-topic.md` record for the task.
   Cover every logical change, including internal refactors, tests, assets and
   documentation. Group related files under their purpose rather than narrate every
   line edit. Use the user's local date (Asia/Manila for this project).
4. Link new/revised documentation from the appropriate index. Record new visual
   references and confirmed design departures in `docs/design/`.
5. Check changed local links and whitespace. Run only verification relevant to the
   actual code change, following `AGENTS.md`. Report unresolved checks honestly.
6. Keep the record current through follow-ups. Separate later unrelated work into
   a new record. Never invent a commit ID or imply a local file was deployed.

A read-only investigation needs no repository record unless one is requested.
Do not backfill all Git history as part of routine work. Preserve existing dated
investigations as historical evidence and link them when they explain a change.

## Destinations

| Content | Destination |
| --- | --- |
| Durable agent rules and pointers | [AGENTS.md](../../AGENTS.md) |
| Project onboarding and common commands | [Root README](../../README.md) |
| Documentation navigation and current gaps | [docs/README.md](../README.md) |
| What changed, why, evidence and pending work | [docs/changes/](../changes/README.md) |
| Appearance baseline, source screenshots and confirmed exceptions | [docs/design/](../design/README.md) |
| Android field procedure and dated device evidence | [docs/field-testing/](../field-testing/README.md) |
| Historical merge/integration analysis | `docs/merge-analysis/` |
| Admin/service setup and feature limitations | [RescuAR.Admin/README.md](../../RescuAR.Admin/README.md) and the owning service guide |
| Database contracts, migration prerequisites and audit coverage | [RescuAR.Admin/database/](../../RescuAR.Admin/database/README.md) |
| PREP research, metrics and calibration constraints | [prep_api/validation/](../../RescuAR.Admin/prep_api/validation/VALIDATION_WORKFLOW.md) |
| Routing algorithms, data activation and provider contracts | [Routing guide](../../RescuAR/Navigation/Routing/README.md), [data import](../../RescuAR/Navigation/Data/QC_IMPORT.md) |
| New cross-project feature/architecture/operations guides | A clearly named subfolder under `docs/`, created when needed and linked from its index |

Keep a single current guide for each contract. A change record links to it rather
than copies it. Existing root `AR_*` and batch reports retain their dated context;
new investigation records belong in the appropriate `docs/` folder. If a file is
moved, update all local links and entry points in the same change.

## Change-record template

Use only relevant sections; keep the record proportionate to the task.

```markdown
# Short change title

Date: YYYY-MM-DD (Asia/Manila)
Starting source: branch/revision, if verified

## Changes and purpose

- Each logical change, its affected paths and why it was needed.
- Current guides updated, or why their contracts remain unchanged.

## Decisions and scope

- User choices, relevant tradeoffs and any confirmed visual departure.
- Scope limits and unresolved implementation gaps.

## Verification

- Exact relevant check and result: passed, failed, blocked or skipped.
- Source review versus host/package/device/deployed evidence.

## Follow-up

- Required manual steps, expected results and outstanding acceptance, if any.
```

A starting revision identifies the baseline, not the final artifact. If a commit
is later created, add its verified ID when useful. Record historical test results
as historical; never describe them as a new successful run.

## Accuracy rules

- Separate implemented source behavior, intended behavior and confirmed runtime
  behavior. A rendered prototype or screenshot is not proof of a backend feature.
- Keep setup commands tied to real paths and package scripts. Mark absent files,
  missing prerequisites, unavailable endpoints and known build blockers explicitly.
- Keep credentials, temporary passwords, private field coordinates and raw private
  captures out of change records. Link appropriately stored/redacted evidence.
- For UI changes, inspect the [visual guide](../design/README.md), name the relevant
  reference screens and note any shared resources/components reused. Record static
  comparison separately from any authorized application/device verification.
