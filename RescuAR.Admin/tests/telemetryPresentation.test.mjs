import assert from 'node:assert/strict';
import test from 'node:test';
import { getTelemetryFreshness } from '../src/utils/telemetryFreshness.js';
import {
  getTelemetryConnectionState,
  getMonitoringLevelAssessment,
  getMonitoringTrend,
  hasRecordedStationThresholds,
  selectActiveStoNinoGauge
} from '../src/utils/telemetryPresentation.js';
import { deriveProvisionalExposureScenario } from '../src/utils/provisionalExposureScenario.js';

const now = Date.parse('2026-11-23T07:00:00Z');
const thresholds = { alert_threshold_m: 15, alarm_threshold_m: 16, critical_threshold_m: 17 };
const normal = { status: 'Normal', label: 'Normal Level', color: '#16a34a' };
const critical = { status: 'Critical', label: 'Critical Level', color: '#dc2626' };

for (const [mins, status, label, live] of [
  [10, 'fresh', 'Updated', true],
  [16, 'delayed', 'Awaiting PAGASA Update', false],
  [40, 'stale', 'Outdated Reading', false],
  [61, 'unavailable', 'Data Unavailable', false]
]) {
  test(`observation age ${mins} minutes is ${label} without relaxing risk guard`, () => {
    const reading = getTelemetryFreshness(new Date(now - mins * 60000).toISOString(), now);
    assert.equal(reading.status, status);
    assert.equal(reading.label, label);
    assert.equal(reading.isUsableAsLive, live);
  });
}

test('source connection and measurement freshness are independent', () => {
  const state = getTelemetryConnectionState({
    source_status: 'reachable', worker_status: 'healthy', last_check_at: new Date(now - 60000).toISOString()
  }, now);
  assert.equal(state.sourceLabel, 'Connected');
  assert.equal(state.workerLabel, 'Reporting Normally');
  assert.equal(getTelemetryFreshness(new Date(now - 16 * 60000), now).label, 'Awaiting PAGASA Update');
});

test('worker last-check age prevents a stale healthy flag being called connected', () => {
  const state = getTelemetryConnectionState({
    source_status: 'reachable', worker_status: 'healthy', last_check_at: new Date(now - 3600000).toISOString()
  }, now);
  assert.equal(state.sourceConnected, false);
  assert.equal(state.sourceLabel, 'Connection Not Confirmed');
  assert.equal(state.workerReporting, false);
});

test('reported outage is a connection warning, not a flood alert', () => {
  const state = getTelemetryConnectionState({
    source_status: 'failed', worker_status: 'healthy', last_error_message: 'source timeout', last_check_at: new Date(now - 2000).toISOString()
  }, now);
  assert.equal(state.sourceLabel, 'Connection Issue');
  assert.match(state.detail, /timeout/);
});

test('threshold verification rejects missing, misordered, and invalid thresholds', () => {
  assert.equal(hasRecordedStationThresholds(thresholds), true);
  assert.equal(hasRecordedStationThresholds({ ...thresholds, alert_threshold_m: null }), false);
  assert.equal(hasRecordedStationThresholds({ ...thresholds, critical_threshold_m: 14 }), false);
  assert.equal(hasRecordedStationThresholds({ ...thresholds, critical_threshold_m: 'not-set' }), false);
});

test('unverified thresholds do not appear as Normal despite recorded elevation', () => {
  const state = getMonitoringLevelAssessment(12.09, getTelemetryFreshness(new Date(now - 60000), now), false, normal);
  assert.equal(state.status, 'Unverified');
  assert.equal(state.label, 'Thresholds Not Verified');
});

test('an old critical measurement is historical, not an active emergency classification', () => {
  const state = getMonitoringLevelAssessment(18, getTelemetryFreshness(new Date(now - 20 * 60000), now), true, critical);
  assert.equal(state.status, 'Unavailable');
  assert.equal(state.label, 'Risk Not Assessed');
});

test('live threshold crossings retain their PAGASA Alert/Alarm/Critical labels', () => {
  const state = getMonitoringLevelAssessment(18, getTelemetryFreshness(new Date(now - 60000), now), true, critical);
  assert.equal(state.status, 'Critical');
});

test('unknown or outdated trends never display a misleading zero change', () => {
  assert.equal(getMonitoringTrend('Stable', getTelemetryFreshness(new Date(now - 20 * 60000), now)).available, false);
  assert.equal(getMonitoringTrend('Unknown', getTelemetryFreshness(new Date(now - 60000), now)).label, 'Insufficient Trend Data');
});

test('exposure context uses enabled Sto. Niño gauge rather than archived MMDA', () => {
  const catalog = [
    { station_code: '11104204', enabled: false, is_primary: false },
    { station_code: '11104201', enabled: true, is_primary: true }
  ];
  const rows = [
    { station_code: '11104204', level: 11.88 },
    { station_code: '11104201', level: 12.09 }
  ];
  assert.equal(selectActiveStoNinoGauge(catalog, rows)?.station_code, '11104201');
  assert.equal(selectActiveStoNinoGauge([], rows), null);
});

test('automatic exposure scenario will not classify stations with unverified thresholds', () => {
  const result = deriveProvisionalExposureScenario({
    telemetry: { station_name: 'Sto. Niño (2nd) Station', level: 18, ...thresholds, critical_threshold_m: null },
    freshness: getTelemetryFreshness(new Date(now - 60000), now),
    sourceStatus: { source_status: 'reachable', worker_status: 'healthy' }
  });
  assert.equal(result.active, false);
  assert.equal(result.status, 'Unverified');
});
