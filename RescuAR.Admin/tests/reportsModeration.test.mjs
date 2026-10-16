import assert from 'node:assert/strict';
import test from 'node:test';
import { createReportsModerationService, filterReports, reportMediaUrl } from '../src/services/reportsModerationService.js';
import { createReportsModerationController } from '../src/services/reportsModerationController.js';

const id = (number) => `00000000-0000-4000-8000-${String(number).padStart(12, '0')}`;
const report = (number = 1, status = 'Pending') => ({ id: `report-${number}`, status,
  title: `Report ${number}`, description: 'PRIVATE_REPORT_TEXT', media_url: 'https://example.test/private-media',
  latitude: 14.65, longitude: 121.09 });
const receipt = (request) => ({ report_id: request.reportId, before_status: request.expectedStatus,
  after_status: request.newStatus, outcome: request.expectedStatus === request.newStatus ? 'no_change' : 'succeeded',
  audit_event_id: id(900), recorded_at: '2026-10-07T12:00:00.123456+00:00' });
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
};

function fixture() {
  let sequence = 100;
  let denied = 0;
  const states = [], observations = [], decisions = [], reads = [];
  const service = {
    newOperationId: () => id(++sequence),
    async recordObservation(event) { observations.push(event); return id(800); },
    async readReports() { reads.push(true); return [report(), report(2)]; },
    async moderate(request) { decisions.push(request); return receipt(request); },
  };
  const controller = createReportsModerationController(service, (state) => states.push(state), () => denied++);
  return { service, controller, states, observations, decisions, reads, get denied() { return denied; } };
}

test('report observations use scoped report/module IDs and never include report contents or media URLs', async () => {
  const calls = [];
  const service = createReportsModerationService({ async rpc(name, args) { calls.push({ name, args }); return { data: id(1) }; } });
  await service.recordObservation({ operationId: id(2), eventType: 'record.viewed', targetId: 'report-1', details: {} });
  await service.recordObservation({ operationId: id(3), eventType: 'records.queried', details: { filter_applied: true } });
  assert.deepEqual(calls[0], { name: 'record_web_audit_event', args: {
    p_operation_id: id(2), p_event_type: 'record.viewed', p_module: 'community-reports-moderation',
    p_target_type: 'report', p_target_id: 'report-1', p_details: {},
  } });
  assert.equal(calls[1].args.p_target_type, 'module');
  assert.equal(calls[1].args.p_target_id, 'community-reports-moderation');
  assert.ok(!JSON.stringify(calls).includes('PRIVATE_REPORT_TEXT'));
  assert.ok(!JSON.stringify(calls).includes('private-media'));
});

test('moderation calls only the guarded RPC with text IDs, expected status and the caller retry UUID', async () => {
  const calls = [];
  const request = { operationId: id(2), reportId: 'report-1', expectedStatus: null, newStatus: 'Approved', displayName: 'PRIVATE_REPORT_TEXT' };
  const service = createReportsModerationService({ async rpc(name, args) { calls.push({ name, args }); return { data: [receipt(request)] }; } });
  assert.deepEqual(await service.moderate(request), receipt(request));
  assert.deepEqual(calls[0], { name: 'admin_moderate_report', args: {
    p_operation_id: id(2), p_report_id: 'report-1', p_expected_status: null, p_new_status: 'Approved',
  } });
  assert.ok(!JSON.stringify(calls).includes('PRIVATE_REPORT_TEXT'));
  await assert.rejects(service.moderate({ ...request, newStatus: 'Pending' }));
  await assert.rejects(service.moderate({ ...request, expectedStatus: undefined }));
  assert.equal(calls.length, 1);
});

test('unconfirmed/malformed receipts stay retryable; stale and denied errors are sanitized', async () => {
  const request = { operationId: id(2), reportId: 'report-1', expectedStatus: 'Pending', newStatus: 'Approved' };
  for (const data of [undefined, [], {}, [receipt(request), receipt(request)],
    [{ ...receipt(request), report_id: 'other' }], [{ ...receipt(request), outcome: 'observed' }],
    [{ ...receipt(request), audit_event_id: 'invalid' }], [{ ...receipt(request), recorded_at: 'invalid' }]]) {
    const service = createReportsModerationService({ async rpc() { return { data }; } });
    await assert.rejects(service.moderate(request), (error) => error.retryable && /confirm/i.test(error.message));
  }
  for (const [code, property] of [['40001', 'stale'], ['42501', 'accessDenied'], ['PGRST301', 'accessDenied']]) {
    const service = createReportsModerationService({ async rpc() { return { error: { code, message: 'PRIVATE_BACKEND_DETAIL' } }; } });
    await assert.rejects(service.moderate(request), (error) => error[property] && !error.retryable && !error.message.includes('PRIVATE'));
  }
  const service = createReportsModerationService({ async rpc() { throw new Error('PRIVATE_TOKEN'); } });
  await assert.rejects(service.moderate(request), (error) => error.retryable && !error.message.includes('PRIVATE'));
});

test('report reads are ordered, validated, and preserve nullable status; local search never becomes audit metadata', async () => {
  const calls = [];
  const client = { from(table) { calls.push(table); return { select(columns) { calls.push(columns); return {
    async order(key, options) { calls.push({ key, options }); return { data: [report(1, null)] }; },
  }; } }; } };
  const service = createReportsModerationService(client);
  assert.equal((await service.readReports())[0].status, null);
  assert.deepEqual(calls, ['community_reports', '*', { key: 'created_at', options: { ascending: false } }]);
  assert.equal(filterReports([report(), report(2)], ' Report 2 ').length, 1);
  assert.equal(reportMediaUrl(report()), 'https://example.test/private-media');
  assert.equal(reportMediaUrl({ media_url: 'javascript:alert(1)' }), null);
});

test('opening waits for acknowledgement, default selection/Realtime writes no extra observations, and disposal preserves the opening retry ID', async () => {
  const f = fixture();
  const ack = deferred();
  f.service.recordObservation = (event) => { f.observations.push(event); return ack.promise; };
  const opening = f.controller.loadInitial(id(50));
  await f.controller.automaticRefresh();
  assert.equal(f.reads.length, 0);
  ack.resolve(id(800));
  await opening;
  assert.equal(f.states.at(-1).selectedId, 'report-1');
  await f.controller.automaticRefresh();
  assert.equal(f.observations.length, 1);
  assert.equal(f.observations[0].operationId, id(50));
  const other = fixture();
  const later = deferred();
  other.service.recordObservation = (event) => { other.observations.push(event); return later.promise; };
  const pending = other.controller.loadInitial(id(50));
  other.controller.dispose();
  const count = other.states.length;
  later.resolve(id(800));
  await pending;
  assert.equal(other.reads.length, 0);
  assert.equal(other.states.length, count);
  assert.equal(other.observations[0].operationId, f.observations[0].operationId);
});

test('selection, applied search, clearing and refresh await acknowledgement and exclude raw searches', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  const ack = deferred();
  f.service.recordObservation = (event) => { f.observations.push(event); return ack.promise; };
  const pending = f.controller.inspect('report-2');
  await f.controller.inspect('report-1');
  assert.equal(f.states.at(-1).selectedId, 'report-1');
  assert.equal(f.observations.length, 2);
  ack.resolve(id(800));
  await pending;
  assert.equal(f.states.at(-1).selectedId, 'report-2');
  f.service.recordObservation = async (event) => { f.observations.push(event); return id(800); };
  await f.controller.applyFilter(' PRIVATE_RESIDENT_SEARCH ');
  assert.equal(f.states.at(-1).query, 'PRIVATE_RESIDENT_SEARCH');
  await f.controller.applyFilter('');
  await f.controller.refresh();
  assert.deepEqual(f.observations.map((event) => event.eventType),
    ['module.opened', 'record.viewed', 'records.queried', 'records.queried', 'module.refreshed']);
  assert.ok(!JSON.stringify(f.observations).includes('PRIVATE_RESIDENT_SEARCH'));
  assert.deepEqual(f.observations[2].details, { filter_applied: true });
});

test('failed observations prevent selection/filter/media/refresh; denial clears records and rechecks access', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  f.service.recordObservation = async () => { throw new Error('Recording failed'); };
  let navigated = 0;
  await f.controller.inspect('report-2');
  await f.controller.applyFilter('Report 2');
  await f.controller.openMedia('report-2', () => navigated++);
  await f.controller.refresh();
  assert.equal(f.states.at(-1).selectedId, 'report-1');
  assert.equal(f.states.at(-1).query, '');
  assert.equal(navigated, 0);
  assert.equal(f.reads.length, 1);
  f.service.recordObservation = async () => { throw Object.assign(new Error('Access denied'), { accessDenied: true }); };
  await f.controller.refresh();
  assert.deepEqual(f.states.at(-1).reports, []);
  assert.equal(f.states.at(-1).selectedId, null);
  assert.equal(f.denied, 1);
});

test('media navigation occurs only after the report observation, with no URL in the event', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  const ack = deferred();
  f.service.recordObservation = (event) => { f.observations.push(event); return ack.promise; };
  let navigated = 0;
  const pending = f.controller.openMedia('report-2', () => navigated++);
  assert.equal(navigated, 0);
  ack.resolve(id(800));
  await pending;
  assert.equal(navigated, 1);
  assert.equal(f.observations.at(-1).eventType, 'report.media_opened');
  assert.deepEqual(f.observations.at(-1).details, {});
});

test('moderation waits for its trusted receipt, blocks repeated clicks and refetches instead of applying a historical status', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  const ack = deferred();
  f.service.moderate = (request) => { f.decisions.push(request); return ack.promise; };
  const pending = f.controller.moderate('Approved');
  await f.controller.moderate('Rejected');
  await f.controller.automaticRefresh();
  assert.equal(f.states.at(-1).reports[0].status, 'Pending');
  assert.equal(f.decisions.length, 1);
  f.service.readReports = async () => [report(1, 'Resolved'), report(2)];
  ack.resolve(receipt(f.decisions[0]));
  await pending;
  assert.equal(f.states.at(-1).reports[0].status, 'Resolved');
  assert.match(f.states.at(-1).notice, /saved and recorded/);
  assert.equal(f.observations.length, 1, 'confirmed mutations and related refetches do not write duplicate browser events');
});

test('uncertain mutation retry keeps the exact operation, target and expected status after Realtime changes', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  f.service.moderate = async (request) => { f.decisions.push(request); throw Object.assign(new Error('Network uncertain'), { retryable: true }); };
  await f.controller.moderate('Approved');
  assert.equal(f.states.at(-1).retryDecision, true);
  await f.controller.inspect('report-2');
  await f.controller.moderate('Rejected');
  f.service.readReports = async () => [report(1, 'Resolved'), report(2)];
  await f.controller.automaticRefresh();
  f.service.moderate = async (request) => { f.decisions.push(request); return receipt(request); };
  await f.controller.retryModeration();
  assert.deepEqual(f.decisions[1], f.decisions[0]);
  assert.equal(f.decisions[1].expectedStatus, 'Pending');
  assert.equal(f.decisions[1].reportId, 'report-1');
  assert.equal(f.states.at(-1).selectedId, 'report-2');
  assert.equal(f.states.at(-1).reports[0].status, 'Resolved');
  assert.equal(f.states.at(-1).retryDecision, false);
});

test('stale decisions refetch before a new operation; confirmed decisions with failed reads never become mutation retries', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  f.service.moderate = async () => { throw Object.assign(new Error('Status changed'), { stale: true, refreshRequired: true, retryable: false }); };
  f.service.readReports = async () => [report(1, 'Approved')];
  await f.controller.moderate('Rejected');
  assert.equal(f.states.at(-1).reports[0].status, 'Approved');
  assert.equal(f.states.at(-1).retryDecision, false);
  f.service.moderate = async (request) => receipt(request);
  f.service.readReports = async () => { throw new Error('Read failed'); };
  await f.controller.moderate('Resolved');
  assert.match(f.states.at(-1).notice, /saved and recorded/);
  assert.equal(f.states.at(-1).error, 'Read failed');
  assert.equal(f.states.at(-1).retryDecision, false);
});

test('late automatic results/errors and disposed mutation responses cannot overwrite current state', async () => {
  const f = fixture();
  await f.controller.loadInitial(id(50));
  const old = deferred();
  f.service.readReports = () => old.promise;
  const pending = f.controller.automaticRefresh();
  f.service.readReports = async () => [report(1, 'Approved')];
  await f.controller.refresh();
  old.reject(Object.assign(new Error('Old denial'), { accessDenied: true }));
  await pending;
  assert.equal(f.states.at(-1).reports[0].status, 'Approved');
  assert.equal(f.denied, 0);
  const ack = deferred();
  f.service.moderate = (request) => { f.decisions.push(request); return ack.promise; };
  const decision = f.controller.moderate('Resolved');
  f.controller.dispose();
  const count = f.states.length;
  ack.resolve(receipt(f.decisions[0]));
  await decision;
  assert.equal(f.states.length, count);
});
