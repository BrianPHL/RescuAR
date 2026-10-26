import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (file) => readFileSync(path.join(root, file), 'utf8');
const sql = read('database/migrations/005_rbac_admin_provisioning.sql');
const fn = read('supabase/functions/rbac-admin-provision/index.ts');
const wrapper = read('src/AuthWrapper.jsx');
const adminPage = read('src/components/RbacAdmins.jsx');
const passwordPage = read('src/components/FirstLoginPasswordChange.jsx');

test('only Superadmin can provision accounts; role is looked up on server', () => {
  assert.match(fn, /auth\.getUser\(token\)/);
  assert.match(fn, /\.eq\('role_id', 'superadmin'\)/);
  assert.match(fn, /role === 'superadmin'/);
  assert.match(fn, /rbac_register_provisioned_admin/);
  assert.match(sql, /Not an active Superadmin/);
  assert.match(sql, /REVOKE ALL ON FUNCTION public\.rbac_register_provisioned_admin\(uuid,text,uuid,text\) FROM PUBLIC, anon, authenticated/);
  assert.match(sql, /GRANT EXECUTE ON FUNCTION public\.rbac_register_provisioned_admin\(uuid,text,uuid,text\) TO service_role/);
});

test('generated temporary password is ephemeral and never placed in app database or logs', () => {
  assert.match(fn, /crypto\.getRandomValues/);
  assert.match(fn, /temporaryPassword: password/);
  assert.match(fn, /await passwordSha256\(password\)/);
  assert.doesNotMatch(fn, /console\.log\(.*password/i);
  assert.doesNotMatch(adminPage, /localStorage|sessionStorage/);
  assert.match(sql, /temporary_password_sha256/);
  assert.match(sql, /temporary_password_sha256=NULL/);
});

test('email is preconfirmed but new administrator is blocked pending password change', () => {
  assert.match(fn, /email_confirm: true/);
  assert.match(fn, /mustChangePassword: true/);
  assert.match(sql, /NOT EXISTS \(\s*SELECT 1 FROM public\.rbac_password_setup s/);
  assert.match(wrapper, /rbac_password_setup_status/);
  assert.match(wrapper, /passwordChangeRequired/);
  assert.match(wrapper, /<FirstLoginPasswordChange/);
  assert.match(passwordPage, /complete_first_login/);
  assert.match(fn, /New password must differ from the temporary password/);
});

test('unprivileged browser never has service-role key and pending database state is server-only', () => {
  assert.doesNotMatch(adminPage, /SERVICE_ROLE_KEY|service_role/);
  assert.doesNotMatch(passwordPage, /SERVICE_ROLE_KEY|service_role/);
  assert.match(sql, /REVOKE ALL ON TABLE public\.rbac_password_setup FROM PUBLIC, anon, authenticated/);
  assert.match(fn, /SUPABASE_SERVICE_ROLE_KEY/);
  assert.match(fn, /RESCUAR_ADMIN_ALLOWED_ORIGINS/);
  assert.match(fn, /\bCache-Control': 'no-store'/);
});

test('account creation failure attempts Auth user rollback', () => {
  assert.match(fn, /admin\.auth\.admin\.deleteUser\(userId\)/);
  assert.match(sql, /admin_account_provisioned/);
  assert.match(sql, /first_login_password_changed/);
});
