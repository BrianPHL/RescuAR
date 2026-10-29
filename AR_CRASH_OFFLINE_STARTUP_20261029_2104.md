# Native crash and offline account startup — 29 October 2026

## Evidence

- Complete field archive: `Logs/RescuAR_FieldTest_20261029_210402.zip`, 21:04:06–21:07:17 PHT, Samsung SM-A546E / Android 14.
- Installed candidate remained `14821a40dd61330edafdc1086e9dfe1114c3a718e477e217819754efbc1def50`, the preceding guidance-presentation APK.
- Current source baseline: `24ca006f7b35091e921b514493e5428cf44b1f7d`; changes below are uncommitted.
- Extracted logs, exact runtime binary, matched symbols and decoded stack are under `artifacts/field-review-20261029-2104/`.

## Crash: confirmed failure and mitigation

At **21:05:47.914**, process 25817 crashed with SIGSEGV / null pointer dereference at address `0x37` on a .NET thread-pool worker. Camera's **2D Map** view was active, ARCore was paused, and no navigation route had been accepted. The capture contains no managed unhandled-exception stack identifying an application method.

The APK's `libmonosgen-2.0.so` build ID is `6bceca0e711d8fb06f6ba715055d597afce5c4b6`. Microsoft symbols downloaded by that exact ID match and decode the native frames to:

1. `is_opaque_object`, `sgen-tarjan-bridge.c:612`
2. `push_all`, `sgen-scan-object.h:59`
3. `sgen_bridge_processing_stw_step`
4. nursery collection / `sgen_perform_collection_inner`
5. string allocation / `System.String.FastAllocateString`

This establishes a fault while the Mono garbage-collection bridge was scanning object references. It does **not** identify the managed caller allocating the string, prove why an invalid reference existed, or establish that ARCore, a route algorithm, the new overlay stack or a particular map layer caused it. Earlier Java garbage-collection messages are activity context, not an out-of-memory diagnosis.

The pinned Mono 9.0.20 runtime, commit `3879076d9a06ce098d37c3882fb1845a6627335b`, supports `new` and `tarjan` bridge implementations; `tarjan` is its default. Its historical `old` option is unsupported and redirects to `new`. See the [exact runtime source](https://github.com/dotnet/runtime/blob/3879076d9a06ce098d37c3882fb1845a6627335b/src/mono/mono/metadata/sgen-bridge.c#L82-L114).

The candidate packages `MONO_GC_PARAMS=bridge-implementation=new` using AndroidEnvironment. Build diagnostics now report the effective runtime parameter and build metadata records the choice. This is a **targeted mitigation**, requiring device crash and responsiveness validation. The underlying memory fault is not proven eliminated. Framework, MAUI, ARCore, custom native bridge and navigation coordinate/accuracy policies remain unchanged.

## Offline startup: source-confirmed defects

Offline access already existed in AuthenticationFlowCoordinator, but its production adapter did not fully support it:

- Reading the saved identity only deserialized secure storage; it did not load that identity into the SDK. Offline activation populated preferences, while account-scoped local features expected `Auth.CurrentUser` to exist.
- GetClientAsync awaited Supabase InitializeAsync. The [pinned Supabase 1.1.1 implementation](https://github.com/supabase-community/supabase-csharp/blob/f37fef770d3f281e6adea74382a4144c0f4964a1/Supabase/Client.cs#L202-L210) refreshes the session and connects realtime during initialization, making it unsuitable as a mandatory local startup gate.
- [Gotrue 6.0.3 RetrieveSessionAsync](https://github.com/supabase-community/gotrue-csharp/blob/4c5f21981ed206a27a0367a363c9e2984b59f228/Gotrue/Client.cs#L592-L633) destroys expired sessions before refresh, and converts refresh exceptions into session destruction plus a null result. The app could then classify connection loss as an invalid login and clear the saved account.
- The SDK's automatic-refresh listener can emit SignedOut when loading an expired session. That is unsuitable for a remembered identity allowed to use local features while offline.

The supplied capture establishes the native crash. It does not contain a diagnostic account-restoration failure identifying which of these offline paths happened on the phone; those defects are confirmed by implementation and pinned SDK source.

## Offline startup changes

- Load secure persistence into the SDK **locally**, giving account-scoped caches a real remembered identity without an HTTP request.
- Pause the automatic-refresh listener while restoring locally or offline. Resume it for valid online sessions after verification or connectivity recovery; an expired local identity cannot start online auto-refresh.
- Refresh tokens through the API that exposes transport failures without silently destroying the current session. Existing classification still distinguishes offline transport failures from explicit authentication rejection.
- Bound restoration/profile network waits to eight seconds per request. Connection failure or timeout can enter the existing local-access path; explicit server rejection, sign-out, disabled Remember Me, invalid saved data, and account/revision changes retain their existing safeguards.
- Connect realtime independently of account startup. Cloud failures do not prevent the saved account opening local features.

No login credentials are created from preferences. Local access is not proof of an unexpired cloud token. Remember Me remains an explicit user choice. Sessions already deleted by the preceding SDK behavior cannot be reconstructed; sign in online once after installing this candidate.

## Verification and candidate

- Release diagnostic Android build: **passed**, 0 warnings / 0 errors, 1m51.19s.
- **42 targeted session-persistence, identity and startup checks passed.** New checks exercise the real pinned SDK with no HTTP requests: expired-session local restoration, refresh-listener suppression, preserved saved identity and explicit sign-out removal. Existing different-account, late-callback, rejection and revision guards passed.
- Release-readiness checks, full APK manifest/ARM64/native validation and `git diff --check`: passed.
- APK inspection confirms `MONO_GC_PARAMS` / `bridge-implementation=new` inside `lib/arm64-v8a/libxamarin-app.so`; evidence is in `package-evidence/runtime-gc-environment.json`. The next field log must confirm the effective process setting.
- Candidate: `artifacts/field-test-20261029-crash-offline/RescuAR-20261029-Crash-Offline.apk`.
- APK SHA-256: `f1c8e9daa2fc79d218a47776da846d1b0e3dc2c368e0b98435a5fd6e7343ce13`.
- Custom native bridge unchanged: `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
- No physical-device test, UI automation or integration test was performed. Successful builds/host checks do not establish crash elimination or complete offline device behavior.

## Required device test

1. Install over the existing app, sign in online once with **Remember Me enabled**, then enable airplane mode, force-close and reopen. Expected: the saved account opens local features without requiring online sign-in. Offline map/A* access still depends on installed local data and usable device location. Cloud updates remain unavailable offline.
2. With capture running, repeat the observed Camera → 2D Map sequence, pan/zoom and switch back several times. Leave the map open for at least two minutes. Expected: no process restart, acceptable responsiveness, and `monoGcParams='bridge-implementation=new'` in BUILD_MANIFEST.
3. Reconnect and check cloud account/profile access. Sign out, disable networking and reopen. Expected: the signed-out account is not restored. For long-offline/expired-token validation, repeat offline reopening after the access token expires without manually editing storage or credentials.

Return the capture and actual results before further crash-dependent changes. If it still crashes, the next native stack determines whether the failure remains in GC bridge processing or has a different cause.
