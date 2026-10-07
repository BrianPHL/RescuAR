import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createWebAuditService, normalizeAuditFilters } from '../src/services/webAuditService.js';
import { createAuditLogController } from '../src/services/auditLogController.js';

const id = (number) => `00000000-0000-4000-8000-${String(number).padStart(12, '0')}`;
const row = (number) => ({
  id: id(number), operation_id: id(number + 100), actor_id: id(999),
  recorded_at: `2026-10-07T08:00:00.${String(number).padStart(6, '0')}+00:00`,
  event_type: 'user.updated', module: 'community-user-management', source: 'database',
  outcome: 'succeeded', target_type: 'user', target_id: id(800), details: { changed_fields: ['name'] },
});
const page = (number = 1, hasMore = false) => ({ records: [row(number)], hasMore,
  nextCursor: hasMore ? { recordedAt: row(number).recorded_at, id: id(number) } : null });
const deferred = () => {
  let resolve;
  const promise = new Promise((done) => { resolve = done; });
  return { promise, resolve };
};

function controllerFixture() {
  let operation = 200;
  const observations = [];
  const reads = [];
  const states = [];
  let denied = 0;
  const service = {
    newOperationId: () => id(++operation),
    async recordObservation(event) { observations.push(event); return id(500); },
    async readPage(request) { reads.push(request); return page(); },
  };
  const controller = createAuditLogController(service, (state) => states.push(state), () => denied++);
  return { controller, service, observations, reads, states, get denied() { return denied; } };
}

test('the viewer writes only the server-owned observation contract and awaits its event ID', async () => {
  let call;
  const service = createWebAuditService({ async rpc(name, args) { call = { name, args }; return { data: id(1) }; } });
  await service.recordObservation({ operationId: id(2), eventType: 'audit.viewed', targetId: id(3), details: { page: 2 } });
  assert.deepEqual(call, { name: 'record_web_audit_event', args: {
    p_operation_id: id(2), p_event_type: 'audit.viewed', p_module: 'audit-logs',
    p_target_type: 'audit_event', p_target_id: id(3), p_details: { page: 2 },
  } });
  assert.equal(Object.hasOwn(call.args, 'actor_id'), false);
  assert.equal(Object.hasOwn(call.args, 'source'), false);
});

test('paging uses one look-ahead record and preserves the displayed boundary timestamp microseconds', async () => {
  const calls = [];
  const rows = Array.from({ length: 26 }, (_, index) => row(26 - index));
  const service = createWebAuditService({ async rpc(name, args) { calls.push({ name, args }); return { data: rows }; } });
  const filters = { module: ' community-user-management ', actorId: id(999), eventType: ' user.updated ' };
  const first = await service.readPage({ filters });
  assert.equal(first.records.length, 25);
  assert.equal(first.hasMore, true);
  assert.deepEqual(first.nextCursor, { recordedAt: row(2).recorded_at, id: id(2) });
  await service.readPage({ filters, cursor: first.nextCursor });
  assert.deepEqual(calls[1], { name: 'get_web_audit_logs', args: {
    p_limit: 26, p_before_at: row(2).recorded_at, p_before_id: id(2),
    p_module: 'community-user-management', p_actor_id: id(999), p_event_type: 'user.updated',
  } });
});

test('invalid actor IDs never call an RPC; empty filters use SQL nulls', async () => {
  const calls = [];
  const service = createWebAuditService({ async rpc(_name, args) { calls.push(args); return { data: [] }; } });
  await assert.rejects(service.readPage({ filters: { actorId: 'resident@example.test' } }), /valid administrator ID/);
  assert.equal(calls.length, 0);
  assert.deepEqual(await service.readPage(), { records: [], hasMore: false, nextCursor: null });
  assert.deepEqual(calls[0], { p_limit: 26, p_before_at: null, p_before_id: null, p_module: null, p_actor_id: null, p_event_type: null });
  assert.deepEqual(normalizeAuditFilters(), { module: '', actorId: '', eventType: '' });
});

test('RPC errors are sanitized and malformed data cannot be presented as audit history', async () => {
  const service = createWebAuditService({ async rpc() { return { error: { code: '42501', message: 'private backend detail' } }; } });
  await assert.rejects(service.readPage(), (error) => error.accessDenied && !error.message.includes('private'));
  for (const data of [null, {}, [null], [{ ...row(1), recorded_at: 'invalid' }], [{ ...row(1), details: [] }]]) {
    const invalid = createWebAuditService({ async rpc() { return { data }; } });
    await assert.rejects(invalid.readPage(), /could not be read/);
  }
  const missing = createWebAuditService({ async rpc() { return undefined; } });
  await assert.rejects(missing.recordObservation({ operationId: id(1), eventType: 'audit.viewed' }), /could not confirm/i);
});

test('a failed observation prevents the requested read and an access denial clears cached records', async () => {
  const fixture = controllerFixture();
  await fixture.controller.loadInitial(id(300));
  fixture.service.recordObservation = async () => { throw new Error('Could not record this action. Please retry.'); };
  await fixture.controller.refresh();
  assert.equal(fixture.reads.length, 1);
  assert.equal(fixture.states.at(-1).pages[0].records[0].id, id(1));
  assert.match(fixture.states.at(-1).error, /Could not record/);
  fixture.service.recordObservation = async () => { throw Object.assign(new Error('Access denied'), { accessDenied: true }); };
  await fixture.controller.refresh();
  assert.deepEqual(fixture.states.at(-1).pages, []);
  assert.equal(fixture.denied, 1);
});

test('cached newer pages remain stable while subsequent observations add head events; refresh resets paging', async () => {
  const fixture = controllerFixture();
  const pages = [page(10, true), page(5), page(20, true)];
  fixture.service.readPage = async (request) => { fixture.reads.push(request); return pages.shift(); };
  await fixture.controller.loadInitial(id(300));
  await fixture.controller.older();
  assert.deepEqual(fixture.reads[1].cursor, { recordedAt: row(10).recorded_at, id: id(10) });
  await fixture.controller.newer();
  assert.equal(fixture.states.at(-1).pages[0].records[0].id, id(10));
  await fixture.controller.older();
  assert.equal(fixture.reads.length, 2, 'cached navigation does not reread a shifting head');
  assert.equal(fixture.observations.length, 4, 'intentional cached navigation is still recorded');
  await fixture.controller.refresh();
  assert.equal(fixture.states.at(-1).pageIndex, 0);
  assert.equal(fixture.states.at(-1).pages.length, 1);
  assert.equal(fixture.states.at(-1).pages[0].records[0].id, id(20));
});

test('late results cannot overwrite newer filters and observation metadata excludes raw filter values', async () => {
  const fixture = controllerFixture();
  const old = deferred();
  const started = deferred();
  fixture.service.readPage = async ({ filters }) => {
    if (filters.module === 'dashboard') { started.resolve(); return old.promise; }
    return page(2);
  };
  const previous = fixture.controller.applyFilters({ module: 'dashboard' });
  await started.promise;
  await fixture.controller.applyFilters({ module: 'audit-logs', actorId: id(999) });
  old.resolve(page(1));
  await previous;
  assert.equal(fixture.states.at(-1).filters.module, 'audit-logs');
  assert.equal(fixture.states.at(-1).pages[0].records[0].id, id(2));
  assert.deepEqual(fixture.observations[1].details, { page: 1, filter_applied: true });
  assert.equal(JSON.stringify(fixture.observations).includes(id(999)), false);
});

test('record details are opened only after their own view observation is acknowledged', async () => {
  const fixture = controllerFixture();
  await fixture.controller.loadInitial(id(300));
  const ack = deferred();
  fixture.service.recordObservation = (event) => { fixture.observations.push(event); return ack.promise; };
  const pending = fixture.controller.inspect(row(1));
  await fixture.controller.inspect(row(2));
  assert.equal(fixture.observations.length, 2, 'row selection while an action is pending cannot issue another observation');
  assert.equal(fixture.states.at(-1).selected, null);
  ack.resolve(id(500));
  await pending;
  assert.equal(fixture.states.at(-1).selected.id, id(1));
  assert.equal(fixture.observations.at(-1).targetId, id(1));
  assert.equal(fixture.observations.at(-1).eventType, 'audit.viewed');
  fixture.controller.closeDetails();
  assert.equal(fixture.states.at(-1).selected, null);
});

test('disposed viewer controllers ignore pending results and preserve the caller retry ID', async () => {
  const fixture = controllerFixture();
  const ack = deferred();
  fixture.service.recordObservation = (event) => { fixture.observations.push(event); return ack.promise; };
  const pending = fixture.controller.loadInitial(id(300));
  fixture.controller.dispose();
  const count = fixture.states.length;
  ack.resolve(id(500));
  await pending;
  assert.equal(fixture.reads.length, 0);
  assert.equal(fixture.states.length, count);
  assert.equal(fixture.observations[0].operationId, id(300));
});
