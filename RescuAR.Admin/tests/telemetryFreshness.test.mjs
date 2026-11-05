import test from 'node:test';
import assert from 'node:assert/strict';
import { getTelemetryFreshness } from '../src/utils/telemetryFreshness.js';

const now = Date.parse('2026-11-05T04:30:00Z');

test('freshness reflects the observation time rather than the scrape time', () => {
  assert.equal(getTelemetryFreshness('2026-11-05T04:20:00Z', now).status, 'fresh');
  assert.equal(getTelemetryFreshness('2026-11-05T04:10:00Z', now).status, 'delayed');
  assert.equal(getTelemetryFreshness('2026-11-05T03:50:00Z', now).status, 'stale');
  assert.equal(getTelemetryFreshness('2026-11-05T03:00:00Z', now).status, 'unavailable');
});

test('future-dated records do not appear fresh', () => {
  const state = getTelemetryFreshness('2026-11-05T05:00:00Z', now);
  assert.equal(state.status, 'unavailable');
  assert.equal(state.isUsableAsLive, false);
});
