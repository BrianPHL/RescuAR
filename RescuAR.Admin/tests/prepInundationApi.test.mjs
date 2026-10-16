import assert from 'node:assert/strict';
import { test } from 'node:test';
import { predictPrepInundation } from '../src/services/prepInundationApi.js';

const result = {
  alarm: { code: 'ALARM_2', level: 2, label: 'Preliminary model alarm' },
  inundation: { has_triggered_rule: true, affected_barangays: ['Example'], triggered_thresholds: [16], evidence_status: 'unvalidated' },
  exposure: { total_population_affected: 123 },
  model: { evidence_status: 'preliminary' },
};
const input = { riverLevel: 16, mode: 'simulation', inputSource: 'MANUAL_SIMULATION', endpoint: 'https://model.example/predict' };

test('forwards the frontend contract and preserves external calculations and evidence', async () => {
  const output = await predictPrepInundation({ ...input, fetchImpl: async (url, options) => {
    assert.equal(url, input.endpoint);
    assert.equal(options.method, 'POST');
    assert.deepEqual(JSON.parse(options.body), { riverLevel: 16, mode: 'simulation', inputSource: 'MANUAL_SIMULATION', observedAt: null });
    return { ok: true, json: async () => result };
  } });
  assert.equal(output, result);
});

test('requires endpoint configuration and valid observed inputs before contacting the model', async () => {
  const fetchImpl = () => assert.fail('invalid input should not reach the model');
  await assert.rejects(predictPrepInundation({ ...input, endpoint: '', fetchImpl }), /Configure/);
  await assert.rejects(predictPrepInundation({ ...input, riverLevel: Number.NaN, fetchImpl }), /river level/);
  await assert.rejects(predictPrepInundation({ ...input, mode: 'live', fetchImpl }), /timestamp/);
});

test('server failures and malformed results stay unavailable without local estimates', async () => {
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl: async () => ({ ok: false, status: 503 }) }), /HTTP 503/);
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl: async () => ({ ok: true, json: async () => ({}) }) }), /contract/);
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl: async () => ({ ok: true, json: async () => ({ ...result, exposure: { total_population_affected: -1 } }) }) }), /population/);
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl: async () => ({ ok: true, json: async () => ({ ...result, alarm: { ...result.alarm, code: 'NORMAL' } }) }) }), /inconsistent/);
});

test('honors caller cancellation and timeout', async () => {
  const fetchImpl = async (_, { signal }) => {
    if (signal.aborted) throw new DOMException('Cancelled', 'AbortError');
    return new Promise((_, reject) => signal.addEventListener('abort', () => reject(new DOMException('Cancelled', 'AbortError')), { once: true }));
  };
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl, signal: controller.signal }), { name: 'AbortError' });
  await assert.rejects(predictPrepInundation({ ...input, fetchImpl, timeoutMs: 5 }), /timed out/);
});
