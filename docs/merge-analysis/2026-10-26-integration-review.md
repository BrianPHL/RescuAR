# Dev / Athena integration review — 26 October 2026

At the original review checkpoint, source preparation was complete on `codex/integrate-athena-modules` and all finished batches were committed without a merge or final APK. The integration was subsequently merged into `migration/athena-modules-pt2` (`51ede19`). The post-review Release correction below is now applied to both checkouts.

The preserved sources are dev `004ed201b3f2e0b7922dc4cbb6c784bdddaa6ad9` and Athena `fa777da153a492268ace76162a80389e0d904534`. The final code checkpoint is `22836e154ec0ecde8b165ebc5819324f52b29d0a`; the later documentation commit contains this review and the final records. See [the progress log](2026-10-26-integration-progress.md) for the full decisions, commits and per-batch evidence, and [the baseline manifest](2026-10-26-integration-baseline.json) for source hashes and reversible Camera adaptations.

## Final module choices

| Area | Kept/adapted | Preserved or excluded |
| --- | --- | --- |
| Native/in-app splash | Athena navy AR/radar splash, banner and radar animation | Dev native bridge/library checks and dependency pins. Final startup uses the later explicitly approved restored-identity flow. |
| Sign-in/startup/registration | Secure saved sessions, one verified email/Google/OTP/address/permissions flow; Athena login/reset and two-step registration | Real Google auth; Remember Me governs restart persistence; dev password rules, optional middle name and barangays. No age restriction. Legacy archives stay aside. |
| Profile/avatar/personal/health | Athena presentation with account-scoped photos and field-only saves | Username, middle name, verified read-only email and all dev health fields; unrelated profile fields preserved. Local versus confirmed cloud photo saves distinguished. |
| Contacts/Quick Actions/system/help/policies | Athena cards/import, confirmed contact saves, shortcut sync, real app/permission details and FAQ/layouts | Import fills the form; Save confirms. Dev dashboard layout and unrelated shortcuts remain. Existing Hotlines directory and shared feature-matched policy text retained. |
| Settings/advisories | Functional optional popups, automatic advisory sound, shared vibration/voice controls and scoped map clearing | Dev feed/camera emergency warnings and manual siren remain; no general cache/account/media/message sweep. |
| Circle membership/management | Athena controls on confirmed account/server-scoped create/join/selection/roster | Labelled confirmed offline cache; member Leave separate from creator Rename/Delete; failures cannot claim cloud success. |
| Circle map | Athena main Map tab, people sheet, avatars, phone controls, inline create/join/invite and shared offline detail | General/Offline Map entry retained. Collapsible People sheet; fresh local self pin independent of uploads. Dated/unknown member readings; sharing defaults Off and uploads only with visible/qualified/confirmed context. |
| Circle chat/media/photos | Athena layout, keyed message updates/retained reading position, truthful count, stable message identity, Pending/Failed/Sent/retry and attachment preview | Account/circle cache; old unscoped files stay aside; failed drafts retained. Group photo is labelled device-local. Quick chips fill the composer for review. |
| Shared offline display | Converted 3,555-tile archive plus buildings/parks/water across General, Circle and Camera maps | Detail defaults On. General Map retains dev basic roads/points; Circle/Camera focus on members or shelters/user/active route. Routing datasets retained. Actual Mapsui local-source contract repaired; both unused conversion inputs excluded. |
| Camera 2D | Athena AR/2D/Flood tabs, real map, shared directory/search/pins, restored directory 2D entry, overview/directions, Recenter/Overview | One dev accepted destination/route/progress/hazard flow. Current validated GPS only. Pan/zoom retained after initial route framing. Existing AR/flood/recovery implementation preserved. |
| Turns/arrival/voice/haptics | Dev classifier/arrival rules; qualified cancellable speech, default Off; one short qualified near-turn pulse under the shared haptic preference | No separate route calculation, guessed current position, 15m arrival shortcut or automatic chip/send behavior. Overview destination-ahead is not confirmed arrival. |
| Dev AR/routing/report/assessment | Latest approved dev recovery, road alignment, progress/ribbon and feed/assessment changes | Required AR/native controls retained. Reports use verified account, stable ID, actual selected point and confirmed server acceptance; failed draft/media retained. |
| Admin/audit/database/flood/data | Complete Admin tracked tree exactly dev `004ed20`, including newer role/provisioning/password-setup source | Athena's conflicting Admin/audit/schema rewrite, inundation-client/data experiments and georeferenced-file deletions kept aside. No database/function/patch script executed. |
| Root Admin archive | Removed as explicitly selected | `RescuAR.Admin` application source retained; only the tracked archive reference was deleted, not remote LFS objects or history. |

## Preservation evidence

- All **861 dev paths** are accounted for: **778 unchanged**, **82 approved adaptations**, **one approved archive deletion**. No unapproved differences remain.
- Shared AR/native/routing datasets, desktop/editor/content and workflow/build sources retain dev. The shared router has only the approved provenance guard; every original regression check remains alongside the four parser fixes and new Camera/feedback entries.
- Camera's original dev body restores exactly by reversing six approved hook sites (five earlier display/feedback/lifecycle sites and the low-light Camera 2D shortcut). All 32 other pinned AR/navigation/check/history source hashes remain exact.
- The native project restores to dev after reversing only the approved splash attributes and conversion-input exclusions. Existing manifest semantics remain, with approved contacts/vibration and Android speech-engine visibility additions.
- The complete Admin subtree hash equals dev: `036fd835eb4077192b04b4d9a48d39da3ba58cf4`. Both source branches remain unchanged. No merge commits were created by this integration.

## Verification and limits

| Check | Result |
| --- | --- |
| Local auth/profile/settings/Circle/report/offline policies | 467 passed on final repair source; includes the shared directory/cache, view handoff, chat presentation and prior local policies/fixtures, including all 3,555 packaged entries. |
| Actual map tile contract | 12 passed using installed Mapsui/BruTile and the production adapter/real PNG; no network/UI. |
| Camera presentation/directions | 41 passed; GPS quality/expiry, trip ownership, geometry, viewport and shared-classifier cases. |
| Navigation feedback | 32 passed; fake speech/vibration, cancellation, deduplication, serialization and fallback. |
| Admin role/access/provisioning | 19 passed; source contracts and mocked access responses, no deployed calls. |
| Android C#/XAML | Post-review Release compilation with `DesignTimeBuild=false`, diagnostics disabled and actual XamlC passed: 0 warnings/errors. Earlier design-time checks had skipped XamlC. No APK packaging. |
| Admin bundle | Passed using existing matching dependencies without installation; large-chunk warning retained. |
| Admin lint | 0 errors, 85 retained dev warnings; no unrelated cleanup. |
| Final source/native/whitespace audit | Passed; no unapproved changes. |

The earlier 864 AR checks are historical unchanged-core evidence, not a new device run. Local checks and compilation do not establish actual GPS, rendering, lifecycle, speech, provider, database or security-policy behavior. Admin dependencies were read from the existing original-checkout installation after matching manifest/lock/config and direct versions; temporary build cache/output stayed in worktree artifacts.

Detailed offline tiles cover **part of Marikina at zoom 13–18**, not the full city/world. The status checks the map center's actual tile. Buildings/parks/water and shelter-directory entries are static context, not current safety, access or availability evidence.

Android speech visibility follows [Microsoft's MAUI platform setup](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/text-to-speech?view=net-maui-9.0); actual engine support, voice quality and cancellation latency remain device checks. Newer dev Admin provisioning also expects its corresponding deployed database/function setup; that deployment was not inspected or changed.

## Post-review Release correction

XC0002 exposed three nonexistent sub-tab handler references. The fix binds AR/2D/Flood to the existing dev mode handlers, without adding duplicate switching logic. Commits: `c628e01` on the integration branch and `f2589f4` on the built branch.

Earlier compile-only C#/XAML claims were too broad: Android's Compile target enables design-time mode by default and skips XamlC. Production verification explicitly set `DesignTimeBuild=false`, `RescuArDiagnosticBuild=false` and `MauiXamlCValidateOnly=false`; the log confirms actual XAML compilation. Release passed with **0 warnings/errors**, without creating an APK. Actual device behavior remains unverified.

## Map regression correction

The map review found a real rendering defect inherited from Athena: the packaged source implemented only `ITileSource` although Mapsui 5.1 requires `ILocalTileSource` to fetch local bytes. The supplied archive was present and valid inside the user's earlier APK. The adapter is corrected, the app-specific online tile identifier restored, and the actual Mapsui fetch conversion now tested. The reported 403 still needs device/network confirmation.

The user also approved removing raw routing-graph overlays from Circle/Camera, restoring a fresh local self pin independent of sharing, and a map-first/collapsible People presentation. General Map and dev routing/AR remain intact. See [findings, commits and device checks](2026-10-26-map-restoration.md). No APK was created for these fixes.

## Before a later merge/release

APK packaging remains deferred. Earlier APK artifacts are earlier checkpoints and **do not represent this final branch**. Use a later requested build for these checks and report actual outcomes before behavior-dependent fixes:

1. Restart, Remember Me, sign-out/account switch, verified sign-in and field-only profile/contact/photo saves. Expect correct identity routing and no previous account's active data.
2. Circle create/join/leave/delete, missing/old telemetry, sharing Off/On, offline state and chat/media retry. Expect confirmed or clearly pending results, truthful readings and no silent cross-account/circle reuse.
3. General/Circle/Camera maps, detail Off/On, coverage boundaries and Clear Map Cache. Expect focused Circle/Camera markers, retained General Map roads/points and active routes, explicit missing coverage and untouched private/routing data.
4. Start/change/stop routes, pan/zoom, AR/2D/Flood, drift/poor GPS, hazards and background/resume. Expect one accepted dev route, guarded progress and existing arrival confirmation. Check physical road alignment separately with the dev field protocol.
5. Voice default/persistence/mute, uncertain guidance, trip changes, app stop and qualified turn pulses. Expect cancellation/fallback without overlapping speakers or false arrival claims; verify actual provider/device behavior.
6. With controlled Admin accounts, verify deployed permissions, first-login password setup, provisioning, audit/report moderation, SMS and flood/data workflows. No live operation is implied by the local tests.

No branch merge, push, deployment, database operation, running-app/browser/device test or final-branch APK is included in this preparation. Those remain separate user-controlled actions.

## 27 October preservation audit

The source-only recheck found remaining losses: Athena's directory-cache repository was left as dev's three static centers; its two directory-to-2D actions were removed and View Map opens Circles; marker invalidation does not track directory updates; chat rebuilds unchanged history every five seconds; and its verified numeric member count was removed. They were unfixed at that analysis checkpoint and are now addressed by the approved 28 October repairs. The earlier tile-interface defect existed in Athena too, so its origin is corrected above. See [pinned evidence, intentional differences and recommended repair order](2026-10-27-athena-regression-audit.md).

## Completed 28 October repairs

The shared directory/empty-result/cache path, selected-center 2D entry, directory-driven markers, stable chat rows/count and inline Circle controls are committed in both checkouts. Final verification: **467 combined local checks**, **41 Camera checks**, actual Release XamlC **0 warnings/errors**, and static ARCore compatibility checks. All 31 changed source/test paths match; core/native/assets/Admin remain preserved. No APK or device/backend run. See [commit table, precise limits and manual cases](2026-10-28-athena-regression-repairs.md).
