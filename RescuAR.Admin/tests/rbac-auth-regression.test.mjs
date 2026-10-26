import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { loadAccess, preferredView, hasPermission } from '../src/rbac/access.js';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relativePath) => fs.readFileSync(path.join(projectRoot, relativePath), 'utf8');

test('login component does not duplicate RBAC lookup or auto-logout authenticated users', () => {
  const login = read('src/components/Auth.jsx');
  assert.match(login, /signInWithPassword\(/);
  assert.doesNotMatch(login, /rbac_my_access/);
  assert.doesNotMatch(login, /auth\.signOut\(/);
});

test('all UI logout buttons use local session scope, never implicit global', () => {
  for (const file of ['src/AuthWrapper.jsx', 'src/components/Header.jsx']) {
    const code = read(file);
    assert.match(code, /auth\.signOut\(\{\s*scope:\s*'local'\s*\}\)/);
    assert.doesNotMatch(code, /auth\.signOut\(\)/);
  }
});

test('RBAC permits approved superadmin and blocks empty permissions', async () => {
  const valid = await loadAccess({ rpc: async () => ({data:{roles:['superadmin'],permissions:['system.all']},error:null}) });
  assert.equal(valid.roles[0], 'superadmin');
  assert.equal(hasPermission(valid, 'advisory.manage'), true);
  assert.ok(preferredView(valid));
  const empty = await loadAccess({ rpc: async () => ({data:{roles:[],permissions:[]},error:null}) });
  assert.equal(preferredView(empty), null);
});

test('RBAC temporary lookup errors propagate for retry, not silent access', async () => {
  await assert.rejects(() => loadAccess({rpc: async () => ({data:null,error:new Error('Service unavailable')})}), /Service unavailable/);
});
