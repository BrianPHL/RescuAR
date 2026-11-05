# UI appearance reference

The user-supplied `Group 1.png` is the default appearance reference for new UI,
additions and redesigns in both the Android app and Admin dashboard. It defines
the visual language; it does not prescribe structure or layout.

![RescuAR mobile and Admin appearance reference](reference/rescuar-ui-reference.png)

## Reference provenance

| Item | Value |
| --- | --- |
| Original attachment | `Group 1.png` |
| Repository copy | [rescuar-ui-reference.png](reference/rescuar-ui-reference.png) |
| Dimensions | 32768 × 10503 pixels |
| File size | 28,144,383 bytes |
| SHA-256 | `2d1fd06c83be0942384f9c17df79da0dd7f2533d7d4a438eb7c942ad9444df7c` |
| Added | 2026-11-03 |

The repository copy preserves the original bytes. Open it at full resolution and
inspect/crop the relevant screens; the whole-board thumbnail cannot establish
small text, spacing or control details. Temporary inspection crops belong under
ignored `artifacts/`; label any retained derivative and preserve the original.
The black space between screens is the board background, not an app dark theme.

Screenshot copy, sample data, alert wording, status values, navigation labels and
pictured behavior are examples, not instructions or verified feature contracts.
Use current requirements and owning source for behavior. Never add fake telemetry,
automatic evacuation, delivery or backend claims merely because a mockup shows them.

## Appearance versus structure

| Match the reference by default | Developer decides |
| --- | --- |
| RescuAR logo/brand treatment and teal accents | Page/screen composition and information grouping |
| Light neutral surfaces, dark text, muted supporting text | Grids, panels, component hierarchy and responsive breakpoints |
| Clear sans-serif typography and visible heading hierarchy | Navigation arrangement and task flow |
| Restrained borders, rounding, shadows and control styling | Where controls, forms, tables, cards and maps are placed |
| Coherent outline icons, button states and selected treatments | Framework/component implementation |
| Red danger/emergency, green success and distinct caution treatments | New states/features required by the task |

The design can use a different layout and still conform. Do not copy fixed
positions, screen dimensions, sidebar arrangements, mobile tab counts or form
sequences as mandatory structure. Accessibility, readable content, localization
and different device sizes can require spacing/layout changes without a visual
style departure.

## Visual language to carry forward

- Use the RescuAR brand/logo assets with their proportions preserved. Use teal
  for primary brand actions, links and selection as shown in the reference.
- Use white/off-white and light-gray surfaces, dark readable headings/body text,
  muted secondary text and restrained dividers. Keep elevation/rounding subtle.
- Use a clear sans-serif hierarchy with consistent weights and button/input/icon
  treatment. Match the relevant platform screens; do not introduce a second
  typeface or decorative style for a single new module.
- Carry semantic status styling across banners, dialogs, badges and controls.
  Emergency red and success green are state colors, not general brand substitutes.
  Pair color with text/icon meaning; preserve usable contrast and control states.
- For camera/map UI, use the reference's legible light/translucent guidance
  surfaces and cyan/teal route emphasis as visual cues. Placement, suppression,
  trust state and arrival behavior follow navigation/AR policies, not the image.
- Preserve the same visual family across loading, empty, error, disabled,
  selected and success states. Avoid inventing an unrelated theme for new features.

These are appearance observations, not a pixel-perfect specification. Do not
invent exact font names, color hex values or spacing measurements from small
thumbnails. The reference does not establish a dark-theme palette; derive any
required dark styling from existing semantic resources and confirm material
appearance departures as described below.

## Implementation entry points

| Surface | Existing resources to inspect/reuse |
| --- | --- |
| MAUI semantic/brand colors | [RescuARColors.xaml](../../RescuAR.MAUI/Resources/Styles/RescuARColors.xaml) |
| MAUI shared controls/theme bindings | [Styles.xaml](../../RescuAR.MAUI/Resources/Styles/Styles.xaml), [App.xaml](../../RescuAR.MAUI/App.xaml) and nearby page styles |
| Admin shared tokens/control styling | [index.css](../../RescuAR.Admin/src/index.css) |
| Admin responsive behavior | [responsive.css](../../RescuAR.Admin/src/responsive.css) |
| Admin logo treatment | [BrandLogo.css](../../RescuAR.Admin/src/components/BrandLogo.css) and existing brand component |

Inspect existing screen-specific resources as well. Shared files currently contain
some differing/default treatments, so their presence is not proof of conformity.
For example, MAUI has named RescuAR teal resources alongside template colors, and
Admin's current brand token differs from the mobile teal. Reconcile a touched
component with the screenshot reference and reusable semantic styles; do not
propagate a known mismatch just because an old screen has it. Update the shared
resource when the requested scope calls for it, rather than add isolated overrides.
A documentation/reference update alone does not authorize a global restyle.

## When a developer requests a different look

1. Follow the requested feature and layout direction. Identify whether the proposal
   changes appearance materially: palette/brand, typography, surfaces/elevation,
   control/icon style or semantic state treatments. Structural changes alone are
   not a reason to ask for approval.
2. Prepare a concrete proposal, reusable preview or change description showing the
   requested appearance and how it differs from the reference. Do not require a
   running-app interaction to prepare the proposal.
3. Ask one focused confirmation: **Proceed with the proposed appearance, or keep
   the screenshot's visual style with the proposed layout?** Identify the relevant
   reference screens and the material differences so the choice is reviewable.
4. Continue work that does not depend on this choice; hold the disputed appearance
   edits until the developer confirms. Apply the confirmed choice for that scope.
   An explicit confirmation already given in the task satisfies this step; do not
   ask repeatedly or expand a one-screen exception into a project-wide redesign.
5. Record the decision in the task's change record. If the confirmed change becomes
   a new baseline, update this guide and add the approved reference with provenance;
   preserve the earlier reference as historical evidence.

If the proposal is agent-initiated, use the same confirmation rule. This is an
explicit project preference requested by the user, not a generic approval gate.
No confirmation is needed for routine additions consistent with the reference.

## Review and evidence

For a UI task, note which mobile/Admin screens informed the change, inspect the
relevant source styles, and compare the proposed appearance with those screens.
Check the relevant visual states and responsive/text-size constraints within the
requested scope. Record comparison limitations instead of claiming visual parity.

Reference inspection and source review do not authorize manually operating the
app/browser, automation of interactions or integration/E2E tests. Follow
[AGENTS.md](../../AGENTS.md). When runtime appearance needs confirmation, provide
manual steps and expected results and obtain the tester's actual observations.
