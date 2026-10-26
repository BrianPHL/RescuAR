# RescuAR Admin — Create Verified Administrators (Temporary Password) Patch

## Scope and prerequisites

Source archive: `RescuAR.Admin(20261026-065143).zip` (October 26, 2026).

This is a **patch**, not a replacement installation. It adds **Create Verified Administrator** to the existing Community Management → User Management → Administrator Management page. A Superadmin chooses an email and role; the authenticated Supabase Edge Function creates an email-confirmed Auth user, assigns an RBAC role, stores a first-login status, and returns a cryptographically random temporary password **once**. At first login, the new administrator must set a different strong password before reaching the dashboard.

**“Verified” means an existing Superadmin has independently checked the person's identity and approved the email address.** Supabase's `email_confirm: true` does **not** prove mailbox ownership; this workflow intentionally skips the email invitation/verification process.

The patch does not change existing Superadmin credentials, does not provision a second Superadmin, and does not modify live river telemetry or advisories.

## Files

- `src/components/RbacAdmins.jsx` — new administrator form, one-time secret display
- `src/components/RbacPages.css` — form styling consistent with existing RescuAR Admin
- `src/AuthWrapper.jsx` — mandatory first-login gate before loading main app
- `src/components/FirstLoginPasswordChange.jsx` — first-login password form
- `src/components/FirstLoginPasswordChange.css` — first-login styling
- `database/migrations/005_rbac_admin_provisioning.sql` — server-only password setup, secure RPCs, deny while pending
- `supabase/functions/rbac-admin-provision/index.ts` — verifies JWT, authorizes Superadmin, provisions Auth user, changes password
- `tests/rbac-admin-provisioning.test.mjs` — source/security regression checks

## Installation order (important)

1. **Back up** your current `RescuAR.Admin` folder and your Supabase database.
2. Extract `RescuAR_Admin_Provisioning_PATCH.zip` **inside** your `RescuAR.Admin` project directory. It creates a `RescuAR_Admin_Provisioning_PATCH` subfolder.
3. From the root of `RescuAR.Admin`, run PowerShell:

   ```powershell
   & .\RescuAR_Admin_Provisioning_PATCH\Apply-Patch.ps1 -DryRun
   & .\RescuAR_Admin_Provisioning_PATCH\Apply-Patch.ps1
   ```

   The installer compares the SHA-256 of your three existing files with the uploaded source archive, refuses to overwrite unmatched files, and backs up replaced files. If there is a mismatch, **stop** and manually review/merge the changes. Do not blindly overwrite a newer project.

4. Open **Supabase Dashboard → SQL Editor** for the project used by Railway. Execute `database/migrations/005_rbac_admin_provisioning.sql` as the project owner. It requires the existing `004_rbac_foundation.sql` RBAC migration. **Do not rerun 004 or reassign your existing Superadmin.**
5. Configure the **Supabase Edge Function secret** (NOT a `VITE_` environment variable). The function uses Supabase's server-side `SUPABASE_URL` and `SUPABASE_SERVICE_ROLE_KEY`, which should already be provided by the Supabase hosted function environment. Set:

   ```dotenv
   RESCUAR_ADMIN_ALLOWED_ORIGINS=https://admin-rescuar-production.up.railway.app,http://localhost:5173
   ```

   Use your **actual** deployed URL (shown above only as an example) and any approved localhost origin. Do not add a wildcard `*`, a trailing slash, or unknown origins. If your domain differs, update the value before deployment. Keep secret keys out of Git and the frontend.

6. Deploy the Edge Function to the **same Supabase project** (not to Railway):

   ```powershell
   supabase login
   supabase link --project-ref YOUR_SUPABASE_PROJECT_REF
   supabase secrets set RESCUAR_ADMIN_ALLOWED_ORIGINS="https://YOUR_RAILWAY_DOMAIN,http://localhost:5173"
   supabase functions deploy rbac-admin-provision
   ```

   The platform normally verifies JWTs. For projects where the legacy Edge Function gateway rejects asymmetric JWTs, review Supabase's current Edge Function authentication documentation. You may deploy the function with `--no-verify-jwt` **only because this implementation independently validates `Authorization` via `admin.auth.getUser(token)` and checks the role server-side**. Do not disable JWT verification on other functions as a workaround.

7. Verify local build and tests:

   ```powershell
   node --test .\tests\rbac-admin-provisioning.test.mjs
   node --test .\tests\rbac-auth-regression.test.mjs
   npm run build
   ```

8. Commit and push only relevant source, migration, Edge Function, and tests (do not commit `.env`, backups, or the patch folder). Railway then redeploys the UI. **Deploying just the frontend is insufficient:** the SQL migration and Edge Function are required for account creation.

## Usage

1. Log into RescuAR Admin as the existing Superadmin.
2. Go to **Community Management → User Management → Administrator Management**.
3. Enter the approved real email of a NEW administrator and select a non-Superadmin role.
4. Click **Create Admin**. The temporary password appears once. Share it securely with the intended person; do not put it in a public chat, issue tracker, email thread, or screenshot. Clear your clipboard afterward.
5. New administrator uses the standard email + temporary password login.
6. The application **blocks access to the admin dashboard** and requires a new strong password. The backend rejects reuse of the temporary password. After success, their authorized pages become available.

## Important behavior / limitations

- Existing Auth accounts must still use the existing **Assign Role** form. The Create Admin form rejects duplicate Auth emails.
- This patch will not prove the person owns the email. Confirm identity through your internal procedures before provisioning, and configure a secure account recovery process.
- Existing RBAC RPCs calling `rbac_can()` are blocked until password rotation, and the React dashboard is gated. **Older database tables/functions that do not use RBAC must be audited separately** before treating the entire RescuAR system as fully protected. This patch is not a blanket fix for legacy authorization.
- A Superadmin can still reset or revoke accounts using existing approved processes; a dedicated account reset UI and recovery workflow are not included in this patch.
- If an Edge Function request fails after Supabase Auth account creation, the function attempts to delete the newly created Auth user; inspect Auth accounts before retrying if registration reports failure.
- Browser UI can't recover the temporary password after dismissal. Use proper admin-authorized reset or recreate the account if appropriate.
- Test in a staging Supabase project with a non-production account before deploying to the emergency-management environment.
- Password fingerprints used solely to reject reuse are SHA-256 of cryptographically random 32-byte temporary credentials; no plaintext password is stored in RescuAR tables. Fingerprint is removed on first successful password change.

## Suggested Git commit

`feat(rbac): add secure administrator provisioning and first-login password setup`
