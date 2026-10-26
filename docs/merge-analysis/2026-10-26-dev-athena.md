# Dev and Athena integration analysis — 26 October 2026

## Recommendation and scope

Start the eventual integration from `dev`. Preserve Athena's splash, authentication/profile screens, Safety Circle map/management/chat, and Camera 2D map experience. Adapt their shared services and state handling to preserve dev's routing, AR recovery, data, dashboard, reports, preparation modules, admin permissions, tooling, and validation evidence.

No merge, cherry-pick, application edit, commit, database change, build, or application interaction was performed during this analysis. The attached worktree remains detached at the inspected dev commit; these analysis documents are its only intended changes. The original checkout was left untouched.

| Reference | Inspected commit |
| --- | --- |
| `dev` and stored `origin/dev` | `982146b600d3bf19006e5d4044bcbe8cf383de8c` |
| `migration/athena-modules` and stored remote counterpart | `fa777da153a492268ace76162a80389e0d904534` |
| Common ancestor | `92c9db88373ae5651266e29bc3d4131d6d44b2aa` |
| Analysis worktree | `C:\Users\brian\.codex\worktrees\dev-athena-merge-analysis\RescuAR` |

Remote refresh could not connect to GitHub. These conclusions apply to the exact local commits above. Refresh and compare again before implementation if either branch advances.

### Uncommitted work in the original checkout

Additional local AR/navigation work appeared in `D:\Projects\RescuAR` during final inspection. It is outside the pinned-commit comparison and was not copied into this worktree:

- Modified: `RescuAR.MAUI/Views/Camera/CameraPage.Positioning.cs`, `CameraPage.RoadApproach.cs`, and `CameraPage.xaml.cs`.
- Modified: `RescuAR/AR/ARCameraSpatialController.cs`, `ARRoadApproachBridge.cs`, and the regression runner's `Program.cs` and `.csproj`.
- Untracked: `RescuAR.MAUI/Views/Camera/CameraPage.MldGuidance.cs`, `RescuAR/AR/MldGuidanceBridge.cs`, `tests/RescuAR.RegressionChecks/Fixtures/MldCBenitezResponse.json`, and `tests/RescuAR.RegressionChecks/MldGuidanceOwnershipChecks.cs`.

Preserve these edits and reconcile them in batch 0 before any integration. They overlap protected navigation areas and may change the eventual baseline. Their completeness and behavior were not assessed here. Normal status also encountered a Git LFS access error on `RescuAR.Admin.zip`; a filter-bypassed status reports that archive as modified, which is not sufficient to establish a real archive-content change. The archive was left untouched.

There are **275 final-tree differences**: 158 files present only on Athena, 25 present only on dev, and 92 modified files. Of the paths changed on both sides since the common ancestor, **30 remain different**. Those are review hotspots, not a count of Git merge conflicts. No trial merge was run. A file absent from Athena's snapshot is not necessarily a deletion that a three-way merge would apply.

The [file decision inventory](2026-10-26-file-decisions.csv) covers all 275 differences. The [baseline manifest](2026-10-26-baseline.json) records the pinned trees and every tracked dev file's blob ID for later preservation checks. Recommendations identify integration scope; they do not claim every asset or every changed line has been behaviorally validated.

## Which modules to keep

| Module or surface | Recommended owner | Integration decision |
| --- | --- | --- |
| Native splash, logo, animated radar splash | Athena presentation | Keep visuals and assets. Separate startup/session decisions from the animation. Preserve native AR build requirements when editing project settings. |
| Login, registration, password visibility/reset, Google sign-in | Athena presentation; reconciled shared authentication | Keep forms. Route all successful sign-in, registration, OTP, and session restoration through one authenticated identity/address/permissions flow. |
| Profile overview, personal and health information, avatar | Athena presentation; one account-scoped profile service | Keep screens and fields. Use field-specific saves, one authenticated user ID, and truthful local/cloud save status. |
| Emergency contacts and contact Quick Actions | Athena additions on dev dashboard | Keep import/edit/phone actions. Synchronize one account-specific contact/shortcut representation without replacing unrelated dashboard behavior or styling. |
| App settings, Help, System Information, legal screens | Athena presentation | Keep useful screens. Reconcile settings keys and consumers; verify displayed support/legal content separately. |
| Safety Circle creation, joining, selection, rename, leave/delete | Athena presentation; adapted sync service | Keep controls. Replace local-success fallbacks with confirmed operations, explicit pending/failure state, and account/role checks. |
| Circle map, member pins/avatars, people sheet, battery and phone actions | Athena presentation | Keep. Show unknown/stale locations honestly and add shared offline display layers. Preserve access to dev's general map. |
| Circle chat, quick messages, media and group photo UI | Athena presentation; adapted delivery service | Keep. Connect delivery state and retry/deduplication to the actual screen; isolate account and circle caches. |
| Camera 2D map, shelter search, route summary/directions, mode switch, voice button | Athena presentation; dev navigation authority | Keep visible features. Render dev's accepted route, accepted location/progress, hazard/reroute decisions, and arrival outcome. |
| General map and embedded road/point layers | Dev, with useful Athena offline extensions | Keep reachable. Athena changes the main Map tab to Circles; that must not silently remove access to the existing general map. |
| Evacuation-center catalog and Prepare entry points | Dev data/behavior with Athena 2D entry | Keep current data flow and AR entry. Add 2D handoff to the same selected destination. Label fallback catalog information as static/unverified. |
| AR runtime, tracked-frame recovery, ground/heading/anchor logic | Dev | Preserve current implementation and dependency pins. Port only the UI and destination/view hooks required by Athena. |
| Reports, underlying advisories, preparation/PASS/checklists, telemetry and prediction | Dev | Preserve. Add only the small consumer changes required for contacts, settings, and map navigation. |
| Admin access control, login-session handling and permission-aware navigation | Dev | Preserve the newer role/permission foundation. |
| Athena live Audit Logs viewer | Optional separate admin batch | Adapt to dev permissions and compatible audit RPCs. Keep mobile integration independent. |
| Legacy `RescuAR.App/` copy, generated R state, unrelated repository changes | Current dev defaults | Exclude from the mobile port. The active MAUI solution references `RescuAR.MAUI/RescuAR.MAUI.csproj`. |

The three principal map surfaces need distinct roles: the Circle map shows people; the Camera 2D map guides a journey; the existing general map retains offline road/point exploration. Shelter preview maps are another existing consumer. Share display layers and accepted journey state where appropriate while retaining each purpose. Recommended navigation: keep Athena's Circles on the Map tab and provide an explicit general/offline map entry within that area; Camera retains its 2D/AR/Flood choices.

## Findings that determine the merge boundaries

Source references below use **A** for the pinned Athena tree and **D** for the pinned dev tree. Line numbers refer to those commits, not a future combined file.

### 1. Preserve the latest dev recovery batch

Dev commit `d08e3c2` adds tracked-frame requalification, mapped-arrow world anchoring, diagnostics, and multi-device recovery checks. Athena has earlier versions of these paths:

- `RescuAR/Navigation/Progress/RouteDisplayProgressPolicy.cs`: dev uses the configured recovery lifetime; Athena resets requalification evidence after the separate eight-second PDR display budget. At a five-second fix cadence this can repeatedly discard evidence before three fixes accumulate.
- `RescuAR/AR/ARRoadApproachBridge.cs` and `ARRoadApproachRenderer.cs`: dev retains/recomputes a mapped world target and records cue visibility reasons; Athena lacks these additions.
- `RescuAR.MAUI/Views/Camera/CameraPage.RoadApproach.cs`: keep dev's qualified mapped-target and publish-result handling, along with `CameraPage.Positioning.cs` and `CameraPage.FieldDiagnostics.cs` diagnostics.
- `tests/RescuAR.RegressionChecks/ARMultiDeviceRecoveryChecks.cs` and runner entries, plus `AR_FIELD_VALIDATION_20261026.md`: retain dev.

These are protected implementations. Replacing the camera files as a block would lose current dev work even though Athena already contains many older dev navigation updates.

### 2. Shared 2D/AR geometry has improved, but acceptance is still split

Athena commit `94b605f` retains a computed route through the destination-change event and refreshes the 2D map after accepted routing/rerouting. Preserve the user-facing intent of that fix.

However, A `Views/Camera/CameraPage.xaml.cs:3128–3197` directly obtains an origin, requests a hazard-avoiding route with `Array.Empty<RouteHazard>()`, falls back offline, and assigns `activeRoute` before publishing the destination. This path lacks the normal request's destination-version/cancellation ownership. A late shelter request can therefore compete with a newer selection. Reuse of pending geometry alone does not establish equivalent location/hazard/startup admission.

A `CameraPage.xaml.cs:5400–5449` updates the 2D marker/guidance before `positionObservationPolicy` and navigation stability accept the reading. A `:12388–12475` independently finds the nearest route vertex, generates turns, and announces arrival within 15 metres of the final route point. Dev's `SafeZoneConfirmationService` and explicit user-arrival confirmation have additional conditions. This can produce contradictory instructions or a premature “safe zone” message even while the underlying shared confirmation remains unconfirmed.

Integrate shelter selection through dev's request/acceptance lifecycle. Both displays must consume the accepted trip, route revision, position/progress, turn instruction and arrival state. Keep geographical navigation available while AR heading/tracking recovers; AR rendering has its own qualification requirements. Do not require a working AR session merely to display an accepted 2D route.

### 3. A default map viewport must not become a user location

A `CameraPage.xaml.cs:2894–2964` returns cached readings without consistently checking freshness/accuracy and eventually returns `(14.6507, 121.1029)` when location is unavailable. The returned coordinate can be used as a routing origin. A `ViewModels/Map/SafetyCircleViewModel.cs:403–405` similarly creates the current-user pin at default coordinates when a location is unavailable.

Keep a default center for browsing, but represent an unknown user location explicitly. Use dev's location admission policies for route startup and accepted progress. Circle locations also need validity, age, sharing-permission and stale-state treatment; battery freshness must not imply location freshness.

### 4. Safer circle helpers exist but the active screens bypass them

A `Services/SafetyCircle/SafetyCircleSyncService.cs` provides per-account storage, account-change checks, cached-state indication and pending/delivery state. `Services/Cloud/SupabaseSafetyCircleRemote.cs` checks the authenticated session and confirms several membership/management operations. These are useful adaptation candidates.

The current Map, Chat and Circle Settings view models use `Services/Cloud/SafetyCircleService.cs` instead. Source references show no active MAUI consumer of `SafetyCircleSyncService` or `SupabaseSafetyCircleRemote` outside their definitions; helper tests exercise the former separately.

The active service still:

- Derives a user ID from cached email or generates a device ID if authenticated identity is missing (`:25–59`).
- Caches a created circle and returns it after cloud failure (`:73–111`); joining also swallows membership errors (`:156–217`).
- Reads unscoped `LocalSafetyCircles`, selected/joined/deleted IDs, and `chat_cache_{circleId}.json` files; chat persists across sign-outs (`:674–676`, `:752–793`).
- Returns a local message after failed cloud delivery, while the screen has already added a separate optimistic message ID (`ViewModels/Map/CircleChatViewModel.cs:289–323`).

A `ViewModels/Profile/SafetyCircleSettingsViewModel.cs:132–156` reports a rename after a local cache write. Its Leave action (`:295–300`) calls the delete-confirmation flow. Reconcile member leave versus creator deletion explicitly. Live backend permissions and table relationships remain unverified; client checks alone do not establish authorization.

### 5. Authentication/profile work has competing paths

A `Services/Profile/UserProfileService.cs:105–134` has useful field-specific writes and account-change checks; `PersonalInfoViewModel.cs:48–74` uses them. The actual `Views/Profile/PersonalInformationPage.xaml.cs:8–17` binds to the separate `PersonalInformationViewModel`, which saves an incomplete `User` object with `Upsert`, swallows cloud failure, and reports success (`:123–174`). Emergency Contacts also patches fields and then performs another incomplete-object upsert (`EmergencyContactsViewModel.cs:184–246`). Such writes can overwrite fields managed by another screen; the deployed schema/default behavior was not checked.

Login (`ViewModels/Authentication/LoginViewModel.cs:153–236`) writes both user-ID preference variants, performs an unawaited partial user upsert, and goes directly to Shell/Permissions. Registration (`RegistrationViewModel.cs:270–325`) also has a direct navigation/background-write path. OTP/Google/address flows can use `AuthenticationNavigation.CompleteSignInAsync`, so the account-activation contract is inconsistent.

`SplashViewModel.cs:54–82` still routes using preference flags after a best-effort session restore. `UserProfileService.ClearIdentity` clears `CurrentUserId` but leaves several keys consumed by the active circle/profile code, including `current_user_id`, `UserPhone`, relationship keys ending in `Rel`, circle selection/cache and chat files. Consolidate identity and migration rules before enabling all screens together. Preserve unowned legacy cache data for review; do not attach it to a newly signed-in account automatically.

### 6. Settings screens and behavior use different stores

A `ViewModels/Profile/AppSettingsViewModel.cs:49–57,91–126` writes `PushNotificationsEnabled` and `OfflineMapCachingEnabled`. `Services/Settings/AppSettingsStore.cs:25–31` reads `ForegroundAdvisoriesEnabled` and `DetailedOfflineMapEnabled` instead. Direct preference writes also bypass the store's change event. Siren Quick Actions are edited in `CustomQuickActionsList_v18`, while the dashboard reads account-scoped `UserProfileService.GetLocal("QuickActions")`.

The active camera voice toggle is an in-memory `_isVoiceMuted` flag (`CameraPage.xaml.cs:12493–12511`). The standalone voice/haptic services and NavigationVoice preference are not the active camera consumer. Connect the UI to one shared store and define migration/defaults once. Keep the underlying advisory event available even when an optional foreground popup is disabled.

Settings' Clear Cache deletes `FileSystem.CacheDirectory`; the detailed offline-map copy is under AppData (`DetailedOfflineMapService.cs:20`). Adapt the scoped `RemoveLocalCopyAsync` operation and report what was actually removed, while preserving private account data, profile media and unsent messages.

### 7. Offline helpers are not equivalent to offline map coverage

A `AppShell.xaml:32` opens `SafetyCirclePage` for Map. The useful `MapViewModel` still contains dev's embedded road/point display and an offline layer controller, but the replacement shell entry no longer selects it. A `SafetyCircleViewModel.cs:645–667` and `CameraPage.xaml.cs:12099–12104,12296–12305` construct online OpenStreetMap layers; the camera removes its old roads layer. `CameraOfflineMapDataService` and the standalone offline renderer helpers are not wired into this camera path.

Reuse the packaged offline display pipeline across the maps, retain the embedded routing datasets, and keep the general map reachable. A cold offline start must work within actual packaged coverage; a warmed network tile cache is insufficient evidence.

The added `MAP_V2.mbtiles` is 94,307,328 bytes and the converted `offline-map-tiles.zip` is 93,661,024 bytes. There is no active runtime reader for `MAP_V2.mbtiles`. The project's raw-asset glob excludes `2D_MAP.mbtiles` but does not exclude `MAP_V2.mbtiles`, whereas `build/Verify-Phase4Package.py:61` expects both MBTiles files excluded. Importing the current assets/project configuration would include approximately 179 MiB of those two raw inputs before packaging effects. Keep conversion inputs outside the app asset selection and verify the resulting package once the offline-map batch is implemented.

### 8. Shelter discovery needs one catalog contract

A `Data/EvacuationCenterRepository.cs` prefers cached centers and otherwise supplies a static Marikina list with capacity/status values. Keep useful fallback discovery and search presentation, but distinguish fallback metadata from current operating status. Preserve dev's shelter data/cache flow and selected-center AR navigation. Validate complete latitude/longitude values; the current cached-center test uses `latitude != 0 || longitude != 0`, which also accepts a missing coordinate component. Both AR and 2D buttons must target the selected valid center, rather than silently choosing the first available center when selection is missing.

### 9. Preserve dev native packaging and Admin authorization

A `RescuAR.MAUI.csproj:149,225` makes the native bridge item conditional and changes the missing-bridge error into a warning. Preserve dev's required ARM64 native library and validation error. Import only splash/approved asset changes into this project file; preserve dependency pins and the existing AR release checks.

Dev commits `443f531`, `28225ba`, and `05a70e4` introduce/fix role-based permissions, allowed views/sidebar, session handling, administrator management and checks. Athena's Admin entry files use an older membership-style gate and omit those new files in its tree. Keep dev's `App.jsx`, `AuthWrapper.jsx`, `Auth.jsx`, `Header.jsx`, `Sidebar.jsx`, `src/rbac/access.js`, RBAC migration and tests as the foundation. Adapt the optional Audit Logs viewer with a suitable permission mapping and compatible server enforcement.

Athena's audit foundation SQL is historical, guarded deployment material, not a migration to replay automatically against today's schema. The actual deployed schema was not inspected. Keep dev's SMS gateway workflow, routing-bundle exporter, evidence documents, research data, Git LFS configuration and other unrelated assets.

### 10. Preserve useful checks without assuming they test active screens

The three Athena helper projects test Circle sync, directions/speech and settings/offline-package utilities. Retain useful assertions while wiring the actual screens to the corresponding services. Their presence does not verify the current screen implementation.

Static inspection also found a dev baseline test issue: `MLDRoutingService.ParseRoutes` has three parameters (`:701–704`), while four reflective calls in `tests/RescuAR.RegressionChecks/Program.cs` supply two. Athena corrects those call sites and adds a null-provenance guard. Evaluate those small changes separately, keeping all dev recovery tests. No failure was reproduced because no tests were run during analysis.

## Integration sequence

Each numbered batch should produce a small, reviewable patch. Create an integration branch from the pinned dev baseline only when implementation is authorized. Do not copy whole overlapping files, apply broad historical commits, or use an all-ours/all-theirs conflict strategy. The file inventory is a starting scope list; narrow it to specific hunks before each batch.

| Batch | Work and allowed boundary | Gate before dependent work |
| --- | --- | --- |
| 0. Baseline | Preserve/reconcile the original checkout's uncommitted navigation work, then refresh branch IDs; preserve the 30 overlap hotspots and protected dev paths. Establish dev checks. If needed, separately adapt the four reflective parser calls and assess the isolated null guard. | Record existing failures separately. Current dev recovery checks remain present; baseline build/check outcome recorded before attributing failures to integration. |
| 1. Splash only | Logo assets, native splash color/size, splash XAML/radar animation. Keep authentication/navigation logic separate. | Resource and lifecycle review; user confirms cold-start appearance and transition. Native-bridge error stays intact. |
| 2. Identity and profile foundation | Secure session persistence, one authenticated user identity, account activation/cleanup, one sign-in/OTP/Google/address/permissions path, partial-field profile writes. Scope DI/callback additions to this need. | Focused state/persistence checks plus Android build. User checks restart, expired session, sign-out and account switch; one account's data cannot populate another's screen. |
| 3. Profile and contacts | Athena Profile/Personal/Health/Avatar/support screens; connect one profile service. Import contacts and synchronize Quick Actions in dev's dashboard. Preserve unrelated dashboard layout/behavior. | Focused save/contact tests where useful; user saves each screen and reopens it, changes accounts, and checks phone actions. No save overwrites unrelated profile fields. |
| 4. Settings | Athena settings UI plus one shared settings store, migrated keys, actual advisory/siren/haptic/voice consumers and narrowly scoped map cleanup. | Adapt SettingsChecks. User verifies toggles after restart and scoped cleanup. Underlying safety/advisory data flow remains active. |
| 5. Circle membership and management | Adapt sync/remote services first; then connect create/join/select/rename/leave/delete screens. Use verified account identity, account-scoped storage and truthful failures. | Adapt SafetyCircleChecks. User checks two accounts, membership revocation and failed writes; no false successful join/create or cross-account cache display. |
| 6. Circle map | Athena people sheet/pins/avatars, battery and calling UI. Handle absent/stale readings, permission/sharing changes and timer cancellation. Keep an explicit route to the general map. | User checks denied location, stale member positions, account/circle switching, page exit/re-entry and offline map behavior. No default coordinate is labelled as a live user pin. |
| 7. Circle chat | Connect chat/quick messages/media to one delivery pipeline. Use one message ID from optimistic display through delivery; isolate cached/pending messages and stop polling on exit. | Unit checks for pending/retry/deduplication/account changes. User checks two accounts/circles, offline send/retry and sign-out. Pending and failed messages have visible states. |
| 8. Shared offline display | Adapt reusable tile/context layers for the general, Circle and Camera maps. Import approved package/context assets; exclude conversion-source MBTiles. Preserve routing datasets and exporter. | Adapt offline/archive checks and package verifier. User confirms a cold offline start in packaged coverage and honest out-of-coverage state. |
| 9. Camera UI and shelter selection | Port Athena Camera XAML, search, summaries, mode buttons and directions sheet. Preserve all dev AR overlays, arrival controls and diagnostic partials. Add 2D entry alongside current shelter AR entry. | Android resource/handler build; user checks layout and navigation access. Routing actions remain on the existing accepted lifecycle or inactive until batch 10 is ready. |
| 10. Shared journey and speech | Connect search/selection to dev route acceptance with hazards and trip/revision/cancellation ownership. Render one accepted route/progress/turn/arrival snapshot in both modes. Integrate voice/haptics using that snapshot and unified settings. Split route rendering, request ownership, and voice into separate patches if needed. | Relevant dev navigation/AR and Athena helper checks, Android build and release-readiness checks. User/device cases below pass before behavior-dependent fixes or promotion. |
| 11. Optional Admin audit viewer | Separate patch or branch after mobile scope. Keep dev permissions/session/sidebar and current RPC contracts; adapt only the viewer/services and access mapping. | Relevant existing RBAC and audit unit checks; admin lint/build. User checks allowed/denied roles. Schema changes require a separately reviewed plan; no automatic SQL execution. |
| 12. Preservation review | Compare against pinned dev and the accepted per-batch hunk list. Confirm requested Athena screens remain reachable. Reconcile only actual dependency changes outside the list. | No unexplained dev loss, removed checks, missing AR assets, obsolete app project import or unrelated generated-data replacement. Record remaining device/backend limitations. A final merge is a separate later action. |

At each gate, run the smallest relevant existing checks. Build once for a significant code batch; avoid repeated builds for a purely cosmetic/documentation adjustment. Add tests only for meaningful behavior gaps. Do not install new frameworks. Do not run integration/E2E/browser tests or interact with a running app automatically. If a check fails, investigate its immediate cause and allow one reasonable fix-and-retest cycle before reporting the remaining issue.

Keep a copy of each accepted patch and its check results, or a separate commit only if explicitly requested. Roll back only the failing batch and its dependent work in the integration worktree. Preserve the source branches and account data. Defer destructive storage/schema migrations until their ownership and rollback strategy have been reviewed.

## Targeted checks for the implementation phase

These are planned checks, not checks performed for this analysis. Run them only after the corresponding paths are changed or connected:

- Baseline/recovery: `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --multi-device-recovery`.
- Splash/startup: the same project with `--startup-scene`; navigation/recovery batches: select the relevant existing `--alignment-recovery`, `--placement-hazards`, or `--ground-flood` mode rather than running unrelated modes repeatedly.
- Adapted helper projects: `tests/RescuAR.SafetyCircleChecks`, `tests/RescuAR.SettingsChecks`, and `tests/RescuAR.CameraGuidanceChecks`, after porting and wiring their consumers.
- Significant Android changes: `dotnet build RescuAR.MAUI.sln`; Android release boundary: `./build/Verify-ARCoreReleaseReadiness.ps1` and the existing workflow/package requirements. Adapt `Verify-Phase4Package.py` before relying on it as an integration gate.
- Optional Admin batch: existing relevant RBAC/audit Node tests, `npm run lint`, and `npm run build` in `RescuAR.Admin`. No database integration tests or deployment writes are included.

## User/device validation after implementation

| Case | Expected result |
| --- | --- |
| Start/restart/sign out; sign in as another account | Correct onboarding/address/permissions/session handling; no previous account's profile, contacts, circles or messages are shown. |
| Save personal information, health, contacts and avatar independently | Each persists to the intended account and preserves fields managed elsewhere; failed/offline saves are labelled accurately. |
| Circle creation/join/rename/leave/delete across two accounts; network loss and revoked membership | Only confirmed or explicitly pending operations appear; creator deletion and member leave differ; removed membership cannot expose private cached data as current. |
| Missing/denied/stale location, cold offline map, offline chat and retry | No fabricated live pins; offline coverage/age is visible; pending chat survives safely and delivers without duplicates. |
| Select shelter A then B quickly; switch 2D/AR; reroute or lose connection | The current destination owns the request; both displays use the same accepted route revision and consistent progress/instructions. Retained-route behavior follows dev policies. |
| GPS drift/poor accuracy; route near shelter across a barrier; explicit user-arrival confirmation | No premature safe-zone announcement. Shared arrival conditions or clearly labelled user confirmation control completion. |
| Background/resume; tracking/frame recovery; mapped floor arrow; 2D/AR/Flood switching | Dev recovery, anchored cues, flood visualization and lifecycle cleanup remain consistent. Use the existing field-validation protocol and logs on supported devices. |
| Admin permissions, SMS workflow, routing export and unrelated dev modules | Existing allowed/denied access and dev workflows remain available; any checks not exercised are reported as unverified. |

Collect actual results from the user before making further fixes that depend on these behaviors. A passing build or helper check does not establish device, database or deployment readiness.

## Outcome of this analysis

The recommended Athena feature set is preserved in the plan. Its incomplete shared-state paths are adaptation work, rather than reasons to discard the visible modules. Current dev remains the default implementation for every other area. Only this report and its inventory/baseline artifacts are prepared; implementation and merging have not started.
