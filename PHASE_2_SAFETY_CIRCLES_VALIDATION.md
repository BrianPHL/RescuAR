# Phase 2: Safety Circles

Implemented on `migration/athena-modules`, following Phase 1 commit `fc888c2`.
This batch adapts the custom branch's circle management, switching, tutorial,
member cards, and caching into the existing `RescuAR.MAUI` application.

## Resulting behavior

- Create and join require authenticated cloud identity and confirmed membership.
  Failed requests do not invent circles or report local membership success.
  Refresh recovers a creator's interrupted membership insertion.
- Management supports selecting a circle, sharing its invite code, creator-only
  cloud rename/delete, and leaving a circle as a non-creator. Backend permissions
  and foreign keys remain authoritative. Failed mutations retain confirmed state.
- Saved circles, selection, member lists, locations, tutorial dismissal, device
  photos, and chat are scoped to both the Supabase project and account. Successful
  cloud refresh removes revoked/deleted memberships and their cached circle data.
- Chat uses stable client message IDs and a durable pending queue. `Sent` means
  cloud confirmation, not a read receipt. `Retry pending` reconciles cloud history
  before resending. Restart, failed requests, and lost responses do not silently
  turn pending updates into delivered messages or duplicate them on retry.
- Map and chat share the selected circle. Switching clears the previous circle's
  display and rejects late responses. Polling pauses when its page disappears.
- Missing/invalid locations are explicitly unavailable. Cached or older locations
  are labelled last known. Member markers use actual coordinates; no substitute
  coordinates, pin offsets, safety claims, or default battery percentages are used.
  The initial map viewport remains the existing Marikina viewport, not a user pin.
- Circle photos are explicitly **device-only**, because the existing circle model
  has no shared photo field. Member avatars use the existing profile data/cache.
- The dashboard identifies saved member lists without claiming live presence.

Routing policy/data, hazard handling, shared AR code, Android native/ARCore
configuration, and dependency pins were not changed. `RescuAR.App` was not added.
No database migration or alternate message-table fallback was introduced.

## Automated checks

Run from the repository root:

```powershell
dotnet run --project tests/RescuAR.SafetyCircleChecks/RescuAR.SafetyCircleChecks.csproj
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj
./build/Verify-ARCoreReleaseReadiness.ps1
dotnet build RescuAR.MAUI.sln --no-restore --disable-build-servers -m:1 -p:AndroidSdkDirectory='C:\Program Files (x86)\Android\android-sdk' -p:GenerateEvergineContent=False -p:GenerateEvergineScenesCode=False
```

The Safety Circle runner exercises the production synchronization/cache service
with a controlled remote, including offline reads/writes, account changes,
concurrent writes, lost-response recovery, membership removal, and location
freshness. It does not establish deployed Supabase policy behavior or device UX.
The content-generation flags above follow the existing validation workflow; the
native bridge and ARCore requirements remain enabled.

Local evidence on October 7, 2026:

- 52 Safety Circle checks passed.
- 351 existing routing/AR regression checks passed.
- ARCore manifest and compatibility readiness check passed.
- Final Android solution build passed with zero warnings and zero errors.
  Evidence is recorded in `artifacts/phase2-android-build.log`.
- The signed debug APK passed archive CRC checks. Its ARM64 ELF native bridge
  matches the repository binary. Evidence is in `artifacts/phase2-package-checks.json`;
  the installable copy is `artifacts/RescuAR-phase-2-debug.apk`. A native rebuild
  and full release-package toolchain checks were not performed.
- No Android devices were connected. Two-account/device flows have not been run.

The deployed `safety_circle_messages.id` must accept client UUIDs for stable
retry identity, and membership/message/owner operations must be allowed by the
appropriate RLS policies. Verify these existing database contracts during device
acceptance. Legacy unscoped circle/chat caches are not imported into another
account's cache. They are left untouched; confirmed history comes from the cloud.

## Required device acceptance

Use two existing test accounts on two Android devices. Do not treat the automated
controlled-remote checks as a substitute for these results.

1. On account A, create two circles. Copy one invite code; account B joins it.
   Confirm both devices show the same roster and invite code. Repeated join must
   not duplicate membership. Invalid codes and offline create/join show failure.
2. Rename the shared circle as its creator. Refresh B and confirm the new name.
   Verify B cannot rename/delete it. Force a request failure and confirm no false
   success or permanent local hiding. Check database RLS and foreign-key behavior
   if deletion is refused; do not bypass those protections in the client.
3. Send text and a photo from each account. Confirm each arrives once on the other
   device and uses the correct sender identity. With connectivity disabled, send
   text, restart the app, and confirm it remains pending. Restore connectivity and
   retry; confirm one cloud message and one message on B.
4. Switch circles rapidly while requests are in flight. Confirm map, members,
   invite code, messages, and device photo all belong to the selected circle.
   Select from management, return to map/chat, and confirm the shared selection.
5. Deny location permission or disable location services on B. A must never see
   B placed at A's coordinates or the initial map centre. Missing location is
   unavailable; an older published location is last known with its timestamp.
   Disable connectivity and verify saved locations do not claim live presence.
6. Leave as B; refresh both devices. Delete as A; refresh B. Removed memberships
   disappear, and cached messages cannot resurrect the removed circle. Confirm
   the warning before removing a circle with pending messages.
7. Sign out and use another account on each device. Confirm private circle,
   selection, photo, tutorial, and message caches do not cross accounts. Check
   returning to pages after switching accounts and after failed requests.
8. Navigate away from the map/chat and back repeatedly. Confirm polling resumes
   only on the visible page, location sharing stops with the map page, and no
   previous circle's late result replaces the current display.

## Admin baseline repair

The missing `RescuAR.Admin/src/services/prepInundationApi.js` has also been
restored. It is a transport adapter for the existing external-model screen,
with configurable endpoint, cancellation/timeout handling, response validation,
and preserved model/evidence fields. It contains no local prediction formulas.
See `RescuAR.Admin/README.md` for its frontend request/response contract. The
deployed PREP R/Plumber endpoint and compatibility are still unverified.

Admin build and four transport tests passed. Source lint passed with existing
warnings using `npm run lint -- --ignore-pattern node_modules --ignore-pattern dist`.
The unrestricted lint command scans installed dependency files and fails on
third-party code. Build output also contains the existing large-bundle warning.

No live account records or model service were modified during validation.
