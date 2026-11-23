import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { buildSafeAdvisoryDraft } from '../src/utils/advisorySafety.js';

const read = (relative) => fs.readFileSync(new URL(`../${relative}`, import.meta.url), 'utf8');

test('missing observation never becomes fabricated warning, flood location or evacuation', () => {
  const result = buildSafeAdvisoryDraft(null);
  assert.match(result.message, /Unavailable/);
  assert.match(result.message, /NOT AN OFFICIAL/);
  assert.doesNotMatch(result.message, /16\.2 m|mandatory evacuation is in effect|Directly Affected/);
  assert.equal(result.affectedAreas, '');
});

test('unverified or stale readings are not classified as official risk', () => {
  const stale = buildSafeAdvisoryDraft({ stationName: 'Sto. Niño', level: 18.2, alertStatus: 'Critical', observedAt: '2026-10-01T00:00:00Z', thresholdVerified: true }, Date.parse('2026-11-23T00:00:00Z'));
  assert.match(stale.message, /Threshold-based assessment: Not assessed/);
  const unverified = buildSafeAdvisoryDraft({ level: 18.2, alertStatus: 'Critical', observedAt: '2026-11-23T00:00:00Z' }, Date.parse('2026-11-23T00:05:00Z'));
  assert.match(unverified.message, /Threshold-based assessment: Not assessed/);
});

test('verified current reading is presented only as a reviewable draft', () => {
  const result = buildSafeAdvisoryDraft({ stationName: 'Nangka', level: 16, alertStatus: 'Alarm', observedAt: '2026-11-23T00:00:00Z', thresholdVerified: true }, Date.parse('2026-11-23T00:05:00Z'));
  assert.match(result.message, /Threshold-based assessment: Alarm/);
  assert.match(result.message, /INTERNAL DRAFT/);
  assert.equal(result.affectedAreas, '');
});

test('broadcast modal contains no simulation timer or fabricated confirmation', () => {
  const src = read('src/components/Modals.jsx');
  const modal = src.split('/* --- NOTIFY RESIDENTS MODAL --- */')[1].split('/* --- RUN INUNDATION MODAL --- */')[0];
  assert.doesNotMatch(modal, /setInterval|Transmitting alert packets|1,240|sendingState|Broadcast Completed/);
  assert.match(modal, /Broadcasting from this panel is disabled/);
});

test('settings do not expose fake saving, thresholds, endpoint or key', () => {
  const src = read('src/components/SystemSettings.jsx');
  assert.doesNotMatch(src, /Settings Saved!|setSaved|Save Configuration|16\.0|api\.rescuar\.net|••••/);
  assert.match(src, /read-only/);
});

test('advisory creation and duplication default to Draft with explicit manual publish confirmation', () => {
  const src = read('src/components/Advisories.jsx');
  assert.match(src, /status: 'Draft'/);
  assert.match(src, /Publish this advisory publicly/);
  const modal = read('src/components/Modals.jsx');
  assert.match(modal, /status: 'Draft'/);
  assert.doesNotMatch(modal, /mandatory evacuation is in effect/);
  assert.match(src, /payload\.published_at = new Date\(\)\.toISOString\(\)/);
});

test('old inundation demonstration cannot claim fabricated computation', () => {
  const src = read('src/components/Modals.jsx');
  const prediction = src.split('/* --- LEGACY INUNDATION SIMULATION MODAL --- */')[1];
  assert.doesNotMatch(prediction, /setInterval|16\.2m|Simulating flood front velocity|Calculation completed/);
  assert.match(prediction, /No prediction has been calculated/);
});

test('river monitoring never offers unverified direct resident broadcasts', () => {
  const src = read('src/components/RiverLevel.jsx');
  assert.doesNotMatch(src, /notify: \{ enabled: true/);
  assert.match(src, /Resident Broadcast Unavailable/);
});
