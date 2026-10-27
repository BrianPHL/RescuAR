# Athena integration progress — 26 October 2026

## Active baseline

- Integration branch: `codex/integrate-athena-modules`.
- Initial dev baseline: `1b3db7e0512f95d554f91ccce36f7c8ad705ec6e` (includes the previously uncommitted MLD guidance/recovery work).
- Athena source: `fa777da153a492268ace76162a80389e0d904534`.
- Preserved dev source after approved refreshes: `004ed201b3f2e0b7922dc4cbb6c784bdddaa6ad9`.
- Source preparation and the final preservation audit are complete. See [the integration review](2026-10-26-integration-review.md) for the final keep/adapt/exclude matrix and outstanding manual checks.
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
| P3-07 | Help Center layout/FAQ/contact actions | Adopt Athena FAQ/layout with the existing Hotlines directory | Search/accordion retained; feature-accurate answers; existing registered directory route; no unconfirmed support email or automatic rescue call. |
| P3-08 | Registration/profile Terms and Privacy | Adopt Athena layouts with shared text matching implemented features | One section model/content catalog supplies both entry points; contradictory retention claims and unsupported guarantees removed. |
| S4-01 | Notification/haptic settings | Adopt functional Athena controls; retain feed and camera warnings | Optional in-app popup gates in five view models; advisory-only vibration; live updates remain unconditional. Device notification permission remains separate. |
| S4-02 | Siren setting versus dashboard shortcut | Control automatic alarms; retain the manual shortcut | Separate playback ownership and automatic preference gating; manual/SOS sound calls and shortcut visibility are unchanged. |
| S4-03 | Cache scope/offline-map switch | Clear only replaceable map copies; defer the offline-map switch | Dedicated cache root with path/link guards and partial-failure reporting; routing/account/photo/message/pending data and general caches excluded. |

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

## Earlier Help/Terms gate (completed below)

Both Help Center/Hotlines and shared Terms/Privacy choices had already been explicitly approved and have now been implemented.


## Help Center and shared Terms/Privacy completed

- Commit: `56fa572` — `feat(profile): adopt help and shared policy screens`.
- Adopted Athena Help Center presentation with searchable, expandable topics. Answers describe current AR/alignment limits, available offline data, remembered sessions, report submission, contact saves, local photo status and the difference between cached messages/locations and remote delivery. No immediate-response, guaranteed-safe-route or guaranteed-live/offline-circle claim was imported.
- The assistance action opens dev's existing registered `Prepare/HotlineDirectory` route. No support email address or automatic rescue-call action was imported, matching the user's choice. A navigation failure or empty search has a visible message.
- Adopted Athena Terms/Privacy page layouts. Both registration and profile read the same `AppInformationContent` sections through one `InformationSection` model. The registration pages keep their existing authentication back-navigation handlers; the profile pages retain their Shell navigation.
- Shared text describes submitted resident/profile/health/contact data, secure saved-session tokens, separate local profile/message/media caches, account-scoped contact relationships, import-then-Save behavior, cloud location/message storage, retained local data after sign-out and legacy archives kept aside. It adds no fixed retention period, automatic deletion or guaranteed delivery promise.
- FAQ and numbered cards use a bindable vertical list within the page's existing scroll container rather than a nested virtualized scrolling list. This retains Athena's card appearance while keeping all content in one scroll area.
- Android build passed with **0 warnings, 0 errors** on the first attempt. Source/XML checks confirm the shared catalog, registered Hotlines route, preserved back handlers, approved file boundaries and unchanged accepted dev AR source. `git diff --check` passed.
- No new unit tests were added for static content/layout changes. The preceding AR checkpoint's 718-case subset remains its recorded unit evidence; it was not rerun for unrelated text changes. No app/browser/device, backend/provider, integration/E2E or policy-acceptance validation was performed.
- Preserved APK: `artifacts/athena-integration/07-help-terms/RescuAR-Batch07-HelpTerms.apk` (103,274,740 bytes; SHA-256 `c9aff52cb660bc8b48d28a86d690075a8d397cc506f7aa7143f9449254584a18`). This includes the accepted newer dev AR checkpoint as well as previous authentication/profile/contact changes.
- The content reflects the inspected implementation, not a live-backend audit. Reconcile the shared catalog when later accepted Circle, settings or offline-map changes alter those descriptions.

## Required manual Help/Terms checks

Use the preserved batch-7 APK and report actual results before behavior-dependent fixes:

1. Search Help for an offline/account/navigation topic, expand/collapse answers and clear the search. Expect the matching topics and an honest empty-search result. Open Hotlines; expect the existing directory, with no automatic call or email action.
2. Open Terms and Privacy from registration and from Profile. Expect the same six sections and bullets in each corresponding document, with readable scrolling and the appropriate Back action returning to where it was opened.
3. Review the revised wording against your intended published app behavior. Stored last-known positions and retained local caches must not be described as never stored or automatically deleted by sign-out.

## Earlier settings gate (completed below)

The user explicitly approved all three settings choices. Their implementation and validation follow.


## Settings and optional advisory controls completed

- Commit: `1f0cc6a` — `feat(settings): wire advisory controls and map-only cleanup`.
- Adopted Athena's settings layout with three functional controls: optional in-app advisory popups, automatic advisory sound and advisory vibration. Renamed the misleading push-notification label; OS notification access remains a separate device permission and uses the existing truthful metadata view model.
- A shared device settings store preserves current values and migrates the old popup key only when the current key is absent. Confirmed writes notify consumers; failed writes do not emit a successful change. Settings refresh after returning from device settings.
- Dashboard, Profile, general Map, Reports and Advisory Feed now gate only their optional incoming-advisory dialogs. Turning popups off closes existing optional dialogs through weak observers. Feed/summary refreshes, dashboard data updates, manual advisory opening and the Camera's event subscription/source remain preserved.
- Automatic sound is separate from the existing public manual-play API. Muting it stops an active automatic alarm, leaves a manually started siren running, and does not hide or change the dashboard shortcut. Playback generations prevent an old completion callback from clearing a newer manual playback state. This is not a claim of device volume override.
- Added only the normal `VIBRATE` declaration from Athena for the approved optional feedback. The helper checks the setting and tolerates unavailable hardware. Navigation voice and turn-feedback behavior were not changed in this batch.
- Map cleanup can inspect/remove files only under `FileSystem.CacheDirectory/rescuar-map-cache`. It checks each file and ancestor, skips links/reparse points and never recursively deletes/moves directories. It excludes general cache, packaged/core routing data, saved evacuation data, profile/identity archives, photos, messages and pending files. Locked/failed files remain counted rather than producing a false zero-cache success.
- The detailed-offline-map switch is deferred as requested. Until the map module writes replaceable copies into this reserved area, 0 KB and a disabled Clear button are expected; this is not the size of every map/media file on the device. No existing general cache was swept or reclassified.
- **154/154 targeted local checks passed**, including all prior 127 cases and 27 settings/audio/cache cases. They use fake preferences/audio plus isolated filesystem fixtures under ignored worktree artifacts. In-use-file deletion and retry were exercised on Windows. No real app cache or media was deleted by these checks.
- Android build passed on the first attempt with **0 warnings, 0 errors**. ARCore declarations, XML/binding/source review and `git diff --check` passed. The source check reconstructs the five original view models after removing only the approved popup gates/weak observers; blank-line/indentation normalization was needed in that comparison. No application correction was needed for that verification-script adjustment.
- Accepted newer dev AR source, Camera emergency handling, dashboard action logic/shortcut persistence, advisory data delivery and unrelated report/map/profile behavior are preserved. No full AR retest was repeated for these source-preserved paths; their preceding 718-case checkpoint remains recorded above.
- Preserved APK: `artifacts/athena-integration/08-settings/RescuAR-Batch08-Settings.apk` (103,291,124 bytes; SHA-256 `856a490e1a8e30b7a8879f1ed2f5f78f3abed8b7e69bc55e36b7f9e660a40f50`).
- No running app/browser/device, backend write, integration/E2E test, dependency installation, deployment, branch merge or push was performed. Real sound/vibration, permission/lifecycle behavior and live advisory presentation remain device checks.

## Required manual settings checks

Use the preserved settings APK and report actual results before behavior-dependent fixes:

1. Toggle each advisory control, fully restart and return to Settings. Expect the stored choices. Use controlled test advisories to check that optional dialogs follow their switch while the feed and camera still receive updates; manual advisory details should remain accessible.
2. Start the manual Loud Siren, then turn automatic advisory sound off. Expect manual playback/shortcut availability to remain. With automatic sound enabled, verify a test advisory's sound; turn it off while automatic sound is playing and expect that automatic playback to stop.
3. Check optional vibration on supported hardware, then disable it and repeat the controlled test. Device audio/notification permissions remain independent. Return from OS settings and expect current permission text rather than fabricated Granted values.
4. Cache size currently may be 0 KB with clearing disabled. Once the map module is connected, verify that clearing removes only its replaceable copies, retains account/media/messages/pending/routing data, and reports any files that remain in use.

## Earlier Circle membership gate (completed below)

The user explicitly approved confirmed membership controls, account/server-scoped cached lists, and separate member Leave versus creator Rename/Delete.


## Circle membership and management completed

- Commit: `1550d13` — `feat(circle): add confirmed account-scoped membership`.
- Adopted Athena's header, selected-Circle/invite card and Create/Join cards. The adapted list has Select, creator-only Rename/Delete and member-only Leave controls. Delete explicitly confirms removal for everyone; Leave describes removing only the current membership. Offline cards carry a persistent cached/unverified label even after copying an invite or selecting a saved Circle.
- Replaced the settings page's mock/shared Profile view model with a dedicated management consumer. Its service captures the verified signed-in user, full normalized backend URL and session revision; checks again around waits/remote results, and ignores page/account changes. Delayed rename prompts and clipboard results cannot update a reopened view. Draft names/codes are cleared on page exit/account change.
- Create and Join require actual cloud membership confirmation before appearing successful. No device/email-derived identity, global selected/joined-circle preference, fake local membership or legacy archive restoration was imported. An interrupted creator-membership insert is recovered by a later online Refresh; an unknown mutation result disables further changes until Refresh rather than retrying creation automatically.
- Rename rechecks current creator status, uses a creator-filtered field update and reads back the matching name. Member Leave deletes only that account's membership and checks its absence. Creator Delete deletes only the creator-filtered Circle row and checks absence; it never loops through other members/messages with client-side deletes. Existing backend row permissions and foreign-key rules remain authoritative and unverified.
- Membership metadata and selected ID use a separate atomic JSON store under `AppDataDirectory/circle-memberships-v1`, keyed by the full backend URL plus account ID and validated against its owner envelope. Previously confirmed lists may be viewed/selected offline with a label. Online refresh removes revoked entries and stale selection. Auth/access failures remain failures; network loss may display the saved list. Corrupt files are reported and preserved.
- A confirmed cloud change with failed local storage is reported separately, without claiming offline persistence. The cache contains no messages, photos or pending uploads, and membership removal never deletes those files. This does not validate access controls on the existing legacy chat cache, which remains for its later consumer gate.
- **227/227 targeted local checks passed**, including all prior 154 checks and 73 new membership/role/revocation/failure/account-race/cache cases. Filesystem fixtures are isolated under ignored worktree artifacts. Fake remote responses exercise policy; they are not live Supabase/RLS/cascade evidence.
- Android build passed on the first attempt with **0 warnings, 0 errors**. ARCore declarations, XML/binding compilation, exact nine-file source boundary and `git diff --check` passed. All 14 accepted newer-dev AR source hashes and the shared regression runner remain unchanged; no redundant AR unit run was performed for those unchanged paths.
- Accepted auth/profile/contact/settings behavior, the legacy Cloud Circle service, Circle map/chat consumers, models, DI, shell entries, native declarations and Admin source were not changed in this batch. The selected ID saved here is **not yet consumed by the legacy Circle map/chat**; that connection remains an explicit next stage. Group photos and member rosters have not been imported into this management screen yet.
- Preserved APK: `artifacts/athena-integration/09-circle-membership/RescuAR-Batch09-CircleMembership.apk` (103,332,084 bytes; SHA-256 `91ecdd140e1dba0f506000b0cff786f4fe572777bf1eafbbce52288e0391f37f`).
- No running app/browser/device, backend write, database/schema change, integration/E2E test, dependency installation, deployment, branch merge or push was performed. Live create/join/rename/leave/delete, RLS/foreign-key behavior and device lifecycle remain unverified.

## Required manual Circle membership checks

Use the preserved batch-9 APK and report actual results before behavior-dependent fixes:

1. With two test accounts, create a Circle and join by code. Expect the exact confirmed Circle on each account's management screen. Test a rejected code and interrupted connection; neither should report local membership success. After an uncertain create, Refresh before retrying.
2. Confirm creator Rename/Delete and member Leave controls. Expect the member to have no creator controls, Leave to preserve the other account's Circle, and Delete to warn that it affects everyone. If existing backend references/permissions reject an operation, expect a visible failure and retained last-confirmed list rather than a false local removal.
3. Select a confirmed Circle, restart offline and reopen management. Expect that account/server's labelled saved list. Reconnect after membership revocation and Refresh; expect the revoked Circle/selection to disappear. Another account must start with its own confirmed or empty list; legacy archives must remain aside.
4. Exit during a slow request or rename prompt, then change account and reopen. Expect old results/drafts to be ignored. Treat map/chat selection consistency, member positions and photos as pending until their consumer stages are completed.

## Earlier Circle map gate (completed below)

The user explicitly approved the Circle Map tab with dev General/Offline Map access, Athena people controls with truthful readings, and default-Off visible-screen sharing.


## Circle map, people and explicit sharing completed

- Commit: `a6cd5d7` — `feat(circle): add Athena map and explicit location sharing`.
- The main Map tab now opens Athena's Circle map presentation. A visible **General / Offline Map** action opens dev's existing map, with a shell Back action/title. Its former Circle shortcut returns to the root Map tab instead of stacking duplicate Circle pages. Dev's general map content, roads/points and advisory behavior remain unchanged.
- Circle selection now uses the approved account/full-server membership store across management and Map. Entry/manual Refresh/selection recheck the list, and periodic full refresh runs every 30 seconds. Presence reads verify the selected membership; uploads recheck the signed-in member immediately before dispatch. Create/Join/Manage opens the completed confirmation-based management screen rather than introducing a second fallback path.
- Adopted Athena's people panel, avatar/name pins, tap-to-center, zoom and phone-app controls. Phone numbers and lightweight names/avatar details come from the confirmed roster; unavailable profiles show a neutral missing-details label, not fabricated family identities. Calls open the device dialer only after a user action.
- Missing/invalid/undated positions receive no pin. Existing coordinates are not displaced outward by Athena's tens-of-metres avatar fan-out: overlapping members remain available in the people list. The preset Marikina coordinate is a browsing center only. No member is labelled Safe/Online from a default or from a battery value.
- Cards show the recorded time and recent/last-known/cached labels; readings older than two minutes or cached readings use gray pins. Missing/invalid battery telemetry remains Unknown, including a genuine 0% reading being preserved. Dates without a timezone are interpreted as UTC. Optional street labels say **last shared near ... (approx.)** and fall back to recorded coordinates; a failed lookup never invents a nearby street.
- Optional HTTPS avatar downloads and approximate street lookups are bounded and guarded by page/account scope. Their page-memory caches clear on exit/account change. No group-photo placeholder or shared device photo preference was imported, and existing profile photo handling remains unchanged.
- The explicit sharing switch starts Off on every entry/resume and resets on Circle/account changes. Viewing member readings does not request this device's GPS or location permission. Turning sharing On requests permission; only nonmock finite readings at most 30 seconds old with known accuracy at most 100 metres may upload. No old/default coordinate substitutes for a missing fix. Actual acquisition time is saved rather than relabelling an old fix with the upload time.
- Sharing uses a separate cancellation scope. Off, page exit and app stop cancel pending acquisition/uploads; stale callbacks cannot start another upload or publish another account's data. Polls cannot overlap, and permission/upload failures show a paused/off state rather than a claimed confirmation. An already accepted network request cannot be retracted after leaving the page.
- The existing **per-user `user_locations` row** is retained. Circles authorized by the current backend can read that last-known row; this batch does not introduce a separate per-circle location-sharing schema. Off stops new uploads and does not delete stored positions. No backend migration or live write was performed here.
- Offline viewing uses the labelled confirmed membership list and any people already fetched in that same page/account/circle scope. A cold offline start has no invented roster. The Circle background still needs online map coverage until the shared offline-display stage; the dev General/Offline Map remains available. Chat still uses its previous consumer; its delivery, media and private cache reconciliation remain pending. Chat entry requires a fresh membership confirmation during this transition.
- **273/273 targeted local checks passed**: all prior 227 cases plus 46 presence/sharing/lifecycle/account-race cases. These use fake services, not device permissions, actual GPS, live Supabase or hosted RLS.
- Initial build and the final source-review build passed. The final build has **0 warnings, 0 errors**. The source-review changes correct the unavailable-roster message, preserve the cloud-success/local-cache-failure warning, and retain Athena's approximate street labels only for valid dated positions. No failed-build repair cycle was needed.
- ARCore declarations, XML/binding compilation, exact 13-file boundary, General Map restoration comparison and `git diff --check` passed. All 14 accepted dev AR source hashes and the shared runner remain unchanged; auth/profile/contact/settings/chat/Admin sources remain preserved. No redundant AR unit run was performed for unchanged source.
- Preserved APK: `artifacts/athena-integration/10-circle-map/RescuAR-Batch10-CircleMap.apk` (103,352,564 bytes; SHA-256 `3a271b8419fb064b30ae2c2bb23a63de90cda39fdf9148612bcd814d44a3853d`).
- No running app/browser/device, backend/provider validation, database change, integration/E2E test, dependency installation, deployment, branch merge or push was performed.

## Required manual Circle map checks

Use the preserved batch-10 APK and report actual results before behavior-dependent fixes:

1. Open Map with sharing Off. Expect no location-permission prompt or new device-position upload. Confirm selection matches management, then open General/Offline Map and return. Expect dev's roads/points and Circle access to remain available; Camera guidance is unchanged.
2. With two controlled accounts, compare known positions, old readings, missing coordinates/battery, avatar updates, street lookup failure and overlapping pins. Expect dated last-shared values, Unknown where data is absent, no default person pin or invented Safe/Online status, and the phone action to open the dialer.
3. Enable sharing and test granted/denied permission, an accurate fresh fix, unavailable/stale GPS and network failure. Expect uploads only for qualifying readings, preserving acquisition time. Turn it Off, leave the tab, background the app, switch Circle and reopen; expect sharing Off and no subsequent uploads from old callbacks. Previously stored cloud positions remain last known.
4. Revoke membership while the page is open, disconnect/reconnect and switch accounts during a slow read. Expect removed access to clear the people/selection, offline data to be labelled, and no old account/circle data to appear as current. A cold offline Circle basemap/roster may be unavailable until their later stages; General/Offline Map remains the offline entry.

## Earlier Circle chat/photo/roster gate (completed below)

The user approved confirmed delivery states/retry, attachment preview before Send, device-local Circle photos, reviewed quick messages, truthful management roster and old chat files kept aside.


## Circle chat, local photos and management roster completed

- Commit: `c4d1d57` — `feat(circle): connect chat delivery and local media drafts`.
- Adopted Athena's chat header, Circle picker, message bubbles, media controls and quick-message bar. Quick chips now fill the composer; only Send/Return submits the reviewed text. Pending/Failed/Sent labels and Retry are connected to the actual screen, not an unused helper. Sent means server acceptance, not another member reading it or a rescue response.
- The new pipeline captures verified account/full-server/Circle scope, checks membership before private cache reads and submission, and persists a single client UUID before network delivery. The insert row model explicitly includes that UUID; the legacy read model's auto-generated-ID configuration is unchanged for other consumers. Confirmation lookups reconcile lost responses and explicit retries without overwriting confirmed content.
- No detached background send or local-success fallback is used by chat. Online refresh merges confirmed IDs with own unconfirmed messages; reconnect/loading never sends pending items automatically. Rejected membership/session/server responses cannot publish another account's data or quietly become cached success.
- Attachment selection/capture copies the file into owned app data and shows a preview with Send/Cancel. Images display inline; full photo/video previews use the device viewer when requested. Nothing uploads during selection, preview, quick-message tapping or local group-photo changes. Photos are limited to 10 MB and videos to 50 MB; empty/oversized imports fail with a visible explanation.
- Explicit media Send streams multipart binary data to Cloudinary's image/video endpoint with the existing cloud/preset constants. A matching resource type and secure URL must be returned before message submission; that URL is saved so a later message retry can reuse the upload. The [official upload reference](https://cloudinary.com/documentation/image_upload_api_reference) was checked; existing provider configuration was not changed or exercised live. Reports/profile consumers keep their original Cloudinary helper.
- Failed sends retain their original text/ID and any local media plus confirmed URL for explicit Retry. A pending-save failure prevents dispatch. Confirmed cloud acceptance with failed local persistence is reported separately and can be reconciled by ID later. A lost provider response or failed URL save can still leave an orphan/duplicate uploaded asset on a later explicit Retry; this is not a claim of provider-level idempotency or remote cleanup.
- New state/media/photo files live only under `AppDataDirectory/circle-chat-v1`, hashed by full server/account/Circle with checked owner envelopes. Reads/writes/imports reject mismatched paths and linked account files/folders; state replacement is atomic. Local paths/delivery bookkeeping never become database columns. Cancel clears the selected preview; unused owned local copies are retained, with no broad app-data/media sweep.
- The user explicitly extended the archive policy to legacy chat: old circle-ID-only files remain untouched and are not imported into active history. Confirmed cloud history and the new scoped cache form the active conversation. No old local-only message is relabelled Sent. Other account archives, messages, pending files and core routing data are not deleted.
- Circle photos are explicitly **on this device, for this account and Circle** in both chat and management. They share the same checked local pointer. Other members do not receive the photo change; no Cloudinary upload, shared-photo column or database migration is introduced for this choice.
- Management now reads the same verified roster/presence remote and dated/unknown projection as Map. It shows recorded positions and battery readings, with unavailable details labelled, rather than declaring every member Online. The only change in the completed Map view model is making its existing card mapper available to management; location-sharing policy remains unchanged.
- Chat polling and requests stop/cancel on page exit/background. Native chooser/viewer suspension preserves the intended draft scope, while account/Circle/page changes reject stale results. The Circle picker uses the shared selection store. Composer text is retained by that page's account/session/Circle while switching; queued messages and selected attachment files persist across a new app session, while unsent typed-only text is not claimed to persist across restart.
- **324/324 targeted local checks passed**, including all prior 273 and 51 new chat cases. They cover response loss, immutable IDs, retries, offline pending state, upload-before-send, retained failures, stale preview/account callbacks, revoked membership, local-vs-cloud persistence failure, file scope and corrupt-state preservation. The actual installed Postgrest insert serializer confirms the client ID is included and local fields are excluded. No real backend/provider call was made.
- Initial and final source-review Android builds passed; the final build has **0 warnings, 0 errors**. Final source review wrapped long delivery/cache messages and guarded late device-viewer feedback. ARCore declarations, compiled bindings/XML, exact 14-file boundary and `git diff --check` passed. All accepted newer-dev AR hashes and the shared runner remain unchanged; no redundant AR unit run was performed.
- Preserved APK: `artifacts/athena-integration/11-circle-chat/RescuAR-Batch11-CircleChat.apk` (103,393,524 bytes; SHA-256 `01838196ac9c2373e54f2b9891ac582e060c7fbe8ddb4ab843748d88c7d71004`).
- No running app/browser/device, live Supabase/Cloudinary validation, database/schema change, integration/E2E test, dependency installation, deployment, branch merge or push was performed. Real SDK/RLS acceptance of client UUIDs, unsigned preset/video support, OS picker/viewer permissions, layout and lifecycle remain unverified.

## Required manual Circle chat/photo checks

Use the preserved batch-11 APK and report actual results before behavior-dependent fixes:

1. With two controlled accounts, send text and compare the one message ID/row and Pending-to-Sent transition. Interrupt a response, Refresh and Retry; expect one confirmed message, retained failed text, and no automatic resend on reconnect. Confirm Sent never implies the recipient has read it.
2. Select/capture a photo or video. Expect a local preview and no upload until Send. Cancel or interrupt delivery; expect no claimed success, and failed queued attachments available for explicit Retry. After a confirmed upload but failed message submission, Retry should reuse the upload URL. Test real video support with the configured preset and device viewer.
3. Switch account/Circle, exit during a slow send/picker, background/resume and restart. Expect old callbacks not to publish into the new scope, polling to stop, Pending items to remain scoped, and no automatic delivery on reopening. Previously unscoped local history must remain aside; cloud-confirmed history should load.
4. Choose a Circle photo in management and reopen chat for the same account/Circle. Expect the same explicitly local photo, no photo upload/change for other members, and another account/Circle to have its own photo. Compare management roster times/unknown readings with Map; denied/revoked access must not be shown as Online/Safe.

## Earlier next gate (newer-dev interruption completed below)

Shared offline display remains next. Before continuing, the user approved the observed newer-dev 004ed20 carry-over and the report-submission overlaps found during its review.


## Approved dev 004ed20 carry-over completed

- Source pinned to `004ed201b3f2e0b7922dc4cbb6c784bdddaa6ad9` after the user approved the newer navigation/report/assessment changes and the two shared runner/advisory overlaps. Athena source remains pinned to `fa777da`. Admin/provisioning remains for its existing separate gate. No branch merge, rebase, push or original-checkout edit was performed.
- Focused code commits: `5a1d21a` — `fix(navigation): preserve newer dev recovery and progress`; `060080e` — `fix(assessment): preserve dev home navigation fallback`; `a491acd` — `fix(reports): confirm authenticated report submissions`. Every carry-over commit has one parent.
- The AR/navigation group carries all 28 changed source/check/historical-document paths. It retains stationary/progress holds, tracked-motion display windows, recovery after destination/tracking changes, geographic/text guidance independent of ribbon visibility, semantic-image reprojection, approximate-vs-physical alignment states and the narrower solid route ribbon. It does not claim that the physical start offset or unsupported-device road alignment is now field-correct.
- The runner equals dev `004ed20` plus exactly the four approved `RouteProvenance.Unreported` parser arguments. Every old check remains, with the new pipeline/ribbon/continuity/registration entries added. The active preserved AR reference now covers **33 source/check/history paths** at the approved dev revision; later Camera/2D work must retain these updated behaviors.
- **864/864 targeted AR/navigation alignment-recovery checks passed**, including the new pipeline/ribbon/continuity/registration/semantic cases. This supersedes the earlier 718-case checkpoint for this source revision; those counts overlap and must not be added.
- Newer dev's report source includes approved-only filtering for remote hazard/public feeds, a fetch timeout, safe collection dispatch, caught comment-enrichment faults and model status/alias properties. These remain dev's source. In the customized advisory service, the only change is observing a fetch task that faults after its timeout; the accepted settings and separate manual/automatic siren code are preserved exactly.
- The assessment net change is only Back/Close navigation: try the parent route and fall back to Home if it fails. Its questions/scoring are unchanged. No new test was added for this reversible route fallback; it is covered by source comparison/Android compilation and needs user navigation confirmation.
- Review found the new report REST fallback used the app key as its bearer identity, substituted preset coordinates and returned to an unconditional Success path after unconfirmed failure. The user separately approved adapting all three overlaps: verified-account/stable-ID retry, a real chosen point, and confirmation-only success with failed form/photo retention.
- Report submission now captures full server/account/session scope. SDK and REST retain one report UUID; REST uses the current resident access token for Authorization and the configured app key only for `apikey`. It looks up an existing ID before insert/retry and validates matching contents before Success. The existing report model already inserts its client ID; no replacement schema or new database column was needed.
- Public feed/status UI is distinct from submission: accepted reports begin Pending, and the modal says **submitted for review**, with administrator approval needed for public visibility. Failed/unconfirmed submission stays in the form with selected photo/details and the same ID for explicit retry. An already accepted ID cannot be silently overwritten by editing the failed draft. No success is inferred merely from a completed SDK call without a matching receipt.
- Incident submission starts without coordinates. A GPS selection must be recent, nonmock and have known accuracy at most 100m; an explicit valid map/search result also qualifies. Selected incident points remain fixed while details are typed. Submission snapshots the chosen point and rejects late GPS replacement. Preset/typed address text opens/searches for a real result instead of silently assigning Marikina. General browsing centers and dev's unrelated vicinity display behavior are unchanged.
- Report photo upload is scoped/cancellable and bounded to 10 MB. A confirmed secure URL is reused for the same active account/session and exact selection after a failed report submit. Photo/provider failure does not hide the form or submit an empty attachment as a success. Other Cloudinary consumers/configuration are preserved. The draft is active-view-model state, not a promise of restoration after restart or after explicitly starting a new report.
- **368/368 local policy checks passed**, including the prior 324 plus **44 new report cases**. They use fake transports/media callbacks and actual installed SDK model serialization/header construction without network requests. They cover missing/preset/invalid point rejection, stable-ID reconciliation, no anonymous fallback, incorrect receipts, failed media, upload reuse and account/cancellation changes.
- The first report check compilation exposed the UI comment model's toolkit dependency in the console harness. One repair supplied a test-only empty-comments fixture, leaving production comment code/packages unchanged; the one rerun passed. These tests exercise an empty initial comment list, not comment UI behavior.
- The first combined Android build succeeded with three warnings. One correction guarded nullable session-token access and restored the legacy custom-address control as a search that requires a mapped result. The single rebuild passed with **0 warnings, 0 errors**. XML/binding compilation, ARCore declarations and `git diff --check` passed.
- Source comparisons prove the native/core/check transfer and assessment match the approved dev source, the runner has only the accepted parser deviation, and the advisory adds only its approved observer. Dev report feed/like/comment code is preserved outside its approved submission region. All completed Athena splash/auth/profile/settings/Circle/map/chat modules, routing data, project dependency/native pins and Admin source remain unchanged.
- Four imported field-diagnosis documents are **historical dev evidence**, not a new device test in this worktree. No running app/browser, hosted Supabase/Cloudinary/RLS check, database/schema change, integration/E2E test or dependency installation was performed.
- The last Android build had already completed when the user said **“No need to build the APK yet.”** No further APK packaging or new batch APK copy was performed after that instruction. Continue source/targeted local checks and focused commits; use compile-only checks when needed until the user requests packaging. No new APK is delivered for this checkpoint.

## Required manual checks for this carry-over

Report actual results before behavior-dependent fixes; APK packaging is deferred until requested:

1. In a later requested device build, start/change/restart a destination, interrupt/resume tracking, stand still with noisy GPS and walk short increments. Expect held progress not to drag the visible route, retained preview only in its qualified frame/age window, and text/geographic guidance to remain available when geometry is held. Check physical road placement separately; these unit/build results do not certify it.
2. Submit a controlled report using a GPS point or chosen map/search result. Missing/typed-only locations must require selection. With a failed photo/server request, expect the form/media retained and no Success; retry should reuse the report ID/confirmed upload. After confirmed acceptance, expect review status and no public feed appearance until approval.
3. Switch account, leave the page or cancel during slow photo/location/report operations. Expect no stale success or point replacement in another draft/account. Existing backend RLS must permit verified insertion and confirmation; report actual denials rather than changing policies automatically.
4. Check assessment Back/Close from both a nested route and a missing-parent path. Expect Back when available and Home otherwise. Check optional advisories/manual siren remain as previously chosen; the timeout observer must not change their controls.

## Shared offline display and setting completed

- Code/assets/checks commit: `691d44b feat(map): share detailed offline display layers`.
- The user approved sharing Athena's converted tiles and static buildings/parks/water across General Map, Circle Map and the later Camera 2D consumer, while preserving every dev route/data/marker overlay. They approved Detailed Offline Map defaulting On, Off retaining basic roads/points, scoped cache clearing, and including only the converted archive/context rather than the two conversion inputs.
- General Map and Circle Map now consume the shared optional display owner. Camera remains unchanged pending its own gate. Detail is inserted above each online base and below existing roads/points/routes/people; deactivation, setting changes and clearing remove only that owner's layers. General Map's embedded road/point loops, projection loader and advisory code remain unchanged. Circle's basic roads/points use the same dev embedded resources; its account, membership, sharing and people-pin logic remains unchanged.
- Imported the exact Athena context payloads: **1,058 buildings, 219 parks and 465 water features**. Checked feature counts, geometry coordinates and source equality. These are static map context, not current hazard, road access, shelter availability or safety evidence.
- The converted ZIP is **93,661,024 bytes**, SHA-256 `1168d7528af49cf0a6d3d6befdbbdfa1a8e3bc2f350ea6ce943928ed07e62b12`, with **3,555 PNG tiles at zoom 13–18**. Bounds are longitude 121.067945526206–121.135837600791 and latitude 14.6085349357737–14.6748035828093: only part of Marikina. The visible status checks the tile at the map center, not the whole viewport; unavailable coverage and unsupported zoom remain explicit. Existing online bases can still render when connected.
- The ZIP follows the repository's existing Git LFS rule. The committed pointer matches the exact payload checksum and size; the local payload is available in this worktree/LFS storage. No LFS upload or push occurred. Neither unused MBTiles input was imported. Evaluated MAUI raw map assets are only this ZIP plus the three context files; existing conversion-input exclusions also cover `MAP_V2.mbtiles`. Required native bridge errors, dependency pins and AR compatibility checks are preserved.
- The default-On setting is now functional and rolls back a failed preference save. Off leaves basic map data and all other module overlays intact. Navigation voice remains deferred. Clear Map Cache invalidates visible layer requests, closes the shared ZIP reader, then removes only replaceable copies under `CacheDirectory/rescuar-map-cache`; no account/photo/message/pending/routing sweep occurs. A cancelled request cannot recreate cleared copies; the next enabled map activation can recreate them from packaged data without a network.
- **412/412 local policy checks passed**, including the prior 368 plus **44 offline-map/settings cases**. All **3,555 actual PNG tile entries** passed the production reader. Cases cover exact package integrity, missing/out-of-range tiles, actual coverage holes, unsupported/corrupt format, bounded installation, valid-copy reuse, corrupt replacement, failed checksum, temporary cleanup, open-reader clearing, protected sibling data, cancellation, owner changes, Off/hidden-page/clear races, partial attachment rollback and independent settings. No new dependencies were installed.
- The first compile-only check found the MAUI/Mapsui `Map` name collision. One explicit alias correction and one retry passed with **0 warnings, 0 errors**. The inspected MSBuild **Compile** target includes C#/XAML compilation and omits application APK packaging/signing. Existing APK sizes and modification times are unchanged; no new APK or checkpoint copy was created.
- Source checks passed for the exact **23-path** code/asset/test boundary and all **33 pinned dev AR/navigation/check/history hashes**. The shared AR runner and accepted splash/auth/profile/contact/report/advisory/chat/Admin sources are unchanged. ARCore static declarations, modified XAML parsing and staged whitespace checks passed. No redundant AR test run was performed for unchanged source.
- These are local unit/source/compilation results. No running application/browser/device, hosted backend/provider, integration/E2E test, schema change, branch merge, push or deployment was performed.

## Required manual offline-map checks

Use a later requested device build and report actual results before behavior-dependent fixes; APK packaging remains deferred:

1. Open General Map and Circle Map without a network at a covered Marikina center/zoom. Expect packaged tiles/static context plus dev roads/points, with existing routes/people retained and Circle sharing still Off by default. Switch between pages and resume the app; expect no duplicate detail layers or new position upload from merely viewing a map.
2. Turn Detailed Offline Map Off and On. Expect basic roads/points and existing markers/routes to remain, optional detail to follow the setting, and navigation-voice/advisory/siren preferences to remain independent.
3. Pan outside the stated bounds and zoom outside 13–18. Expect an explicit center-coverage notice rather than a blanket Offline Ready or safety claim. Confirm map control and actual pin placement separately on device.
4. Clear Map Cache after a map was open, then return with detail enabled. Expect replaceable copies removed and recreated on that next use; account data, profile/group photos, chat history, pending attachments and routing data must remain intact. Rapid page/setting changes during preparation must not attach stale detail after Off or exit.

## Camera 2D completed

- Code/checks commit: `505db02 feat(camera): add Athena 2D map with shared navigation`.
- The user approved six overlapping choices: Athena's Camera interface/shared offline layers with dev behavior; a single dev navigation flow; a current validated GPS fix for the user pin and nearby distances; one initial frame per new route with explicit Recenter/Overview; existing directory data and truthful estimates/unknowns; and dev turn/arrival rules in both views. Voice/vibration remain at their separate gate.
- Adopted Athena's AR/2D/Flood tabs, real Mapsui map, shelter search/planner, route summary and directions-sheet layout. All **76 existing named controls** remain; original native view, flood compositor/configuration, AR/status, emergency and arrival subtrees are preserved. The map action dock uses two columns to fit narrow screens. Two Athena navigation-icon names do not exist in either branch's image files; they now reference existing teal/white app arrows.
- New map code lives in `CameraPage.Map2D.cs` with pure presentation policies. The original dev Camera body is unchanged apart from **four display/lifecycle hook sites**: setup, periodic display refresh, window observation and display cancellation on exit. Reversing those hooks restores the exact pinned dev body. All **32 other pinned AR/navigation/check/history files** remain unchanged. The baseline retains both Camera's original dev object ID and its accepted integrated ID/reversal hunks for the final audit.
- Start publishes the selected center through the existing launcher. Stop/Cancel uses the existing destination-clear flow; Retry calls the existing route-request entry. The map never computes/assigns a parallel route, writes routing GPS/progress, resets AR/MLD state, removes hazard rules or substitutes an empty hazard list. It renders the exact accepted route only for its matching destination version, and keeps its own route/directory/user layers beneath the existing display ownership rules.
- During a trip, the user pin and nearby distances use dev's current canonical GPS quality/age policy and respect held position/route identity. Without a destination, separately cancellable browsing acquisition uses the same location service/quality rule and never writes routing state. Invalid, stale, future, inaccurate or unknown-accuracy fixes produce no current user pin or nearby distance. The preset Marikina point is only a browsing center. A route-start marker is labelled **Route start**, distinct from the current GPS pin. Page/mode exit and app stop cancel display/location work and invalidate removed user-pin data.
- The existing shelter directory currently contains names/coordinates with no live status. Search shows **current availability unknown**, labels straight-line distances as estimates, and falls back to name ordering when location is unknown. Route walking times are explicitly estimates, with pace/conditions varying. Static map context, directory membership and a computed route do not claim current safety or road/shelter availability.
- A newly accepted route is framed once by destination version/geometry identity. Routine GPS/UI refreshes update layers without changing pan or zoom. Recenter requires a current qualifying fix; Overview explicitly frames the accepted route. Changing a selection does not start a trip until Start is pressed. Waiting/failed requests expose Retry/Cancel rather than a claimed route.
- The 2D banner evaluates dev's turn classifier at accepted tracker progress; uncertain/off-route/low-confidence/held/replacing states withhold current turn distances. The directions overview uses Athena's pure `RouteDirectionsService`, which already delegates to dev's classifier, and describes the route from its start. Destination ahead in an overview does not confirm arrival; dev's existing confirmation/prompt/overlay rules remain untouched. Emergency/arrival overlays retain priority. No direct text-to-speech or new vibration consumer was imported in this batch.
- **41/41 targeted Camera presentation/directions checks passed** using `--camera-map`. They cover current fix/expiry/accuracy/held states, unknown distances/status, route-version ownership, invalid interior geometry, viewport ownership, actual dev turn classification, held progress, route-end wording and repeated vertices. All previous runner checks and the four parser fixes remain exactly present; the earlier 864 AR and 412 policy counts are separate earlier results, not new runs or additive coverage claims.
- Final C#/XAML **Compile** verification passed with **0 warnings, 0 errors**. Source checks caught the two missing icon references and an encoding error introduced during the final display refinement; the icons/text were corrected, and final compilation plus protected-control/source comparisons passed. The new code/test boundary is exactly **8 paths**. ARCore declarations, XML, resource references and staged whitespace checks passed. Existing APK sizes/modification times are unchanged; no APK was packaged or copied.
- No running application/browser/device, hosted backend/provider, integration/E2E test, new dependency, database/schema change, branch merge, push or deployment was performed. Actual layout, touch interaction, GPS, AR switching, map rendering and lifecycle behavior remain unverified on device.

## Required manual Camera 2D checks

Use a later requested device build and report actual results before behavior-dependent fixes; APK packaging remains deferred:

1. Switch AR/2D/Flood, then leave/background/resume Camera. Expect Athena's real map/planner in 2D, the previous dev AR/flood behaviors in their modes, no hidden-map GPS/tiles continuing after exit, and no stale or duplicate display layers after return. Check narrow/rotated screens and keyboard/search layout on device.
2. Select a directory shelter and press Start. Compare AR and 2D route/destination/progress, then test hazard reroute, slow/failed GPS/network, Retry, Stop, destination change and leaving during a request. Expect one dev route flow, clear waiting/failure states and no old route displayed for a new trip.
3. Test denied/missing/stale/inaccurate GPS. Expect Current location/nearby distances to be unknown with no fabricated user pin. Restore a current qualifying fix; expect the actual GPS pin and approximate straight-line distances. Directory status stays unknown unless supplied; map colors never certify safety.
4. Pan/zoom while location updates. Expect your view retained after the route's initial frame. Recenter and Overview must change it only when requested; Detailed Offline Map Off/On and cache clearing must follow the shared map rules.
5. Approach the destination and compare the 2D banner/directions with dev's arrival prompt. Expect **Destination ahead** in the overview and the existing confirmation rules before a confirmed arrival; proximity within 15m alone must not announce success. Voice/turn vibration are pending their own choices.

## Navigation voice and turn feedback completed

- Code/checks commit: `cffe917 feat(navigation): add shared voice and turn feedback`.
- The user approved one saved voice preference defaulting **Off**, shared by the Camera speaker and App Settings; qualified dev instructions with cancellation and confirmation-only arrival; and qualified near-turn pulses using the existing **Haptic Feedback** preference alongside advisory vibration. Existing preference keys/defaults were retained, with no second voice flag.
- Added Athena's cancellable single-speaker service, MAUI speech adapter and short-pulse policy. A display-only coordinator guards the current trip/route context, serializes speech, suppresses repeated cues and cancels old queued speech on mute, lost qualification, destination change, page exit and app stop. Unsupported speech/vibration reports a written-directions fallback and avoids retrying the failing provider on every display tick. Voice Off/On permits a fresh speech attempt.
- AR speech consumes the actual dev turn presentation with current route/version/age/visibility checks. 2D uses dev's geographic turn classifier and accepted progress. Current GPS quality, held/off-route/low-confidence progress, route replacement, hazard/emergency interruption and Flood/hidden/stopped views guard ordinary feedback. No parallel route or progress owner, guessed street name, preset GPS, or 15m arrival rule was introduced.
- Only dev's confirmed-arrival flag can publish **Arrival confirmed at ...**. A route-overview destination-ahead step remains prospective. Confirmation cancels the preceding instruction before its arrival cue; hiding the old AR turn card cannot cancel that cue. Leaving/stopping the page still cancels it. Native speech cancellation timing remains device/provider dependent and has not been measured here.
- The speaker control sits beside the Camera sub-tabs to remain accessible in AR and 2D without overlapping the planner. The two volume SVGs are exact Athena assets. The App Settings voice switch shares the saved preference, defaults Off and retains the existing failed-write rollback behavior. The former advisory-vibration row now clearly describes both advisory and qualified-turn feedback. Advisory feed/popups, automatic sound and the manual siren remain unchanged.
- **32/32 targeted navigation-feedback checks passed** using fake providers. Cases cover disabled/inactive/unqualified contexts, cue deduplication and distance bands, independent haptics, confirmed-arrival wording, mute/uncertainty/destination/exit/stop cancellation, one-speaker serialization with a slow provider and fallback/retry behavior. These simulate service calls and tokens; they do not test actual phone audio/vibration or lifecycle events.
- **415/415 local policy checks passed**, including the previous 412 and **three shared voice-preference cases** for default Off, shared persistence/independence and failed writes. The earlier 41 Camera cases and 864 AR cases are unchanged-source earlier results and were not rerun or added to these counts.
- Initial and final C#/XAML **Compile** checks passed with **0 warnings, 0 errors**. The final source refinement protects confirmed-arrival speech from the hidden turn card and places the control beside the tabs. Source verification passed the exact **16-path** boundary, all **105 prior Camera control subtrees**, existing map/settings body restoration, preserved runner entries/parser fixes, exact Athena icon payloads, ARCore declarations and staged whitespace checks.
- The original dev Camera body still restores exactly after reversing **five** accepted hook sites (the prior four display/lifecycle sites plus observing rendered turn guidance). All **32 other pinned dev reference files** remain exact. The original dev source ID, current integrated ID and reversal hunks are recorded for the final audit. No AR/native/GPS/PDR/hazard/arrival/recovery implementation or dependency pin was replaced.
- Existing APK bytes/modification times are unchanged. No APK packaging/copy, physical speech/vibration, running application/browser/device, hosted provider/backend, integration/E2E test, dependency installation, database/schema change, branch merge, push or deployment was performed.

## Required manual navigation-feedback checks

Use a later requested device build and report actual results before behavior-dependent fixes; APK packaging remains deferred:

1. With fresh preferences, expect voice Off. Toggle it in Camera and App Settings, return/restart and confirm both controls show the same saved choice. Failed storage must show a failure and the prior saved choice, rather than a claimed successful save.
2. On a controlled route in AR and 2D, compare spoken instructions with the visible qualified guidance. Expect meaningful changes without a repeated cue each second or overlapping speakers. Mute, change destination, enter Flood, lose reliable positioning, leave Camera and background the app; expect pending old instructions canceled. Report actual provider latency or residual audio.
3. Compare a destination-ahead overview with dev's actual arrival confirmation. Expect no arrival claim from a 15m distance alone. After confirmation, expect one arrival cue while the page remains active, with page exit/stop still canceling it.
4. With voice Off, enable/disable Vibration Feedback around a controlled nearby turn. Expect one short qualified turn pulse when supported, no pulses for uncertain guidance, and existing advisory vibration controlled by the same preference. Check manual/automatic siren and emergency-warning behavior remains as previously selected.
5. Test unavailable speech/vibration support and a voice Off/On retry. Expect a clear written-directions fallback, no repeated provider-failure loop and no effect on route calculation, AR placement or arrival state.

## Dev Admin preservation completed

- Code/source commit: `ebc6eaf feat(admin): retain dev role management and provisioning`.
- The user selected dev access/navigation with newer administrator provisioning/first-login password setup; dev audit/session/database sources; and dev flood/scraper/data workflows. Athena's conflicting Admin/audit/database rewrite and experiment/data deletions stay aside.
- Copied the exact **19-file** newer-dev delta. The complete `RescuAR.Admin` tracked tree equals dev `004ed20` (`036fd835eb4077192b04b4d9a48d39da3ba58cf4`). Existing role/view rules, report moderation/audit, SMS, flood workflows, R snapshots, georeferenced data and other Admin files remain dev's source.
- **19/19 targeted local Node checks passed**. These are source assertions and mocked access responses, not deployed security or database tests. Lint completed with **0 errors and 85 retained dev warnings**. The Admin bundle build passed with its existing large-chunk warning; no unrelated cleanup or dependency upgrade was introduced.
- The worktree has no installed Admin dependency directory and its npm shim could not resolve npm-cli. Checks reused already-installed original-checkout tools/dependencies. Manifest, lockfile and Vite config matched, and direct installed versions matched the lockfile. A temporary resolver built the **integration worktree's source**, with cache/output under ignored worktree artifacts. No packages were installed and no running browser/server was opened.
- SQL, hosted function, patch and deployment scripts were copied as source only. None was executed. Actual deployed provisioning, password setup, role policies and audit/data behavior remain unverified.

## Final archive and platform decisions completed

- The final tree comparison found the root Admin archive reference differed: dev updates it, Athena deletes it, and the integration still had the older reference. The user explicitly selected **Remove the archive; retain the Admin application source**. Commit `1168758 chore(admin): remove tracked archive reference` removes only `RescuAR.Admin.zip` from this branch. This is the only approved dev-file deletion; Admin application source remains exact dev. No remote LFS object or Git history was deleted.
- Final platform review added the speech-engine visibility intent in `22836e1 fix(android): declare navigation speech engine visibility`. Android 11+ target setup requires this entry according to [Microsoft's MAUI speech documentation](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/text-to-speech?view=net-maui-9.0). It adds only `queries/intent/action android.intent.action.TTS_SERVICE`; prior manifest text, AR requirements, permissions and Google Custom Tabs query are retained. XML and ARCore static checks passed. No APK packaging was run for this manifest-only addition.

## Final preservation audit completed

- Audited code checkpoint `22836e154ec0ecde8b165ebc5819324f52b29d0a` against dev `004ed20` and Athena `fa777da`; both source refs remain unchanged. The integration branch remains `codex/integrate-athena-modules` in its separate worktree.
- All **861 dev tracked paths** are accounted for: **784 exact matches**, **76 approved adapted paths**, and **one explicit archive deletion**. The code checkpoint has **87 added paths**, including prior analysis records. There are **no unapproved changes, additions or deletions**. The new final review document adds one documentation path; it does not change application source.
- The complete Admin tree matches dev. Shared AR, navigation/routing datasets, desktop/editor/content, workflows and build scripts retain dev source. The only shared routing implementation deviation is the approved missing-provenance guard. Native dependency/library checks restore exactly after reversing the approved splash and map-resource attributes.
- All **32 non-Camera pinned AR/navigation/check/history paths** remain exact. Camera's original dev body restores exactly after reversing its **five** approved display/feedback/lifecycle hook sites. The regression runner restores to dev after reversing only the four parser fixes and the two added Camera/feedback entries; every original check remains.
- Native manifest semantics match dev after removing only the approved contact/vibration permissions and speech-engine visibility entry. ARCore, Vulkan, camera/native requirements and Custom Tabs remain preserved. Prior APK byte sizes/modification times are unchanged after the user's deferral.
- Current relevant local evidence: **415 policy checks**, **41 Camera presentation checks**, **32 navigation-feedback checks**, **19 Admin checks**, successful C#/XAML compile-only verification and successful Admin bundle build. Admin lint retains **85 warnings, 0 errors**. Earlier 864 AR checks are earlier unchanged-core evidence, not a fresh physical/device test. Counts are separate and overlapping historical runs are not added.
- All finished code/source batches are committed. This final documentation commit records the decisions and limits. **No branch merge, push, deployment, database/function operation, running-app/browser/device check or final-branch APK was performed.** Earlier APK artifacts represent earlier checkpoints and do not contain this final source state.

## Remaining user-controlled validation

Source preparation is complete. Use the [integration review](2026-10-26-integration-review.md) and the per-batch manual cases above when a later build/backend check is requested. Report actual device/provider results before behavior-dependent fixes. **APK packaging remains deferred.** Any merge, push or deployment is a separate later user instruction.

## Post-review Release XAML fix

- After the integration was merged into `migration/athena-modules-pt2` (`51ede19`), a Release build reported XC0002 for `OnArSubTabClicked`. Three Athena sub-tab XAML references had no matching method. They now use dev's existing `OnArCameraModeClicked`, `OnMapModeClicked` and `OnFloodDepthModeClicked` with their existing `TappedEventArgs` signatures and mode-switch flow. No C# navigation/AR/flood behavior changed.
- Fix commits: integration `c628e01`; built checkout `f2589f4`. Both received the same three-binding correction. No branch merge was performed for this fix.
- **Correction to earlier verification claims:** Android's `Compile` target defaults `DesignTimeBuild=true`, which skips XamlC. Earlier compile-only runs validated C#; the earlier C#/XAML pass wording for the offline/Camera/feedback stages overstated their scope.
- Actual production-mode verification used `dotnet msbuild RescuAR.MAUI/RescuAR.MAUI.csproj -t:Compile -p:Configuration=Release -p:DesignTimeBuild=false -p:RescuArDiagnosticBuild=false -p:MauiXamlCValidateOnly=false -p:BuildProjectReferences=false` in `D:/Projects/RescuAR`. Its Release core-library output already existed from the reported build. The log explicitly reports **Compiling Xaml** and creates the Release `XamlC.stamp`; it passed with **0 warnings, 0 errors**. No Release APK was created. No app/device interaction was performed.

## Map restoration after device feedback

- Compared Athena `fa777da`, integration `ac5c60c` and the user's merged/built checkpoint `776aae1`. User approved focused map markers, a local self pin independent of uploads, and the map-first/collapsible People layout.
- Corrected the integration adapter's missing `ILocalTileSource` contract: actual installed Mapsui 5.1 now produces a raster from the production adapter and packaged PNG. The 93,661,024-byte archive is already present and matches its hash inside the existing user-built APK; missing LFS data was not the cause.
- Restored the app-specific online OSM identifier (a plausible 403 cause), canonical endpoint, attribution and scoped seven-day cache. No provider request was made. Removed the added raw graph roads/nodes from Circle/Camera; General Map and all routing data/AR/navigation remain preserved.
- Added a fresh device-local self pin without uploads, duplicate self pins or cloud membership assumptions; retained explicit sharing consent, actual dated member readings and account/page cancellation. Restored compact map space, collapsible People and visible error information. Camera's search/route cards and dev route/GPS/arrival rules remain.
- Code commits: integration `b0ee28c`, `18d7f47`; built checkout `51cac46`, `53c5bc8`. All 11 affected source paths match across checkouts; all 33 approved AR source references and the exact dev Admin tree remain unchanged.
- Passed: 12 actual-library local tile-contract checks, 421 local policy checks (six new local-position cases), 41 Camera-map checks, and final Release compilation with actual XamlC, 0 warnings/errors. No APK generated, app/device/browser/backend interaction, branch merge, push, deployment or database operation.
- Current display choices supersede earlier statements that raw roads/points must appear in every map or that Sharing Off prevents device-local GPS acquisition. Earlier archive-only checks did not exercise the Mapsui tile contract. See [comparison and device follow-up](2026-10-26-map-restoration.md).

## 27 October — source-only Athena regression recheck

- Compared Athena `fa777da` with integration `9fc7468`; application code was not changed. Main directory Supabase/cache loading is implemented, but Camera uses dev's static three-entry repository instead of Athena's cache-connected repository/16-entry fallback. The previous restoration missed this.
- Found the two lost directory-to-2D journey actions, View Map targeting Circles, directory-independent Camera marker caching, unnecessary full chat resets on unchanged polls, and loss of a truthful numeric chat member count. Circle inline membership controls also moved to management; that presentation needs a choice before alteration.
- Corrected source attribution: Athena itself used the incompatible local tile interface. Its repair remains valid, but it was inherited rather than merge-introduced. Low-light Open 2D Map targeting Circles and missing profile notification placeholders are also inherited.
- Only analysis documents updated. No new build/unit suite, device/browser/app/backend operation, APK, merge/push, deployment or application fix. Evidence and proposed sequence: [Athena regression audit](2026-10-27-athena-regression-audit.md).
