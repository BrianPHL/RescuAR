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

## Current next gate

Ask before reconciling shared offline-map display/packages and the deferred offline-map control. Then reconcile Camera 2D, navigation voice/turn feedback and later Admin preservation. Keep accepted dev AR, routing/GPS state and package pins. Commit every finished batch; no branch merge or push is authorized yet.
