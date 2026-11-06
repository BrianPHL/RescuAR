import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  buildHotlinePayload, createHotline, editHotline, removeHotline, HotlinePermissionError,
} from '../src/services/hotlineManagement.js';

const sample = {
  agency: '  Marikina Rescue  ', category: 'Marikina DRRMO',
  primaryNumber: ' 911 ', alternativeNumber: '(02) 1234-5678', email: '',
  availability: '24/7', coverage: 'Citywide',
};

function makeClient({ user = { id: 'u-1' }, allowed = true, result = { data: [{ id: 'h-1' }], error: null } } = {}) {
  const calls = [];
  const query = {
    insert(payload) { calls.push(['insert', payload]); return this; },
    update(payload) { calls.push(['update', payload]); return this; },
    delete() { calls.push(['delete']); return this; },
    eq(column, value) { calls.push(['eq', column, value]); return this; },
    select(column) { calls.push(['select', column]); return Promise.resolve(result); },
  };
  return {
    calls,
    client: {
      auth: { async getUser() { calls.push(['getUser']); return { data: { user }, error: null }; } },
      async rpc(name, args) { calls.push(['rpc', name, args]); return { data: allowed, error: null }; },
      from(name) { calls.push(['from', name]); return query; },
    },
  };
}

test('normalizes and validates hotline input', () => {
  const payload = buildHotlinePayload(sample);
  assert.equal(payload.agency, 'Marikina Rescue');
  assert.equal(payload.primary_number, '911');
  assert.equal(payload.alternative_number, '(02) 1234-5678');
  assert.equal(payload.email, null);
  assert.throws(() => buildHotlinePayload({ ...sample, primaryNumber: 'javascript:alert(1)' }), /valid primary/);
  assert.throws(() => buildHotlinePayload({ ...sample, agency: '' }), /agency/);
  assert.throws(() => buildHotlinePayload({ ...sample, primaryNumber: '--' }), /valid primary/);
});

test('create confirms live authentication and permission before insert', async () => {
  const { client, calls } = makeClient();
  await createHotline(client, sample);
  assert.deepEqual(calls.map(c => c[0]), ['getUser', 'rpc', 'from', 'insert', 'select']);
  assert.deepEqual(calls[1], ['rpc', 'rbac_can', { p_permission: 'hotline.manage' }]);
  assert.equal(calls[2][1], 'emergency_hotlines');
});

test('edit confirms authorization before updating an exact record', async () => {
  const { client, calls } = makeClient();
  await editHotline(client, 73, sample);
  assert.deepEqual(calls.map(c => c[0]), ['getUser','rpc','from','update','eq','select']);
  assert.deepEqual(calls[4], ['eq', 'id', 73]);
});

test('delete confirms authorization before deleting an exact record', async () => {
  const { client, calls } = makeClient();
  await removeHotline(client, 'h-1');
  assert.deepEqual(calls.map(c => c[0]), ['getUser','rpc','from','delete','eq','select']);
  assert.deepEqual(calls[4], ['eq', 'id', 'h-1']);
});

test('unauthenticated client cannot insert, update, or delete', async () => {
  for (const action of [(c) => createHotline(c, sample), (c) => editHotline(c, 1, sample), (c) => removeHotline(c, 1)]) {
    const { client, calls } = makeClient({ user: null });
    await assert.rejects(() => action(client), HotlinePermissionError);
    assert.ok(!calls.some(c => c[0] === 'from'));
  }
});

test('authenticated user without hotline.manage cannot mutate records', async () => {
  for (const action of [(c) => createHotline(c, sample), (c) => editHotline(c, 1, sample), (c) => removeHotline(c, 1)]) {
    const { client, calls } = makeClient({ allowed: false });
    await assert.rejects(() => action(client), HotlinePermissionError);
    assert.ok(!calls.some(c => c[0] === 'from'));
  }
});

test('zero affected rows is treated as a failed write', async () => {
  const { client } = makeClient({ result: { data: [], error: null } });
  await assert.rejects(() => editHotline(client, 1, sample), /not changed/);
});

test('database permission errors are shown as access failures', async () => {
  const { client } = makeClient({ result: { data: null, error: { code: '42501', message: 'RLS rejection' } } });
  await assert.rejects(() => removeHotline(client, 1), HotlinePermissionError);
});

test('SQL migration requires authenticated role-based access for every write', () => {
  const sql = readFileSync(new URL('../database/migrations/008_secure_emergency_hotlines.sql', import.meta.url), 'utf8');
  assert.match(sql, /ENABLE ROW LEVEL SECURITY/i);
  assert.match(sql, /REVOKE INSERT, UPDATE, DELETE .* FROM PUBLIC, anon/i);
  for (const op of ['INSERT','UPDATE','DELETE']) {
    assert.match(sql, new RegExp(`AS RESTRICTIVE FOR ${op} TO authenticated`, 'i'));
    assert.match(sql, new RegExp(`AS PERMISSIVE FOR ${op} TO authenticated`, 'i'));
  }
  assert.equal((sql.match(/public\.rbac_can\('hotline\.manage'\)/g) || []).length, 8);
  assert.match(sql, /hotlines_preserve_existing_read_access/);
});

test('Admin route passes live session and access to hotline component', () => {
  const app = readFileSync(new URL('../src/App.jsx', import.meta.url), 'utf8');
  const page = readFileSync(new URL('../src/components/EmergencyHotlines.jsx', import.meta.url), 'utf8');
  assert.match(app, /<EmergencyHotlines session=\{session\} access=\{access\}/);
  assert.match(page, /hasPermission\(access, 'hotline\.manage'\)/);
  assert.match(page, /onClick=\{openEdit\}/);
  assert.match(page, /onClick=\{handleDelete\}/);
});


test('a revoked permission is rechecked before each subsequent write', async () => {
  const state = { permitted: true };
  let writes = 0;
  const { client } = makeClient();
  const originalRpc = client.rpc;
  client.rpc = async (...args) => {
    const response = await originalRpc(...args);
    return { ...response, data: state.permitted };
  };
  const originalFrom = client.from;
  client.from = (table) => {
    writes++;
    return originalFrom(table);
  };
  await createHotline(client, sample);
  state.permitted = false;
  await assert.rejects(() => editHotline(client, 1, sample), HotlinePermissionError);
  assert.equal(writes, 1);
});

test('failed permission lookup prevents database mutations', async () => {
  const { client, calls } = makeClient();
  client.rpc = async () => ({ data: null, error: { message: 'offline' } });
  await assert.rejects(() => createHotline(client, sample), /Unable to verify hotline management access/);
  assert.ok(!calls.some(c => c[0] === 'from'));
});

test('empty or missing ID cannot mutate a hotline', async () => {
  const { client, calls } = makeClient();
  await assert.rejects(() => editHotline(client, '', sample), /Select a hotline/);
  await assert.rejects(() => removeHotline(client, null), /Select a hotline/);
  assert.equal(calls.length, 0);
});
