# Agent portability and documentation organization

Date: 2026-11-03 (Asia/Manila)
Starting source: `dev` at `b3090cc`

## Changes and purpose

- Kept `AGENTS.md` as the shared repository policy and added its startup/context
  route and static documentation-check commands. Added thin Gemini/Claude imports
  and a Copilot pointer; Antigravity and Cursor use native `AGENTS.md` discovery.
- Added [agent compatibility guidance](../contributing/ai-agents.md), based on
  current official vendor documentation, including import differences, host limits
  and optional manual discovery checks. No agent-specific global settings/hooks,
  symlinks or copied policy sets were introduced.
- Reworked [the documentation index](../README.md) into minimal task routes and
  role/authority guidance. Added a [generated catalog](../catalog.md) of every
  Markdown file and a [status register](../status.md) for known source/evidence gaps.
- Added a [research landing page](../../RescuAR.Admin/prep_api/validation/README.md)
  and [merge-history index](../merge-analysis/README.md). Marked 31 root/merge
  reports explicitly historical while preserving their original content and paths.
- Extended the documentation workflow with portable authoring conventions,
  evidence-role classification, adapter maintenance and catalog verification.
  Replaced machine-specific working-directory examples in current routing/peer
  procedures with repository-relative setup.
- Added `build/Verify-Documentation.ps1`: uses Git and PowerShell 7 to generate/check
  catalog freshness, local inline links and shared agent-entry imports. It performs
  no application, network, service, deployment or AI-client execution.
- Linked the new material from the project/index/change entry points and recorded
  the verified `b3090cc` result in the prior agent-guidance record.

## Scope and evidence

The screenshot appearance/layout/confirmation rules are preserved. Historical
paths remain stable because CI, source checks and captured merge baselines refer
to them. Research/legacy evidence is discoverable without becoming current policy.
Known application build, database, PREP and device-acceptance gaps remain open.

Official sources and the format-review date are in the compatibility guide.
Compatible file formats are distinct from actual loading/compliance in a user's
installed agent version or organization configuration.

## Verification

- Source inspection reconfirmed committed sidebar conflict markers, missing audit
  foundation 001 and the absent PREP rules route; no application build was rerun.
- Static documentation checker: 88 Markdown files and 344 local inline links,
  no missing targets; catalog current; Gemini/Claude imports and Copilot pointer valid.
- The first scan treated a literal import-syntax example as a link. One fix made
  the scanner mask fenced/inline code while retaining line offsets; the rerun passed.
- `git diff --check` and new-document/script whitespace checks: passed.
- The preserved UI reference SHA-256 remains
  `2d1fd06c83be0942384f9c17df79da0dd7f2533d7d4a438eb7c942ad9444df7c`.
- Application builds/unit suites, browser/app interaction, integration/E2E,
  physical-device checks and external AI-agent sessions are skipped for this
  documentation/tooling change. No new dependencies are installed.

## Optional manual client check

In the desired agent's next repository session, ask which instruction files were
actually loaded and request a summary of the documentation, UI-reference,
verification and commit rules. Expect the shared root policy and applicable entry
file; report the actual result before treating client discovery as verified.
