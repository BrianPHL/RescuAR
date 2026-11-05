import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (file) => readFileSync(path.join(root, file), 'utf8');
const ui = read('src/components/RbacAdmins.jsx');
const css = read('src/components/RbacPages.css');
const fn = read('supabase/functions/rbac-admin-provision/index.ts');
const sql = read('database/migrations/006_rbac_existing_user_promotion.sql');
const firstLogin = read('src/components/FirstLoginPasswordChange.jsx');
const oldSql = read('database/migrations/005_rbac_admin_provisioning.sql');

test('existing-user promotion is opt-in and keeps existing password by default', () => {
  assert.match(ui, /useState\('keep'\)/);
  assert.match(ui, /password_mode|existingPasswordMode/);
  assert.match(ui, /supabase\.rpc\('rbac_set_assignment'/);
  assert.match(ui, /body: \{ action: 'assign_existing_admin'/);
  assert.match(ui, /window\.confirm\(/);
  assert.match(ui, /replaces the current password|password will be replaced/i);
  assert.match(css, /rbac-existing-password-options/);
});

test('new and existing administrator credentials are displayed only in memory with no storage', () => {
  assert.match(ui, /oneTimeCredential\?\.source === 'create'/);
  assert.match(ui, /oneTimeCredential\?\.source === 'existing'/);
  assert.doesNotMatch(ui, /localStorage|sessionStorage/);
  assert.doesNotMatch(fn, /console\.log\(.*password/i);
  assert.match(fn, /crypto\.getRandomValues/);
  assert.match(fn, /temporaryPassword: password/);
});

test('Edge Function verifies Superadmin and authorizes role against existing records', () => {
  assert.match(fn, /auth\.getUser\(token\)/);
  assert.match(fn, /if \(body\.action === 'assign_existing_admin'\)/);
  assert.match(fn, /\.eq\('role_id', 'superadmin'\)/);
  assert.match(fn, /setupStatus\?\.must_change_password/);
  assert.match(fn, /role === 'superadmin'/);
  assert.match(fn, /rbac_prepare_existing_admin_promotion/);
  assert.match(fn, /auth\.admin\.updateUserById\(promotedId, \{ password \}\)/);
});

test('server-side SQL denies email-unconfirmed or already-admin promotion, gates before credential replacement', () => {
  assert.match(sql, /email_confirmed_at/);
  assert.match(sql, /target_confirmed IS NULL/);
  assert.match(sql, /target = p_actor/);
  assert.match(sql, /p_role = 'superadmin'/);
  assert.match(sql, /SELECT 1 FROM public\.rbac_admin_assignments a WHERE a\.user_id = target/);
  assert.match(sql, /INSERT INTO public\.rbac_password_setup/);
  assert.match(sql, /INSERT INTO public\.rbac_admin_assignments/);
  assert.match(sql, /GRANT EXECUTE ON FUNCTION public\.rbac_prepare_existing_admin_promotion\(text,text,uuid,text\) TO service_role/);
  assert.match(sql, /REVOKE ALL ON FUNCTION public\.rbac_prepare_existing_admin_promotion\(text,text,uuid,text\) FROM PUBLIC, anon, authenticated/);
  assert.match(oldSql, /NOT EXISTS \(\s*SELECT 1 FROM public\.rbac_password_setup s/);
});

test('password reset error triggers controlled rollback with audit trail', () => {
  assert.match(fn, /rbac_abort_existing_admin_promotion/);
  assert.match(sql, /created_by = p_actor/);
  assert.match(sql, /temporary_password_sha256 = p_password_sha256/);
  assert.match(sql, /existing_user_admin_promotion_rolled_back/);
  assert.match(sql, /GRANT EXECUTE ON FUNCTION public\.rbac_abort_existing_admin_promotion\(uuid,text,uuid,text\) TO service_role/);
});

test('first-login setup requires proof of the temporary credential, not just an old authenticated session', () => {
  assert.match(firstLogin, /temporaryPassword/);
  assert.match(firstLogin, /id="first-admin-temp-password"/);
  assert.match(firstLogin, /complete_first_login', temporaryPassword, newPassword/);
  assert.match(fn, /setup\.temporary_password_sha256 !== await passwordSha256\(body\.temporaryPassword\)/);
  assert.match(fn, /New password must differ from the temporary password/);
});
