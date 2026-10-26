import assert from 'node:assert/strict';
import test from 'node:test';
import { VIEW_PERMISSION, canView, loadAccess, preferredView } from '../src/rbac/access.js';

test('loaded account permissions make assigned navigation destinations available', async () => {
  const calls = [];
  const access = await loadAccess({
    async rpc(name) {
      calls.push(name);
      return {
        data: {
          roles: ['operations_admin'],
          permissions: ['monitoring.view', 'hazard.view', 'advisory.manage'],
        },
      };
    },
  });

  assert.deepEqual(calls, ['rbac_my_access']);
  assert.deepEqual(access.roles, ['operations_admin']);
  assert.equal(canView(access, 'monitoring-stations'), true);
  assert.equal(canView(access, 'monitoring-river-level'), true);
  assert.equal(canView(access, 'monitoring-hazard-exposure'), true);
  assert.equal(canView(access, 'content-advisories'), true);
  assert.equal(canView(access, 'community-user-management'), false);
  assert.equal(canView(access, 'rbac-admins'), false);
});

test('superadmin permissions include the existing administration and testing pages', () => {
  const access = { roles: ['superadmin'], permissions: ['system.all'] };
  for (const view of Object.keys(VIEW_PERMISSION)) {
    assert.equal(canView(access, view), true, `${view} should be available`);
  }
  assert.equal(canView(access, 'rbac-admins'), true);
  assert.equal(canView(access, 'rbac-tests'), true);
});

test('accounts without monitoring access start on an assigned page', () => {
  const access = { roles: ['custom_moderator'], permissions: ['reports.moderate'] };
  assert.equal(canView(access, 'monitoring-stations'), false);
  assert.equal(preferredView(access), 'community-reports-moderation');
});

test('missing or malformed access never grants navigation or a default page', async () => {
  for (const data of [null, {}, { roles: 'superadmin', permissions: 'system.all' }]) {
    const access = await loadAccess({ async rpc() { return { data }; } });
    assert.deepEqual(access, { roles: [], permissions: [] });
    assert.equal(preferredView(access), null);
    assert.equal(canView(access, 'monitoring-stations'), false);
  }
  assert.equal(canView(undefined, 'dashboard'), false);
  assert.equal(preferredView(null), null);
  assert.equal(preferredView({ roles: ['research_head'], permissions: ['research.view'] }), null);
});

test('permission service failures propagate instead of granting fallback access', async () => {
  const error = new Error('Permission request failed');
  await assert.rejects(loadAccess({ async rpc() { return { error }; } }), error);
  await assert.rejects(loadAccess({ async rpc() { throw error; } }), error);
});
