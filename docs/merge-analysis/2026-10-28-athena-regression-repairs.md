# Completed Athena regression repairs — 28 October 2026

Completed the user's approved repair sequence in the separate integration worktree and copied each verified batch to the clean `migration/athena-modules-pt2` build checkout. No merge, push, database change or APK packaging was performed.

## User choices applied

- Main directory, Camera 2D search and pins share live Supabase records and the saved offline directory. Real string IDs, coordinates, supplied metadata and update time are retained.
- Before live/saved data is available, use the main app's nine historical locations. Do not reinstate Athena's sixteen-entry fallback or invented current capacities, status, contacts or facilities.
- A successful empty server response clears directory entries and old directory pins; failure retains labelled earlier data. An active journey's separately owned destination/route is preserved.
- Restore both directory-to-2D journey actions and retain the AR action. View Map and low-light Open 2D Map open Camera 2D, preserving an existing trip and never automatically selecting the first center.
- Update changed chat rows; preserve older-message reading position; show confirmed/cached numeric membership counts without an Online claim.
- Restore Athena-style inline Circle create/join/invite controls. Continue using confirmed account-scoped membership operations and existing creator-only management rules.

## Result

### Shared evacuation directory and map refresh

`EvacuationDirectory` is the shared list/search/pin source. The main directory and Camera can refresh it independently; Camera no longer depends on first visiting the main list to populate its data. The server adapter reads paged records and preserves string IDs instead of replacing GUIDs with numeric IDs. Invalid coordinates/missing identities are rejected; a nonempty unusable response is a failure, not an authoritative empty result.

The offline envelope retains source URL, check time, hotline data and explicit empty confirmation. Legacy nonempty directory files remain readable. Cache replacement preserves the previous file on failure; hotline updates cannot revive an empty directory. Another configured server cannot reuse a cache stamped for a different source. A valid public snapshot committed to disk remains consistent in memory if its requesting page exits during the commit; each view still guards its own lifetime before showing data.

Camera tracks both trip and directory revisions and refreshes directory pins/search on a directory update or activation. It retains the accepted route and pan/zoom. A removed or moved selection must be selected again before starting navigation. The existing nearest-center repository consumer reads the shared snapshot as well; unrelated existing area-status/provider behavior was not rewritten.

The historical fallback contains location context only. The main list no longer invents capacity, officer/contact or facility details when those are unavailable. Directory status and saved/previous/historical notices remain separate from actual current availability.

### Selected-center 2D entry

Both “Start 2D Map Guidance (Journey)” actions are restored on the evacuation list/details. The existing AR action explicitly selects AR presentation. A one-use view request is tied to the current destination version; a consumed, cancelled or obsolete request cannot force a later trip into the wrong view.

The directory's View Map and Camera's low-light Open 2D Map now open Camera's actual 2D module. Browsing does not publish a destination, choose the first center or replace an existing journey. Selected-center actions still use the shared destination, hazard-aware routing, qualified GPS, progress and arrival flow. No parallel router or immediate arrival shortcut was restored.

### Circle chat

Unchanged polling results no longer clear/recreate the conversation. Keyed reconciliation keeps existing rows, updates changed delivery/content, and inserts/removes/moves only affected rows. Pending-to-Sent changes are not missed when the message ID stays the same. The collection retains its scroll offset; new incoming messages do not force the end while the user reads older history, while self sends/end-following still may scroll.

An account- and membership-checked exact count supplies the numeric membership label. The count/check time is retained in the scoped chat cache and saved counts are labelled cached. A failed count does not invent a value or prevent otherwise available history. The local group-photo label is separate. No member is declared Online merely because they belong to the Circle.

### Inline Circle controls

The Circle selector includes Create, Join and Invite code actions. Inline cards collect a name/code, report progress/failure and display/copy the actual confirmed invite code. Create/join still call the approved membership service; a cached or unconfirmed response cannot produce success. Inputs survive a failed request; account/page exit clears the form. Membership polling pauses while editing/submitting so it cannot interrupt form input, while the page-owned local-location freshness path remains active. A membership change resets upload consent to Off.

Management remains available for existing rename/leave/delete/photo/roster actions. Its creator-only delete/rename rules are unchanged. Private data remains scoped by account/server/Circle, and no archived legacy state is restored.

## Commits

| Batch | Integration | Build checkout |
| --- | --- | --- |
| Shared directory, cache and revision-driven pins | `269bd0b` | `9bf9e7c` |
| Directory/low-light 2D entry and one-use view handoff | `db5bc9c` | `6f8d327` |
| Stable chat rows and truthful member count | `f9e82f3` | `5704858` |
| Inline create/join/invite cards | `48d6d53` | `fa4387e` |
| Directory commit/page-exit consistency | `1b535a8` | `2d9a0c1` |

A separate whitespace-only model cleanup is `fcbf10b` / `ffff1fc`.

## Verification and preservation

- **467 combined local checks passed** on the final source, including directory/cache, one-use view requests, membership/sharing, chat delivery, row updates and the existing authentication/profile/settings/report/offline policies. The directory group now contains 29 checks; fake services and isolated file fixtures are used.
- **41 existing Camera-map checks passed:** accepted route identity, qualified GPS, viewport, shared directions and arrival rules. Focused runs also passed 7 view-handoff, 61 chat/presentation and 125 membership/map checks; these are not additive coverage totals.
- **Final Release Compile with actual XamlC passed, 0 warnings/errors**, in the build checkout using `DesignTimeBuild=false`, `RescuArDiagnosticBuild=false`, `MauiXamlCValidateOnly=false`, `BuildProjectReferences=false`. Existing core Release output was reused. No APK was generated. Earlier batch compilation also passed. One initial namespace collision was corrected before the directory batch was committed.
- **ARCore declaration/compatibility static check passed.** No native/dependency pins, package resources, routing datasets, core routing source or database files changed.
- **31 changed source/test paths match across checkouts.** An existing trailing-whitespace-only difference in `CommunityReportService.cs` predates this work and was left untouched.
- All **861 dev paths** remain accounted for: **778 exact**, **82 approved adaptations**, and the one previously approved root Admin archive deletion. **32 pinned AR/navigation reference files remain exact.** Camera's original dev body has the five earlier approved display/feedback/lifecycle hook sites plus the explicitly approved low-light 2D shortcut; no other new change to that body.
- Admin still exactly matches the preserved dev tree `036fd835eb4077192b04b4d9a48d39da3ba58cf4`. Both source branches retain their pinned refs. No branch merge was performed.

Source assertions, fake-service checks and XAML compilation do not establish real Android scrolling, permission/lifecycle behavior, live database/RLS visibility, actual server row counts or map-provider availability. The earlier 12 tile-fetch contract checks are unchanged-source evidence, not a new device run in this repair pass.

## Device checks after a later requested build

1. Open the main directory with all filters cleared, then Camera 2D search with its query cleared. Expect the same underlying catalog and real center IDs/coordinates, with honest source/date/unknown labels. Refresh the directory while keeping the same trip; expect changed pins/search, preserved route and pan/zoom. A confirmed empty list clears directory pins; a failed query keeps labelled earlier data.
2. Start 2D from a chosen center's card and details, then start AR. Expect the selected center in the requested view with the same shared route flow. Directory View Map and low-light Open 2D Map must open Camera 2D without selecting a different destination.
3. Read older chat messages through several unchanged polls and new incoming messages; expect no forced end jump. Check Pending/Failed/Sent/retry updates, a confirmed numeric member count, cached count offline and a separate local-photo label.
4. Use inline create/join/invite. Failure must retain the form and never claim membership. Success must use the actual Circle/code, refresh selection and leave sharing Off. Verify creator/member management restrictions and account/Circle/background transitions.

Report actual results before further behavior-dependent fixes. APK packaging and live device/backend acceptance remain pending the user's later run.
