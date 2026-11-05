import test from 'node:test';
import assert from 'node:assert/strict';
import { matchConfiguredStations, isUsableNewObservation, hasCompleteThresholds } from '../lib/stationSelection.js';

const configured = [
  { station_code: '11104204', display_name: 'Sto. Niño Station' },
  { station_code: '11102202', display_name: 'Montalban Station' },
  { station_code: '11103202', display_name: 'Nangka Station' },
  { station_code: '11102201', display_name: 'Rodriguez Station' },
  { station_code: '11103203', display_name: 'Tumana Station' }
];

test('detects Sto. Niño absent from returned list without altering its code', () => {
  const rows = configured.slice(1).map(s => ({ obscd: s.station_code, obsnm: s.display_name }));
  const result = matchConfiguredStations(rows, configured);
  assert.equal(result.selected.length, 4);
  assert.deepEqual(result.missing.map(s => s.station_code), ['11104204']);
});

test('normalizes whitespace but never guesses codes from similar names', () => {
  const result = matchConfiguredStations([
    { obscd: ' 11104204 ', obsnm: 'Renamed station' },
    { obscd: '98765432', obsnm: 'Sto. Niño Station' }
  ], configured);
  assert.equal(result.selected[0].station.station_code, '11104204');
  assert.equal(result.unexpected[0].code, '98765432');
});

test('rejects duplicate station rows', () => {
  const result = matchConfiguredStations([
    { obscd: '11104204' }, { obscd: '11104204' }
  ], configured);
  assert.equal(result.selected.length, 1);
  assert.deepEqual(result.duplicates, ['11104204']);
});

test('requires newer historical observation and rejects future timestamps', () => {
  const checkedAt = '2026-11-05T04:30:00.000Z';
  assert.equal(isUsableNewObservation('2026-11-05T04:20:00.000Z', '2026-11-05T04:10:00.000Z', checkedAt), true);
  assert.equal(isUsableNewObservation('2026-11-05T04:10:00.000Z', '2026-11-05T04:10:00.000Z', checkedAt), false);
  assert.equal(isUsableNewObservation('2026-11-05T04:40:00.000Z', null, checkedAt), false);
  assert.equal(isUsableNewObservation('not a date', null, checkedAt), false);
});

test('only classifies recovered readings using complete thresholds', () => {
  assert.equal(hasCompleteThresholds({ alert_threshold_m: 14, alarm_threshold_m: 15, critical_threshold_m: 16 }), true);
  assert.equal(hasCompleteThresholds({ alert_threshold_m: 14, alarm_threshold_m: null, critical_threshold_m: 16 }), false);
});
