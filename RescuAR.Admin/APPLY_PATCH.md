# RescuAR RBAC / Authentication Logout Fix (26 Oct 2026)

## Scope

This is a **small patch based on the exact uploaded `RescuAR.Admin(20261026-045113).zip` snapshot**. It does not modify Supabase SQL, telemetry, PREP exposure calculations, RBAC roles, or Vite environment variables.

### Changes

- `src/components/Auth.jsx`: login now handles **identity verification only**. `AuthWrapper` remains the **single authorization gate** and blocks administrator pages until an RBAC check succeeds. Temporary RBAC lookup errors no longer cause a second, automatic global logout from the login form.
- `src/AuthWrapper.jsx`: explicit Sign Out uses `signOut({ scope: 'local' })`, reports failures, and handles session restoration errors without leaving the page indefinitely loading.
- `src/components/Header.jsx`: manual Sign Out uses `scope: 'local'`, prevents double clicks, and shows errors if sign-out fails.
- `tests/rbac-auth-regression.test.mjs`: 4 lightweight regression tests, including permissions and transient RPC errors.

**Authorization remains mandatory.** A valid Supabase password without an authorized RBAC role cannot open the dashboard. `AuthWrapper` shows an access-unavailable screen with Retry and Sign out.

### Why this fix

The uploaded login component checked `rbac_my_access` independently and called bare `signOut()` when the RPC was unavailable or returned an empty role. A bare sign-out uses global scope and may send `POST /auth/v1/logout?scope=global`. The application already uses an RBAC gate in `AuthWrapper`. The duplicate check could unnecessarily log out authenticated admins during transient lookups or sign-in races.

### Cloudflare `__cf_bm` invalid-domain warning

**Not fixable safely within this React patch.** That cookie is set by Cloudflare/provider infrastructure; Firefox may reject it when the returned Domain does not match the responding host. Do not hardcode an alternative cookie domain or modify Supabase authentication cookies in application code. The warning is generally harmless if authentication and the app continue working. Investigate the `Set-Cookie` response origin with browser Network tools if desired.

### Install

1. Back up the local project (and ideally commit it to a branch).
2. Extract this ZIP **inside** the existing `RescuAR.Admin/` folder so you get `RescuAR.Admin/rbac_auth_fix_patch/`.
3. Open PowerShell in `RescuAR.Admin/` and run:

   ```powershell
   & .\rbac_auth_fix_patch\Apply-Auth-Fix.ps1
   node --test .\tests\rbac-auth-regression.test.mjs
   npm run build
   git diff -- src/components/Auth.jsx src/AuthWrapper.jsx src/components/Header.jsx
   ```

4. If all tests pass, commit only the changed source and test files:

   ```powershell
   git add src/components/Auth.jsx src/AuthWrapper.jsx src/components/Header.jsx tests/rbac-auth-regression.test.mjs
   git commit -m "fix(auth): centralize RBAC gate and use local session logout"
   git push
   ```

5. Confirm Railway deploys the intended commit. Sign in again, monitor the Network tab, test Sign Out intentionally, and verify non-admin accounts cannot open protected views.

**Installer guard:** It compares SHA-256 hashes against the uploaded snapshot, refuses changed files, and backs up originals to `rbac_auth_fix_patch/backups/` before overwriting. It is safe to rerun after successful installation. Do **not** use `git add .` unless you deliberately want to include your patch folder/backups.

### Verification checklist

- [ ] Valid Superadmin can log in and reaches the permitted dashboard.
- [ ] Invalid login still reports an error.
- [ ] Empty-role account cannot enter the dashboard and sees a permission message.
- [ ] Temporary RPC failure shows retry, **not** automatic logout.
- [ ] Manual logout generates a local-scope sign-out and returns to login.
- [ ] Refresh and sign-in preserve RBAC protections.
- [ ] `__cf_bm` warning, if shown, has no observable effect on application functionality.

### Notes / limitations

- Only source-level and local static/unit checks can be completed here; live Supabase and Railway integration must be tested after deployment.
- The existing login's in-memory five-attempt lock is **not** a server-side rate limit and was not changed. Configure provider-side protections separately.
- This patch does **not** finish the RBAC foundation's previously identified RLS/backend authorization hardening; test against direct API access before trusting it for production emergency operations.
- Rotate any administrator password exposed in earlier screenshots.
