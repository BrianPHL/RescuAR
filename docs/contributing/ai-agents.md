# AI agent compatibility

Repository instruction formats checked against official documentation on
2026-11-03. These files provide compatible entry points; no Gemini, Antigravity,
Claude, Cursor or Copilot session was launched to certify runtime discovery.
Product versions, trust settings and personal/organization rules can affect loading.

## One shared policy

[AGENTS.md](../../AGENTS.md) owns repository rules. Agent-specific files only load
or point to it. The [task index](../README.md) selects current guides, the
[catalog](../catalog.md) locates the complete corpus, and
[status](../status.md) distinguishes source gaps from verified results.

No tool-specific SDK, connector, model choice, global setting or symlink is
required to read the documentation. Preserve each host's instruction hierarchy
and permissions. A repository file cannot override them or authorize actions
outside the user's request.

## Entry points

| Agent/host | Repository entry | Loading behavior and source |
| --- | --- | --- |
| Codex | [AGENTS.md](../../AGENTS.md) | Native repository instructions; applicable parent/override rules and size limits still apply. [Official guide](https://learn.chatgpt.com/docs/agent-configuration/agents-md). |
| Google Antigravity | [AGENTS.md](../../AGENTS.md) | Current App/CLI/IDE discovery supports root/ancestor `AGENTS.md` or `GEMINI.md`; no duplicate `.agents/rules` file is needed. [Official rules](https://antigravity.google/docs/rules/). |
| Gemini CLI | [GEMINI.md](../../GEMINI.md) | Uses its documented `@./AGENTS.md` import. Custom context filenames/trust settings can alter discovery. [Official context guide](https://geminicli.com/docs/cli/gemini-md/). |
| Claude Code | [CLAUDE.md](../../CLAUDE.md) | Imports the shared policy with `@AGENTS.md`, including versions/sessions that do not load `AGENTS.md` natively. [Official memory guide](https://code.claude.com/docs/en/memory). |
| Cursor Agent | [AGENTS.md](../../AGENTS.md) | Current Agent supports root `AGENTS.md`; no second `.cursor/rules` policy is needed. This does not establish Tab behavior. [Official rules](https://cursor.com/docs/rules). |
| GitHub Copilot | [.github/copilot-instructions.md](../../.github/copilot-instructions.md) | Repository-wide Markdown entry explicitly directs the agent to the shared policy. A Markdown link is a read instruction, not a guaranteed text import. Host/feature support varies. [Official instructions](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions/add-repository-instructions). |
| Other agents/editors | Explicitly read [AGENTS.md](../../AGENTS.md) | Use the same plain Markdown rules and task index. Do not invent an automatic-discovery filename for an unverified host. |

Open/start the agent at the Git repository root when possible. For a subfolder
workspace, check which ancestor instruction files that host actually loads.
In particular, Antigravity does not discover arbitrary child rules merely because
it later edits files there; the root policy keeps the shared constraints visible.

## Import differences

Gemini and Claude expand their documented plain `@path` imports. Antigravity's
plain `@filename` is a resolved file reference, while its `@[label](path)` form
inlines content. This repository relies on Antigravity's native root `AGENTS.md`
support rather than treating Gemini's import syntax as a universal preprocessor.
See the vendor guides above for those distinct behaviors.

Avoid reciprocal imports, full-corpus imports and symlink aliases. A client that
loads both an entry file and `AGENTS.md` must apply one policy, not interpret
repeated content as additional authorization. Do not copy policy into personal
memory or generate a competing instruction file with an onboarding command.

## Starting a task on any host

1. Read root `AGENTS.md`, inspect Git status, and identify the user's requested
   outcome and existing work. Verify instruction discovery rather than assume it.
2. Pick the shortest relevant route in `docs/README.md`. Load source and detailed
   guides only for the affected area; do not preload dated field/merge reports.
3. Use current source/configuration for consequential claims. For UI work, inspect
   the relevant [appearance reference](../design/README.md). Layout remains free;
   material appearance departures follow the existing confirmation rule.
4. Follow the [documentation workflow](documentation-workflow.md), perform the
   smallest allowed verification, and maintain a task change record.
5. Report actual files changed, checks/results, limitations and a proposed commit.
   Identify unavailable tools/permissions plainly; do not fabricate their outputs.

If the host cannot read repository files, provide or attach `AGENTS.md` and only
the relevant task guide through that host's supported context mechanism. A prompt
to read files is not proof they were loaded. Do not upload private source/captures
to an unrelated service merely to work around a missing integration.

If image inspection is unavailable, read the visual guide's text and existing
semantic styles, report that limitation, and do not claim a screenshot comparison.
The absent image tool does not authorize an unrelated appearance or fabricated
visual acceptance.

## Verify discovery without changing the application

In the chosen agent's next session, ask it to identify the instruction files it
actually loaded and summarize the documentation, UI-reference, verification and
commit rules. It should name `AGENTS.md` and the applicable adapter, and preserve
the screenshot-appearance/layout distinction. This read-only prompt is optional
manual client validation; it was not run during the repository update.

Gemini's `/memory show` and `/memory reload` can inspect/refresh its context;
Claude's `/context` can show memory files. Antigravity's Rules view and the other
hosts' available instruction diagnostics can help. These are manual suggestions,
not commands to start an app, run tests, or alter global settings automatically.

## Maintenance

Change shared policy in `AGENTS.md` and detailed behavior in its owning guide.
Keep adapters small, relative and free of machine-specific paths. Recheck vendor
documentation when changing an entry format; record format/source verification
separately from actual client testing. Run the repository documentation checker
after changes. Known product limitations do not justify silently relaxing user rules.
