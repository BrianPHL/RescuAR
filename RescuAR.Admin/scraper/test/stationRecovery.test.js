import test from 'node:test';
import assert from 'node:assert/strict';
import {
  detectStationSwitches, normalizeSourceStationName,
  verifyStationSwitchData, isRecentStationObservation, deriveTrendFromHistory
} from '../lib/stationRecovery.js';

const active = [
  { station_code: '11104201', source_station_name: 'Sto.Nino(2nd)', display_name: 'Sto. Niño (2nd) Station', enabled: true, is_primary: true },
  { station_code: '11103203', source_station_name: 'Tumana Bridge', display_name: 'Tumana Station', enabled: true }
];
const catalog = [
  ...active,
  { station_code: '11104204', source_station_name: 'Sto Nino(MMDA)', display_name: 'Sto. Niño Station', enabled: false }
];
const checkedAt = '2026-11-05T04:30:00.000Z';
const reading = { wl: '15.30', alertwl: '14.0', alarmwl: '15.0', criticalwl: '16.0' };
const detail = [{ timestr: '2026-11-05 12:20', wl: '15.30', alertwl: '14', alarmwl: '15', criticalwl: '16' }];

test('normalizes punctuation and accent differences but not different gauge suffixes', () => {
  assert.equal(normalizeSourceStationName('Sto. Niño 2nd'), 'sto nino 2nd');
  assert.notEqual(normalizeSourceStationName('Sto Nino (MMDA)'), normalizeSourceStationName('Sto.Nino (2nd)'));
});

test('recognizes preverified disabled Sto. Niño alternate, retaining its own identity', () => {
  const result = detectStationSwitches([
    { obscd: '11104204', obsnm: 'Sto Nino(MMDA)', ...reading },
    { obscd: '11103203', obsnm: 'Tumana Bridge', ...reading }
  ], active, catalog);
  assert.equal(result.candidates.length, 1);
  assert.equal(result.candidates[0].mode, 'alternate');
  assert.equal(result.candidates[0].oldStation.station_code, '11104201');
  assert.equal(result.candidates[0].newStation.station_code, '11104204');
  assert.equal(result.candidates[0].newStation.display_name, 'Sto. Niño Station');
});

test('discovers an exact-name new code but refuses same-looking names', () => {
  const result = detectStationSwitches([
    { obscd: '99990001', obsnm: 'Sto.Nino(2nd)', ...reading },
    { obscd: '99990002', obsnm: 'Tumana Upper Bridge', ...reading }
  ], active, catalog);
  assert.equal(result.candidates.length, 1);
  assert.equal(result.candidates[0].mode, 'code_change');
  assert.equal(result.candidates[0].newStation.station_code, '99990001');
});

test('rejects ambiguous candidates, malformed codes and source-name mismatch', () => {
  const result = detectStationSwitches([
    { obscd: '99990001', obsnm: 'Sto.Nino(2nd)' },
    { obscd: '99990002', obsnm: 'Sto.Nino(2nd)' },
    { obscd: '9x', obsnm: 'Tumana Bridge' }
  ], active, catalog);
  assert.equal(result.candidates.length, 0);
  assert.equal(result.ambiguous.length, 1);
});

test('rejects duplicate candidate code rows even if the names match', () => {
  const result = detectStationSwitches([
    { obscd: '99990001', obsnm: 'Sto.Nino(2nd)' },
    { obscd: '99990001', obsnm: 'Sto.Nino(2nd)' }
  ], active, catalog);
  assert.equal(result.candidates.length, 0);
});

test('requires recent valid detail observations and thresholds before switching', () => {
  assert.equal(verifyStationSwitchData(reading, detail, checkedAt), true);
  assert.equal(verifyStationSwitchData(reading, [{ ...detail[0], timestr: '2026-11-05 09:00' }], checkedAt), false);
  assert.equal(verifyStationSwitchData({ ...reading, criticalwl: null }, [{ ...detail[0], criticalwl: null }], checkedAt), false);
  assert.equal(verifyStationSwitchData(reading, [{ ...detail[0], timestr: '2026-11-05 13:00' }], checkedAt), false);
  assert.equal(verifyStationSwitchData({ ...reading, wl: null }, detail, checkedAt), false);
  assert.equal(verifyStationSwitchData({ ...reading, alertwl: '20' }, [{ ...detail[0], alertwl: '20' }], checkedAt), false);
});

test('does not call old readings live', () => {
  assert.equal(isRecentStationObservation('2026-11-05T04:20:00Z', checkedAt), true);
  assert.equal(isRecentStationObservation('2026-11-05T03:00:00Z', checkedAt), false);
  assert.equal(isRecentStationObservation('2026-11-05T05:00:00Z', checkedAt), false);
});

test('derives real 30-minute trends and leaves gaps unknown', () => {
  const history = [
    { observed_at: '2026-11-05T03:50:00Z', level: 14.80 },
    { observed_at: '2026-11-05T04:20:00Z', level: 15.30 }
  ];
  assert.deepEqual(deriveTrendFromHistory(history, history[1]), {
    trend: 'Rising', delta_30m_m: 0.5, water_level_30m_m: 14.80
  });
  assert.equal(deriveTrendFromHistory([history[1]], history[1]), null);
});
