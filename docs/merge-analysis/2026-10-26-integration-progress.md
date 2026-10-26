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
| S1-03 | SplashViewModel startup/session behavior | Keep dev after explanation | `SplashViewModel.cs` unchanged from current dev baseline. Authentication reconciliation is deferred to its own decisions. |
| A2-01 | Backend initialization and saved sessions | Adopt secure saved sessions and guarded initialization; keep dev server settings | Added secure-storage persistence, ordered writes, project-specific token slots, initialization serialization and stale-client rejection. |
| A2-02 | Authentication client readiness | Wait for initialization | Email/Google sign-in, registration and sign-out await the shared client; sign-up fields and UI flows are unchanged. |
| A2-03 | Local cleanup on sign-out | Adopt reliable cleanup, including network failure; preserve stored account data | Flush/remove stored session; mark removed slots signed out; archive legacy account preferences before clearing active aliases. Archives carry ownership hints only and are not automatically restored as verified account data. |


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
- Account activation/restoration, the Remember Me control, Google account selection, and routing after sign-in are still pending explicit overlap decisions. This is an intermediate service batch, not a claim that authentication migration is complete.
- No live-provider, backend, application, device, integration or E2E validation was performed. Do not infer those results from the build or fake-storage checks.

## Next gate

Obtain the user's next decisions for the shared post-sign-in flow, saved-session startup routing and the Google account-picker path before changing their view models/navigation. Obtain the remaining Remember Me/profile-storage/UI decisions before wiring those behaviors. Device splash appearance/transition remains a user verification step; use the preserved batch-1 APK above.

## Completed-batch commits

- `0498405` — `fix(routing): handle missing provenance in parser checks`
- `70b5139` — `feat(splash): adopt Athena branding and radar animation`
- `5744060` — `feat(auth): persist sessions and preserve identity on sign-out`

The analysis/inventory/progress records are committed separately after these batches.

## Approved next decisions, not yet implemented

- Use one verified-identity/address/permissions flow after email/Google/OTP sign-in.
- Replace flag-only startup with restored identity plus explicit offline/error handling, preserving offline access.
- Require real Google authentication for every displayed account selection.

The original `dev` branch advanced to `822ded0` with an RBAC UI patch after the integration baseline was pinned. Those Admin changes are outside these completed batches and must be preserved/reconciled at the Admin/final preservation gate; this branch was not silently updated.
