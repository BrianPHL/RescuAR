import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { readCsv } from '../lib/csv.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SCRAPER_ROOT = path.resolve(__dirname, '..');
const HISTORICAL_ROOT = path.join(SCRAPER_ROOT, 'data', 'historical');

const ULYSSES_REFERENCE_PEAKS = [
  { match: ['montalban'], reference_level_m: 29.73, reference_time_manila: '2020-11-12 05:00' },
  { match: ['nangka'], reference_level_m: 24.58, reference_time_manila: '2020-11-12 05:50' },
  { match: ['sto nino', 'sto niño'], reference_level_m: 22.01, reference_time_manila: '2020-11-12 10:20' }
];

function parseArgs(argv) {
  const args = { event: 'ulysses_2020' };
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--event' && argv[i + 1]) args.event = argv[++i];
    else if (argv[i] === '--input' && argv[i + 1]) args.input = argv[++i];
  }
  return args;
}

function normalize(value) {
  return String(value || '')
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
}

function num(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function median(values) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

function analyzeStation(rows) {
  const sorted = [...rows].sort((a, b) => new Date(a.observed_at) - new Date(b.observed_at));
  const levels = sorted.map((row) => num(row.water_level_m)).filter(Number.isFinite);
  const gaps = [];
  for (let i = 1; i < sorted.length; i += 1) {
    const diffMin = (new Date(sorted[i].observed_at) - new Date(sorted[i - 1].observed_at)) / 60000;
    if (Number.isFinite(diffMin) && diffMin >= 0) gaps.push(diffMin);
  }
  const peak = sorted.reduce((best, row) => {
    const level = num(row.water_level_m);
    if (!Number.isFinite(level)) return best;
    if (!best || level > best.level) return { level, observed_at: row.observed_at };
    return best;
  }, null);

  return {
    station_code: sorted[0]?.station_code || '',
    station_name: sorted[0]?.station_name || '',
    observations: sorted.length,
    first_observation_at: sorted[0]?.observed_at || null,
    last_observation_at: sorted.at(-1)?.observed_at || null,
    min_level_m: levels.length ? Math.min(...levels) : null,
    peak_level_m: peak?.level ?? null,
    peak_observed_at: peak?.observed_at ?? null,
    median_interval_minutes: median(gaps),
    gaps_over_20_minutes: gaps.filter((gap) => gap > 20).length
  };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const eventKey = args.event.replace(/[^a-zA-Z0-9_-]+/g, '_');
  const input = args.input
    ? path.resolve(args.input)
    : path.join(HISTORICAL_ROOT, 'processed', `${eventKey}_water_levels.csv`);
  if (!fs.existsSync(input)) throw new Error(`Input not found: ${input}`);

  const rows = readCsv(input);
  if (!rows.length) throw new Error('Historical file contains no observations.');

  const duplicateKeys = new Set();
  const seen = new Set();
  let invalidLevels = 0;
  let invalidTimes = 0;
  for (const row of rows) {
    const key = `${row.station_code}|${row.observed_at}`;
    if (seen.has(key)) duplicateKeys.add(key);
    seen.add(key);
    const level = num(row.water_level_m);
    if (!Number.isFinite(level) || level < 0 || level > 100) invalidLevels += 1;
    if (!Number.isFinite(new Date(row.observed_at).getTime())) invalidTimes += 1;
  }

  const grouped = new Map();
  for (const row of rows) {
    if (!grouped.has(row.station_code)) grouped.set(row.station_code, []);
    grouped.get(row.station_code).push(row);
  }
  const stationSummaries = [...grouped.values()].map(analyzeStation);

  const peakChecks = [];
  if (eventKey.toLowerCase().includes('ulysses')) {
    for (const reference of ULYSSES_REFERENCE_PEAKS) {
      const summary = stationSummaries.find((item) => {
        const name = normalize(item.station_name);
        return reference.match.some((needle) => name.includes(normalize(needle)));
      });
      if (!summary) {
        peakChecks.push({ ...reference, station_name: null, observed_peak_m: null, absolute_difference_m: null, result: 'NO_MATCHING_STATION' });
        continue;
      }
      const diff = Math.abs(summary.peak_level_m - reference.reference_level_m);
      peakChecks.push({
        ...reference,
        station_name: summary.station_name,
        observed_peak_m: summary.peak_level_m,
        observed_peak_at: summary.peak_observed_at,
        absolute_difference_m: Number(diff.toFixed(3)),
        result: diff <= 0.15 ? 'WITHIN_0.15M_SANITY_TOLERANCE' : 'REVIEW_REQUIRED'
      });
    }
  }

  const report = {
    event_id: eventKey,
    generated_at: new Date().toISOString(),
    input,
    total_rows: rows.length,
    unique_station_timestamp_rows: seen.size,
    duplicate_station_timestamp_keys: duplicateKeys.size,
    invalid_water_levels: invalidLevels,
    invalid_timestamps: invalidTimes,
    station_summaries: stationSummaries,
    documented_peak_sanity_checks: peakChecks,
    interpretation: [
      'This validates collected sensor-data integrity and documented event peaks; it is not an inundation-model accuracy score.',
      'A final inundation/risk model still requires observed flood labels/extent and independent-event validation.'
    ]
  };

  const validationDir = path.join(HISTORICAL_ROOT, 'validation');
  fs.mkdirSync(validationDir, { recursive: true });
  const jsonPath = path.join(validationDir, `${eventKey}_validation_report.json`);
  const mdPath = path.join(validationDir, `${eventKey}_validation_report.md`);
  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2));

  const md = [
    `# Historical PAGASA Validation — ${eventKey}`,
    '',
    `- Rows: **${rows.length}**`,
    `- Duplicate station/timestamp keys: **${duplicateKeys.size}**`,
    `- Invalid water levels: **${invalidLevels}**`,
    `- Invalid timestamps: **${invalidTimes}**`,
    '',
    '## Station summaries',
    '',
    '| Station | Observations | Peak (m) | Peak time | Median interval (min) | >20 min gaps |',
    '|---|---:|---:|---|---:|---:|',
    ...stationSummaries.map((row) => `| ${row.station_name} | ${row.observations} | ${row.peak_level_m ?? ''} | ${row.peak_observed_at ?? ''} | ${row.median_interval_minutes ?? ''} | ${row.gaps_over_20_minutes} |`),
    '',
    '## Documented peak sanity checks',
    '',
    '| Station | Reference peak (m) | Collected peak (m) | Difference (m) | Result |',
    '|---|---:|---:|---:|---|',
    ...peakChecks.map((row) => `| ${row.station_name || row.match.join('/')} | ${row.reference_level_m} | ${row.observed_peak_m ?? ''} | ${row.absolute_difference_m ?? ''} | ${row.result} |`),
    '',
    '> These checks validate the historical sensor collection, not the accuracy of the flood-inundation model.'
  ].join('\n');
  fs.writeFileSync(mdPath, `${md}\n`);

  console.log(`[Validation] Report: ${jsonPath}`);
  console.log(`[Validation] Markdown: ${mdPath}`);
  console.table(stationSummaries);
  if (duplicateKeys.size || invalidLevels || invalidTimes) process.exitCode = 2;
}

try {
  main();
} catch (error) {
  console.error('[Validation] Failed:', error.message);
  process.exitCode = 1;
}
