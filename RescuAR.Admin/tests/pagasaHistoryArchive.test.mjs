import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const ui = fs.readFileSync(new URL('../src/components/RiverLevel.jsx', import.meta.url), 'utf8');
const backfill = fs.readFileSync(new URL('../scraper/scripts/backfillToSupabase.js', import.meta.url), 'utf8');

test('historic Sto. Niño gauges remain separate IDs', () => {
  assert.match(ui, /'11104201': 'Sto\. Niño \(2nd\)/);
  assert.match(ui, /'11104204': 'Sto\. Niño \(MMDA\)/);
  assert.match(ui, /fetchHistory\(historyCode, range\)/);
  assert.doesNotMatch(ui, /station_code\s*=\s*'11104204'.*station_code\s*=\s*'11104201'/);
});

test('archived gauge cannot inherit the live gauge warning thresholds', () => {
  assert.match(ui, /isHistoricalGauge \? \[\] :/);
  assert.match(ui, /!isHistoricalGauge && Number\.isFinite\(thresholds\.ALARM_1\)/);
  assert.match(ui, /!isHistoricalGauge && Number\.isFinite\(thresholds\.ALARM_2\)/);
  assert.match(ui, /!isHistoricalGauge && Number\.isFinite\(thresholds\.ALARM_3\)/);
});

test('backfill permits specified known gauges only', () => {
  assert.match(backfill, /'sto_nino_mmda', 'sto_nino_secondary'/);
  assert.match(backfill, /!ALLOWED_STATIONS\.has\(station\)/);
  assert.match(backfill, /'--stations', opts\.stations/);
});
