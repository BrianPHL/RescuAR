# Athena preservation audit — 27 October 2026

Compared these exact local Git sources, without changing application behavior:

- Athena: `migration/athena-modules` / `fa777da153a492268ace76162a80389e0d904534`.
- Integration: `codex/integrate-athena-modules` / `9fc746878dde90080f69562e6a2f008058818076`.
- Preserved dev source: `004ed201b3f2e0b7922dc4cbb6c784bdddaa6ad9`.

**The three-center list is a confirmed preservation regression.** The app's main directory already queries Supabase's `evacuation_centers` table and saves an offline directory. Athena's repository consumed that directory cache and had a 16-entry fallback. Integration retained dev's static three-entry repository instead. The Camera 2D search and marker builder both call that repository. This is a lost connection, not an entirely unimplemented feature, and not a `Take(3)` display limit. The earlier restoration review missed it.

The live database's actual row count was not queried. Sixteen is the number of Athena's hardcoded fallback entries, not a verified live-center count or a recommendation to reinstate their hardcoded capacities/open statuses.

## Findings

Git evidence below uses pinned tree paths (`revision:path`), with line numbers for the integration snapshot.

### 1. High priority: the 2D map no longer consumes the main evacuation directory

- `fa777da:RescuAR.MAUI/Data/EvacuationCenterRepository.cs` loads `CacheService.LoadEvacuationCenters()`, maps saved records into map entries, and falls back to 16 static centers.
- `9fc7468:RescuAR.MAUI/Data/EvacuationCenterRepository.cs`, lines 5–31, returns only Marikina Sports Center, Marikina City Hall and Riverbanks Center. It also omits their IDs/capacity/occupancy/status values.
- `9fc7468:RescuAR.MAUI/Views/Camera/CameraPage.Map2D.cs`, lines 248 and 290, uses that repository for both pins and search. There is no three-row truncation.
- `9fc7468:RescuAR.MAUI/ViewModels/Prepare/EvacuationCenterInfoViewModel.cs`, `LoadData` / `FetchEvacuationCentersFromSupabaseAsync`, already loads cache, fetches backend records and saves the resulting directory. That view model is preserved dev source, so its existing main-directory implementation was not removed.

**Effect:** Camera cannot list or select the other centers that appear in the main directory, and its available directory metadata is missing. Repository-dependent nearest-center fallback consumers are also limited to the same three.

**Recommended retention:** use one directory snapshot for main list, Camera search and pins, preserving backend identity, valid coordinates, recorded metadata, cache/freshness and honest unknown status. Use the main directory's historical fallback when live/cache data is unavailable. Do not blindly copy Athena's fake numeric replacement IDs or treat hardcoded capacities/statuses as current evidence.

### 2. High priority: main-directory 2D navigation entry points were lost

- Athena's `Views/Prepare/EvacuationCenterInfoPage.xaml` has **two** “Start 2D Map Guidance (Journey)” actions: a list-card action and a details-popup button. Both are absent from the integration page.
- Athena's `ViewModels/Prepare/EvacuationCenterInfoViewModel.cs` includes `NavigateTo2DMapAsync` / `Start2DNavigationAsync`, publishes the selected destination, requests 2D mode and opens Camera.
- Integration's `NavigateToMapAsync`, lines 1235–1240, simply opens `//Map`. Its `AppShell.xaml`, lines 28–34, maps that tab to `SafetyCirclePage`. The selected evacuation center is not published by that action.

**Effect:** a user browsing the real directory loses Athena's direct 2D journey flow. “View Map” opens a Circle map instead of the selected center's evacuation map. AR guidance remains available, but is not an equivalent replacement for the lost 2D actions.

**Recommended retention:** restore Athena's 2D list/details actions and center-specific map entry while retaining dev's accepted routing/GPS/hazard/progress/arrival flow. Select 2D presentation for the intended trip without restoring Athena's independent hazard-free router or introducing a stale global view flag.

### 3. Medium priority: directory updates do not invalidate Camera's marker layer

- `9fc7468:RescuAR.MAUI/Views/Camera/CameraPage.Map2D.cs`, lines 172–177, rebuilds `cameraMapPlacesLayer` only when `destination.Version != cameraMapPlacesTrip`.
- `StopCameraMapDisplay` does not invalidate `cameraMapPlacesTrip`; reopening the 2D view with the same trip does not force a catalog rebuild.
- Athena's mode-entry flow called its map/destination builders again on entry.

**Effect:** reconnecting the repository alone is insufficient. A directory refresh while the destination remains unchanged could update a newly opened search list while leaving the old pins visible. The present three-entry stub masks this defect because it never changes.

**Recommended retention:** track the directory revision or refresh its snapshot on activation, rebuild only directory/destination features, and preserve the accepted route and user's pan/zoom.

### 4. Medium priority: Circle chat resets unchanged history every poll

- `9fc7468:RescuAR.MAUI/ViewModels/Map/CircleChatViewModel.cs`, lines 66–67, polls every five seconds. `RefreshAsync` passes each snapshot to `Present`.
- `Present`, lines 119–125, always calls `Messages.Clear()` and recreates every row, including unchanged snapshots. It requests end scrolling only if the last message ID changed.
- Athena's `RefreshMessagesSilentAsync` compared count/last ID and rebuilt only when it detected a change.

**Effect:** unnecessary full collection resets are confirmed in source; visible jumping/flicker or lost reading position is a runtime risk, not a reproduced device result. This is avoidable degradation of the chat interaction.

**Recommended retention:** update keyed message rows only when content/delivery changes. Preserve Pending/Failed/Sent/retry behavior and update an acknowledgement even when the message ID stays the same; Athena's old count/last-ID shortcut alone would miss such changes.

### 5. Lower priority: chat's actual member count was removed with its false Online claim

- Athena read members and displayed their numeric count, incorrectly appending “Online.”
- `9fc7468:RescuAR.MAUI/ViewModels/Map/CircleChatViewModel.cs`, line 95, replaces the whole count with “Circle members · photo is local” or a cached label.

**Effect:** dropping the unsupported Online claim was appropriate; dropping the verified membership count was not necessary to achieve it.

**Recommended retention:** show a confirmed member count when available, identify cached/unavailable counts explicitly, and keep the local-photo label separate. Do not equate membership with current online status.

## Workflow differences to decide explicitly before further changes

The Circle map no longer contains Athena's inline Create/Join/invite-code/delete/tutorial popups. It routes to “Create, join or manage Circles” instead. Membership creation/join/copy/leave/rename/delete still exist in management under the approved account and permission checks. This is a user-flow simplification, not complete loss of Circle membership functionality. Retaining this central management flow versus restoring Athena-style inline controls needs a presentation choice; either must use the same confirmed membership services.

The following are approved adaptations and should not be reverted as regressions: default-Off uploads with independent local self location; truthful dated/unknown readings; no displaced or fabricated pins; local labelled group photos; explicit attachment Send/retry; profile field-only saves; username/middle-name preservation and read-only verified email; shared dev routing/arrival; shared saved voice preference and cancellation; scoped cache clearing; untouched dev Admin/flood/data sources. The splash radar/banner and profile, health/contact and registration layouts/actions remain present in the source comparison.

## Inherited gaps and a correction to the earlier report

1. **Offline tile-interface origin:** Athena's `DetailedOfflineMapService.LocalTileSource` also implements only `ITileSource`. The earlier 26 October report incorrectly implied this was introduced by integration. It is an inherited rendering defect, now repaired by integration's `PackagedMapTileSource : ILocalTileSource`. The actual-library contract check and repair remain valid; attribution of its origin is corrected here. The dropped application-specific online tile identifier and added raw graph overlays were separate presentation/configuration regressions.
2. **Low-light map shortcut:** both branches' `OnLowLightMapClicked` open `//Map`, although the button says “Open 2D Map.” With `//Map` assigned to Safety Circles, it does not open the active journey's 2D view. Include this inherited mismatch in the map-entry repair, rather than classifying it as newly introduced.
3. **Profile notification placeholders:** five profile subpages reference missing `ic_notification.png` with no click command. The same placeholder exists in Athena, so it is not an integration regression. Existing missing `trash.png` / `carina.jpg` references are also not newly missing Athena assets. No new unresolved static image reference was identified in the compared imported layouts.

## Recommended repair sequence

1. Reconnect the main directory, Camera list and pins to one validated, freshness-labelled source; remove the three-entry stub; add directory-driven marker refresh. Review fallback policy before changing it.
2. Restore Athena's two 2D journey actions and correct directory/low-light map entry, using dev's shared navigation state and preserving separate Circle/General map entries.
3. Preserve chat reading position with keyed row updates; restore a truthful numeric member count.
4. Decide whether Circle membership controls should remain in management or return as Athena-style inline map controls. Preserve all approved identity/membership/privacy/delivery guards in either presentation.

## Verification boundary

This pass inspected the pinned Git trees, compared named controls/actions in 12 major Athena layouts, inventoried retained-dev versus unported-Athena paths, traced directory/cache/navigation/chat consumers, checked source assertions and classified inherited gaps. It did **not** run a new application build, unit suite, browser/app/device interaction, backend query, APK package, migration, merge or deployment. Prior compilation/unit checks are not evidence that these end-to-end data/UI connections work. Application source remains unchanged in this pass; only analysis records are updated.

After repairs, targeted checks should cover directory/cache consistency, changed-directory pins without a trip change, selected-center entry into 2D, route sharing with AR, unchanged-poll chat history, pending-to-sent row updates, and truthful counts. Device display/scroll behavior still requires user confirmation after a later build.
