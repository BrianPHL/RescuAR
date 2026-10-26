# Athena integration progress — 26 October 2026

## Active baseline

- Integration branch: `codex/integrate-athena-modules`.
- Dev baseline: `1b3db7e0512f95d554f91ccce36f7c8ad705ec6e` (includes the previously uncommitted MLD guidance/recovery work).
- Athena source: `fa777da153a492268ace76162a80389e0d904534`.
- The earlier analysis report/CSV/JSON retain their historical `982146b` comparison. This log and the integration-baseline manifest record the implementation baseline.
- Completed batches are committed at the user's explicit request. No branch merge has been performed. Source branches and the original checkout are preserved.
- User instruction: commit every finished change after its relevant checks pass. Continue asking before each overlapping behavior change.

## User decisions and applied scope

| ID | Overlap | User decision | Applied change |
| --- | --- | --- | --- |
| B0-01 | Regression runner parser calls versus recovery checks | Adopt only Athena's four argument fixes; retain every dev recovery check | Four parser invocations now pass `RouteProvenance.Unreported`; dev MLD/multi-device checks retained. |
| B0-02 | Routing-parser missing-provenance handling | Adopt Athena's guard; retain dev routing implementation | One null-provenance fallback hunk in `MLDRoutingService.cs`. |
| S1-01 | Native splash SVG and project splash attributes | Adopt Athena's dark splash and AR/radar symbol | Athena SVG; splash color `#0B132B` and size `128,128`. Dev native-library requirements retained. |
| S1-02 | In-app splash XAML and animation | Adopt Athena's banner and animated radar | Athena SplashPage XAML/code and referenced banner PNG. |
| S1-03 | SplashViewModel startup/session behavior | Keep dev for the visual batch | Kept for batch 1; later superseded by the explicit A2-05 session-startup decision below. |
| A2-01 | Backend initialization and saved sessions | Adopt secure saved sessions and guarded initialization; keep dev server settings | Added secure-storage persistence, ordered writes, project-specific token slots, initialization serialization and stale-client rejection. |
| A2-02 | Authentication client readiness | Wait for initialization | Email/Google sign-in, registration and sign-out await the shared client; sign-up fields and UI flows are unchanged. |
| A2-03 | Local cleanup on sign-out | Adopt reliable cleanup, including network failure; preserve stored account data | Flush/remove stored session; mark removed slots signed out; archive legacy account preferences before clearing active aliases. Archives carry ownership hints only and are not automatically restored as verified account data. |
| A2-04 | Post-sign-in, OTP/address and permissions routing | Adopt one verified-identity/address/permissions flow | All verified entry paths use the shared coordinator, with account/revision guards before navigation. |
| A2-05 | Startup source of identity | Use restored identity with explicit offline/error handling | Startup validates remembered identity, retains local offline access, shows retry for service errors, and returns to login for rejected sessions. Saved login flags alone cannot open an account. |
| A2-06 | Google saved-account choices | Require real Google authentication for every selection | Both selection commands invoke the real provider flow; dev's existing callback handler is preserved and Athena's duplicate is excluded. |
| A2-07 | Login background profile writes | Preserve fields; create only after a confirmed missing row | Removed the background partial-object write; a successful empty read permits an insert that ignores racing duplicates. Failed reads do not authorize creation. |
| A2-08 | Address saves | Update only the address | Address saves use one field-specific update, retain dev address formatting and guard against an account change. |
| A2-09 | Remember Me | Checked remembers login; unchecked requires login after restart | Bound the existing control to secure persistence; an unchecked login leaves no restorable session. |
| A2-10 | Login screen and password-reset dialog | Adopt Athena's layout and dialog; preserve completed verified sign-in/Remember Me behavior | Athena presentation with correctly separated recovery panels, the dev password policy, a temporary non-persistent recovery client, and cancellation/ownership checks. |
| A2-11 | Registration fields and two-step layout | Use Athena's two steps; keep optional middle name; defer the age restriction | Resident details then account credentials, dev 10-character password rules, the complete dev barangay catalog and the existing verified account flow. No minimum-age rule. |
| A2-12 | Legacy local-data restoration | Keep all legacy archives aside; start fresh local preferences | No automatic archive read/restore; archived files/preferences are preserved. Verified cloud profiles and new account-scoped caches remain usable. |
| P3-01 | Profile overview and avatar | Adopt Athena layout and account-scoped photo handling | Preserve dev advisory overlay/actions; isolate new local photos by project/account; expose local-only versus confirmed cloud status and retry. |
| P3-02 | Personal information | Athena layout; retain username/middle name; verified email read-only; field-only saves | New editor keeps the dev fields, patches only changed values and preserves raw/unfamiliar addresses until edited. |
| P3-03 | Health information | Athena layout/guide; retain all dev health fields; save only health fields | Separate editor retains the seven health fields and existing guide-completion key; changed-field patches preserve identity, contacts, avatar and circle fields. |
| P3-04 | Emergency-contact layout/import/save | Adopt Athena layout/import; save only when confirmed | Import fills the form. Save patches only changed contact fields. Relationship labels remain scoped to this account/device; late results are rejected. |
| P3-05 | Dashboard contact shortcuts | Synchronize after Save; preserve dev dashboard and unrelated shortcuts/order/toggles | Account-scoped shortcut storage, stable contact IDs, matching-contact reuse, queued first-dashboard sync and explicit local-failure/retry status. Legacy archives stay aside. |
| P3-06 | System Information | Athena layout with real app and permission information | Actual installed version/build and Android package update date; current camera/location/notification permission state and motion-sensor availability; unavailable values are labelled honestly. |
| D0-01 | Newer dev AR/MLD changes at 53d7cab | Carry over now in a separate reviewed commit | Fourteen non-overlapping files match the newer dev source; no Admin files or branch merge included. |
| D0-02 | Shared regression runner | Add all new dev checks; retain current checks and parser fixes | Runner matches newer dev plus the four previously accepted parser argument fixes. |

Each further overlapping behavior requires a user decision before modification. Approval of one row does not approve unrelated hunks in the same file.

## Validation record

- Initial baseline recovery-check attempt failed before application changes because the fresh worktree lacked generated `EvergineContent` metadata. Invoking the existing `EvergineContentGeneration` target repaired this build-output issue without changing project settings. The single fix-and-retest cycle passed.
- **29/29 multi-device recovery checks passed**, including the dev frame-requalification and world-target safeguards.
- **35/35 MLD guidance ownership checks passed**, including the newly committed dev guidance/recovery work.
- **3/3 startup-scene checks passed**.
- **5/5 focused parser checks passed**: the four existing parser assertions were extracted into a temporary SDK-only project using the built application assembly, plus a missing-provenance case. No repository test framework or dependency was added; the full regression suite was not run.
- **Android solution build passed: 0 warnings, 0 errors.**
- **ARCore release-readiness declaration check passed.** This checks configuration consistency and does not establish device readiness.
- **Source preservation review and `git diff --check` passed.** Only the approved six tracked files plus the referenced new banner asset changed. `SplashViewModel.cs`, native-bridge guards, dependency versions, dev AR/MLD recovery code and all dev recovery assertions are preserved.
- Imported splash text files had trailing whitespace; it was removed and line endings normalized without behavior changes after the successful build. No redundant rebuild was performed for formatting only.
- No running app, browser, device, integration or E2E verification was performed. The APK is a local debug build, not a release/deployment claim.

## Manual splash check

Install the signed debug APK at `artifacts/athena-integration/01-splash/RescuAR-Batch01-Splash.apk`. Fully close and reopen the app. Expect Athena's dark native AR/radar splash, then the banner/animated radar, followed by the same onboarding/login/permissions/dashboard choice dev previously made. Report the actual appearance, transition and any crash/stall before further behavior-dependent fixes.

## Authentication service validation

- **16/16 targeted authentication checks passed**, using fake local storage and the existing Supabase dependency. They cover ordered session saves/removal, restart/project isolation, disabled writes, corrupt or unavailable storage, legacy identity aliases, preservation of account details and failed archive writes.
- **Android build passed: 0 warnings, 0 errors.**
- `git diff --check` and the approved source-path review passed.
- Secure-session removal sets a non-secret signed-out marker before removal. A storage deletion failure therefore does not permit the old token to be silently restored later.
- Legacy saved profile/contact/shortcut/circle preference data is retained in local `AuthIdentityArchive:*` snapshots before active preferences are removed. Current user IDs are recorded as hints; no archive is automatically assigned to a new account. Media files and stored account-specific data are not deleted.
- These service checks preceded the next explicit routing/profile/Remember Me approvals. Their implementation and validation are recorded below; authentication screen redesigns remain separate decisions.
- No live-provider, backend, application, device, integration or E2E validation was performed. Do not infer those results from the build or fake-storage checks.

## Earlier next gate (superseded by the decisions below)

Obtain the user's next decisions for the shared post-sign-in flow, saved-session startup routing and the Google account-picker path before changing their view models/navigation. Obtain the remaining Remember Me/profile-storage/UI decisions before wiring those behaviors. Device splash appearance/transition remains a user verification step; use the preserved batch-1 APK above.

## Completed-batch commits

- `0498405` — `fix(routing): handle missing provenance in parser checks`
- `70b5139` — `feat(splash): adopt Athena branding and radar animation`
- `5744060` — `feat(auth): persist sessions and preserve identity on sign-out`

The analysis/inventory/progress records are committed separately after these batches.

## Earlier routing decisions (implemented below)

- Use one verified-identity/address/permissions flow after email/Google/OTP sign-in.
- Replace flag-only startup with restored identity plus explicit offline/error handling, preserving offline access.
- Require real Google authentication for every displayed account selection.

The original `dev` branch advanced to `822ded0` with an RBAC UI patch after the integration baseline was pinned. Those Admin changes are outside these completed batches and must be preserved/reconciled at the Admin/final preservation gate; this branch was not silently updated.

## Shared authentication flow completed

- Commit: `28e302c` — `feat(auth): unify verified sign-in and offline startup`.
- **38/38 targeted authentication checks passed** (the prior 16 storage/cleanup cases plus 22 routing/ownership cases). These are local fakes; no provider/backend/device interaction was performed.
- Initial Android build found a connectivity namespace collision and a nullable-token warning. The single correction/rebuild passed with **0 warnings and 0 errors**.
- Adding the late-rejection guard changed the safe account-mismatch outcome from sign-out to cancellation. Its older test expectation was corrected in one fix-and-retest cycle; all 38 checks passed afterward.
- ARCore configuration/dependency declarations passed. Protected AR, camera/navigation, native-service code, user model, registration XAML, Admin source and the existing Google callback are unchanged from the pinned dev baseline.
- `git diff --check` and the approved source-path review passed. Other registration fields/validation and provider configuration were preserved.
- Preserved debug APK: `artifacts/athena-integration/02-authentication/RescuAR-Batch02-Authentication.apk`.
- Existing flag-only dev installations without a secure saved session require **one online sign-in** to establish remembered offline access. An expired/rejected session is not converted into a successful offline account.
- Legacy snapshots remain preserved for an explicit account-data migration decision; uncertain ownership is not silently reassigned. This can leave old local customizations unavailable until that migration is decided.

## Required manual authentication checks

Use a test account and the preserved batch-2 APK. Report actual results before behavior-dependent fixes:

1. Sign in with Remember Me checked; fully close/reopen online, then offline. Expect the same remembered account, honest offline notice and local-map access. Repeat with Remember Me unchecked; expect login after restart.
2. Select each Google account option, including Use another account. Expect the actual Google flow; cancelling must not create a synthetic signed-in identity.
3. Use an account with no address, complete the address and permission flow, and reopen the app. Expect the appropriate gates; existing health/contact/profile fields must remain unchanged after an address save.
4. Sign out, restart and switch accounts. Expect no previous-account identity, contacts or circle selection to appear as the new account's current data. Rejected sessions should open sign-in; temporary service problems should expose retry rather than a false successful login.

Provider, deployed schema/RLS, device lifecycle and offline coverage remain unverified by local checks.

## Earlier form gate (completed below)

The user approved the login/reset UI, two-step registration with optional middle name and no minimum-age rule, and keeping legacy archives aside with fresh active local preferences. The next entries record those decisions and their verification.


## Authentication forms completed

- Commit: `bf851f8` — `feat(auth): adopt Athena login and two-step registration`.
- Adopted Athena's login layout and in-app recovery dialog. Email, recovery-code/password and success panels are mutually exclusive. Closing/leaving the page rejects late callbacks and clears entered secrets.
- Recovery uses an isolated Supabase client with no shared persistence or refresh worker. Successful recovery clears its temporary token/user values and shuts down listeners; it does not authenticate the main app or claim success after a failed update. No provider messages were sent during verification.
- Registration now has resident and account steps. Optional middle name and dev's exact 10-character, uppercase, digit and allowed-symbol requirements remain. No age-ten restriction was imported; future birthdays remain invalid. The selector uses dev's complete 16-barangay catalog, including Calumpang.
- The non-secret registration draft contains resident details only. Birthday/address are sent as signup metadata; verified email/OTP identity still gates profile activation and shared navigation. No public-profile birthday column or database migration was added. The registration Google button uses the already-approved real provider flow.
- The user explicitly chose **all legacy archives aside, fresh active local preferences**. No automatic archive restoration is implemented. Archives/media are preserved; this policy does not remove verified cloud profiles or new account-scoped caches.
- **64/64 focused authentication checks passed** (38 earlier storage/routing cases plus 26 form/recovery cases). These use local fakes and do not establish provider, deployed schema/RLS or device behavior.
- Initial Android compilation found an inaccessible SDK cleanup method. One correction/rebuild used the supported public session properties. That build exposed an unrelated missing registration Google command binding; one correction/rebuild connected the approved flow. Final Android build: **0 warnings, 0 errors**.
- ARCore declaration consistency, approved source-path preservation and `git diff --check` passed. Dev AR/camera/navigation/native code and Admin remain preserved relative to the pinned baseline. The only routing-parser difference remains the previously approved missing-provenance guard.
- Preserved debug APK: `artifacts/athena-integration/03-authentication-forms/RescuAR-Batch03-AuthenticationForms.apk` (103,188,724 bytes; SHA-256 `b438a21f61bcea966695f92c1d3b52505dd9013d90c1b76c0c44e352f89b7d7a`).
- Later dev is now `444cd18`. Its new administrator-provisioning/password-setup changes, in addition to `822ded0` RBAC UI, are deferred to the Admin/final preservation gate. No branch update, merge, push, database migration or deployment was performed.

## Required manual form checks

Use a test account and the preserved forms APK. Report the actual results before behavior-dependent fixes:

1. Open registration, complete resident details and proceed/back between steps. Expect values to persist, middle name to stay optional, the complete barangay list, no minimum-age-ten rejection and dev's 10-character password criteria. Complete email verification; expect the correct address/permission gate for the verified account.
2. Open password recovery. Expect only the email panel, then only the code/password panel after a successful request, then success only after a confirmed update. Weak passwords and invalid codes must show an error. Sign in with the changed password to check the provider result. The deployed recovery-email template must provide a code compatible with this dialog; that configuration is unverified here.
3. Close recovery while waiting, reopen it, and return from registration. Expect no old result or secret values in a new dialog and no recovery-based main-app login. Verify Remember Me and Google cancellation still follow the earlier batch's expectations.
4. Sign out and change accounts. Expect no archived local profile/contact/shortcut/circle customization to be restored automatically. Existing server profile fields should remain intact.

## Earlier profile gate (completed below)

All three profile decisions were explicitly approved. Their applied scope and validation follow.


## Profile, personal and health screens completed

- Commit: `f2b0c03` — `feat(profile): adopt Athena screens with scoped field saves`.
- Adopted Athena's profile overview and personal/health layouts. The profile advisory overlay and unrelated dev contact/circle/advisory methods remain unchanged. Personal and health pages now use their own editors rather than the shared whole-profile save command.
- Personal saves retain username and optional middle name, show only the verified email as read-only, and patch only changed fields. Changing a name cannot reformat a stored address. Existing three-, five- and six-part addresses are supported; edits preserve city/region/postal suffixes. The dev 16-barangay catalog remains authoritative for new selections.
- Health saves retain health-card number, blood type, allergies, maintenance medication, average blood pressure, disability/special needs and organ-donor status. The tutorial and its existing device completion flag remain. Only changed health fields are patched. Unfamiliar saved selector values remain available so an unrelated edit cannot erase them.
- Each screen captures the account, backend project and session revision. Loads and saves reject late results after an account/session change. Obsolete form values clear before a new account loads, and acknowledged saves refresh the displayed values and edit baseline together.
- New photo files and state use a project/account-specific cache. Existing global photo paths and legacy archives are not automatically restored. Photos remain locally available after restart for their owning account. This batch does not download every remote avatar for offline use.
- A photo becomes cloud-confirmed only after both storage upload and the avatar-only profile update succeed. Local-only photos expose retry. Acknowledged upload URLs are reused when only the profile patch failed; uncertain uploads receive a fresh object name on retry. Dev's existing `avatars` bucket and user-ID filename prefix are retained. No storage-policy or schema change was made.
- Photo failure/cancellation does not write a local filesystem path into the public profile. Old-account callbacks cannot confirm a newer account's photo. Existing archives/media are preserved; abandoned remote uploads are not deleted automatically.
- **93/93 targeted local checks passed**, including all 64 prior authentication/form cases and 29 new field-isolation/address/photo-failure cases. They use fakes, not a running app or live provider.
- Final Android build: **0 warnings, 0 errors**. ARCore declarations, source-path preservation and `git diff --check` passed. Additional builds followed concrete source-review corrections for saved-value alignment, photo retry and unfamiliar selector preservation. A staging-script typo applied no source changes and was corrected before the final successful adjustment/build.
- Preserved APK: `artifacts/athena-integration/04-profile/RescuAR-Batch04-Profile.apk` (103,217,396 bytes; SHA-256 `6d3884005be4eb820c63cf0a3b54c2fec308b91d9f06fbc4ca4a96562903c3df`).
- Device appearance/lifecycle, deployed profile permissions/RLS and avatar bucket upload access remain unverified. No app/browser interaction, integration/E2E tests, backend writes, deployment, branch merge or push was performed.
- **Remaining contacts limitation:** dev's existing contact screen still uses the broad `SaveProfile` command until its next explicit overlap decision. The newly approved personal, health and avatar paths use scoped partial updates; this is not a claim that every profile-related path has been migrated yet.

## Required manual profile checks

Use a test account and the preserved profile APK. Report actual results before behavior-dependent fixes:

1. Edit username/middle name and save/reopen personal information. Expect the verified email to remain read-only and health/contact/circle/avatar fields to stay intact. Change a health field and reopen both screens; expect only the selected health change. Existing unfamiliar selector values must remain when another field changes.
2. Select a photo using gallery and camera, including denying camera access. Expect no broad storage permission request for the system gallery picker and no false success after denial. A confirmed photo should remain available offline after restart for the same account.
3. Change a photo offline or with upload/profile access denied. Expect a visible local-only status and retry. Reconnect/retry; expect cloud confirmation only after the account profile actually updates. A successful upload followed by a failed profile patch must be retryable.
4. Switch accounts while a load/upload/save is waiting. Expect no old-account details or photo to appear as the new account's active data. Return to the original account; its newly scoped photo may reappear, while legacy archives stay aside.

## Earlier contacts/System Information gate (completed below)

The user explicitly approved all three choices. Their applied scope and verification follow.


## Confirmed contacts, Quick Actions and System Information completed

- Commit: `e9e5fd8` — `feat(profile): add confirmed contacts and system details`.
- Adopted Athena's two contact-card form and phone-contact import. Import only fills editable fields; cancelling/leaving before Save does not write the account or publish shortcut changes. A multi-number contact prompts for a number. Permission denial retains manual entry.
- Save computes only changed values among the four existing emergency-contact fields. No whole-user upsert is used by the contact screen. Existing personal/health/address/avatar/circle data is preserved by the field-specific update. The prior profile-batch contacts limitation is now resolved for this screen; the old helper remains preserved pending later Circle reconciliation.
- Relationship labels are account-scoped local data, associated with matching contact identities. No new backend columns or migration were added. Unchanged account contacts need no remote write; explicit Save can still repair local labels/shortcuts after reopening. Failed account writes do not publish draft contacts. A confirmed cloud save followed by local failure is reported separately, with local retry.
- Added only `READ_CONTACTS` to the native manifest. Permission is requested when Import is tapped. This declaration is required by the current [MAUI contacts API setup](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/contacts?view=net-maui-9.0); no address-book enumeration or upload was introduced.
- Quick Actions now use the signed-in account/backend scope rather than the old device-wide shortcut key. Legacy archives are kept aside with no automatic restoration, as previously chosen. Dev's layout, default shortcuts, icon handling and unrelated call/SOS/flashlight/siren/data behaviors remain unchanged.
- Confirmed contact saves queue an account-scoped sync. Dev defaults are populated before applying contact shortcuts on the first dashboard. Existing shortcut positions, disabled toggles and unrelated entries remain; matching manual contacts are reused, duplicate contact slots collapse to one shortcut, and same-name/different-number contacts remain distinct. Explicitly deleted contact shortcuts remain deleted until another Save requests synchronization.
- Stored synchronization revisions make repeat application idempotent. A failed marker write can be retried without duplicate shortcuts. Invalid saved shortcut JSON is preserved rather than replaced silently. Old-session callbacks cannot store or dial another account's saved custom contact.
- System Information displays the actual installed app version/build and Android package `LastUpdateTime`; missing metadata shows **Unavailable**. It reports current camera/location/notification states and actual motion-sensor availability without requesting permissions. It refreshes when the page appears or its window resumes after device settings. Opening settings failures have a visible status.
- **127/127 targeted local checks passed**: all 93 previous checks plus 34 contact-confirmation, shortcut-preservation/scope, retry and metadata-format cases. The nullable-value warning in the first Android build was corrected in one correction/rebuild. A later explicit-Save local-recovery refinement was covered by a focused retest and final build. Final Android build: **0 warnings, 0 errors**.
- ARCore declarations, `git diff --check`, approved source boundaries and source-preservation checks passed. The native manifest is identical to dev after removing the one newly approved contact permission. Dev dashboard defaults/icon methods and unrelated call/SOS/device/data methods remain equivalent; dashboard XAML is unchanged.
- An initial patch guard found two similar method guards and stopped before applying the dashboard/manifest portion. The patch was narrowed to the contact-action method and completed once. No unrelated method was changed by that correction.
- Preserved APK: `artifacts/athena-integration/05-contacts-system/RescuAR-Batch05-ContactsSystem.apk` (103,241,972 bytes; SHA-256 `987c3003c136e7948b5399bd9550d9cadb74edbbfff2faa8371fdf41bb5a4f7d`).
- No running app/browser/device, provider/database write, integration/E2E test, dependency installation, deployment, branch merge or push was performed. Permission/picker behavior, deployed profile RLS, real dialer actions and device metadata remain manual checks.

## Required manual contacts/System Information checks

Use a test account and the preserved batch-5 APK. Report actual results before behavior-dependent fixes:

1. Import a contact, cancel, leave/reopen, and deny contacts permission. Expect no account/shortcut write before Save and continued manual entry. For a contact with multiple numbers, expect a number choice. Save a contact; expect its call shortcut after returning to the dashboard.
2. Disable a contact shortcut, modify that contact and Save. Expect its position/toggle to remain. Add unrelated custom shortcuts and verify their order, routes and toggles remain. Test the same contact in both slots, two same-name/different-number contacts and clearing a slot; expect no duplicate or unrelated deletion.
3. Attempt a changed-contact save offline or with backend write access denied. Expect an unconfirmed-save error and no draft shortcut. Restore access and Save. If local sync fails after the cloud save, expect distinct status and local retry; an explicit Save after reopening can repair local sync without rewriting unchanged account fields. Reopen personal/health pages to confirm their fields remain intact.
4. Change accounts while import/save is waiting. Expect no old-account contact form, shortcut persistence or dialing through a stale custom-contact action. First use of a new account should retain dev's standard shortcuts while legacy archives stay aside.
5. Compare System Information with the installed app/package and device permission settings. Change camera/location/notification access in device settings and return. Expect the current status, a real package update date or Unavailable, and sensor availability rather than a false motion-permission claim.

## Newly observed dev changes

Dev advanced to `53d7cab` (`fix(mld): align guidance with locally observed road surfaces`). Its new road-surface bridge, native AR surface logic, visual road alignment, MLD/progress changes and regression checks are not yet imported into this integration branch. They overlap the later Camera/map work and require a preservation decision before that stage. The prior Admin RBAC/provisioning changes remain reserved for the Admin/final review. The pinned implementation baseline remains `1b3db7e`; no branch update was performed silently.

## Earlier gate (decisions received)

The user approved carrying over newer dev AR fixes now, adopting Athena Help Center with the existing Hotlines directory, and shared feature-accurate Terms/Privacy layouts. The separate runner overlap was also explicitly approved.


## Newer dev AR carry-over completed

- Commit: `9ac4450` — `fix(ar): preserve newer dev road alignment`; source: `53d7cab`. This was a selective source transfer with one parent commit, not a branch merge. Newer Admin changes remain for their later gate.
- Carried the road-surface bridge, semantic road-boundary extraction, local road fitting, native observation publication, heading calibration, shared display reference and MLD guidance safeguards. Fourteen files match the approved dev source; the only runner differences are the four accepted Athena parser fixes. Keep this dev reference when reconciling later Camera/2D map hunks.
- The user separately approved adding the `--road-surface` entry and all new checks while retaining every existing test. No accepted Athena splash/auth/profile/contact change was replaced.
- **28/28 road-surface checks passed.** **718/718 AR alignment/recovery checks passed**, including those same 28 cases and the existing relevant MLD/registration/recovery/sensor cases. These counts overlap and must not be added together.
- Android build passed with **0 warnings, 0 errors**. ARCore declaration consistency, approved source boundaries and `git diff --check` passed. Dependency pins, native-library guards, dashboard/layouts, profile/auth and Admin sources were not changed in this batch.
- An overly strict verification-script assumption expected `Invoke` on each changed parser-argument line; two calls span multiple lines. Correcting that comparison to the exact approved argument insertion completed the source check without changing application code.
- Preserved APK: `artifacts/athena-integration/06-dev-ar/RescuAR-Batch06-DevAR.apk` (103,262,452 bytes; SHA-256 `a17d3c26a0ac81078760d43cdd47041f3b9af6b99dd3b6a9b1991039149933e3`).
- The imported `AR_FIELD_VALIDATION_20261026.md` is source history from dev, not evidence of a new device test in this worktree. Native semantic capability, physical curb/road alignment, tracking/lifecycle behavior and field accuracy remain unverified here.
- The newer dev implementation withholds MLD route geometry until physical-road alignment qualifies. Devices without the required semantic capability expose an unavailable/pending state. This is imported dev behavior, not an independently validated compatibility guarantee.

## Required manual dev AR checks

Use the preserved AR APK and the existing dev field-validation procedure. Report actual results before behavior-dependent fixes:

1. On a supported device, start MLD guidance and observe a suitable road corridor while stationary. Expect guidance to remain pending until the physical reference qualifies; uncertain, cropped or ambiguous evidence must not create a path.
2. Rotate the phone and change presentation zoom without walking. Expect one retained road/display reference rather than a path dragged by camera rotation. Looking away should retain a qualified reference only while its live session/frame remains valid.
3. Interrupt tracking, replace the route, exit/re-enter the page and resume the app. Expect stale road proofs to be rejected. On unsupported hardware expect an explicit unavailable/pending state, rather than a false road alignment.

## Current next gate

Help Center/FAQ/Hotlines and shared Terms/Privacy presentation are already approved and may proceed. Further settings, circle, offline-display and Camera 2D overlaps still require the user's individual choices. Preserve the newly carried dev AR source when reaching Camera work, and retain newer Admin changes for the final Admin review. Commit every finished batch; no branch merge or push is authorized yet.
