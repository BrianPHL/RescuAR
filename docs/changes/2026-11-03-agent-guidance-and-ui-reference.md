# Agent guidance and UI appearance reference

Date: 2026-11-03 (Asia/Manila)
Starting source: `dev` at `15222e5`

## Changes and purpose

- Reorganized [AGENTS.md](../../AGENTS.md) into durable working, documentation,
  appearance, source, verification and handoff rules. Detailed setup remains in
  its owning guides instead of being duplicated in the agent instructions.
- Added the [documentation workflow](../contributing/documentation-workflow.md)
  requiring a proportional task record for every logical repository change,
  current-guide review/update and index/link maintenance.
- Added the [UI appearance guide](../design/README.md) and the original screenshot
  under `docs/design/reference/`. It captures the shared visual language for
  mobile and Admin while leaving layout and structure to the developer.
- Added the [change-record index](README.md), the prior
  [documentation-alignment record](2026-11-03-documentation-alignment.md) and this
  task record. Updated the main documentation index with the new destinations.

## User decisions and scope

- The supplied `Group 1.png` is the default appearance reference for additions and
  redesigns. It does not mandate layout, navigation structure or pictured behavior.
- A material proposed/requested visual departure needs one concrete comparison
  and confirmation: use the proposed appearance, or retain the screenshot style
  with the developer's layout. Independent work continues; the disputed appearance
  waits for that choice. Previously confirmed scope is not repeatedly questioned.
- Scope of this task: agent instructions, documentation organization and reference
  preservation. The screenshot's embedded copy is reference content, not a new
  behavioral instruction or proof of implementation.

## Verification

- Inspected representative mobile authentication/navigation/status and Admin
  screens through static crops of the full-resolution attachment.
- Compared styling entry points in MAUI resource dictionaries and Admin CSS;
  documented existing differences without declaring current visual conformity.
- Original image and repository copy: SHA-256
  `2d1fd06c83be0942384f9c17df79da0dd7f2533d7d4a438eb7c942ad9444df7c`,
  32768 × 10503 pixels, 28,144,383 bytes.
- Local-link check: 154 inline links across 79 Markdown files, no missing targets.
- Reference-integrity check: original and repository-copy SHA-256 match.
- `git diff --check`: passed; new Markdown files were also checked for trailing
  whitespace and final newlines.
- Application builds/unit suites and app/browser/device/integration/E2E checks
  are skipped for this documentation/reference task. Static screenshot inspection
  does not establish runtime visual acceptance.

## Future UI work

Use the new visual guide when implementing UI changes. Name the relevant reference
screens, reuse shared resources and record any confirmed appearance exception.
When runtime verification is needed, provide manual steps/expected results and
obtain actual observations before behavior-dependent fixes.
