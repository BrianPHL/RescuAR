import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { readCsv, writeCsv } from '../lib/csv.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SCRAPER_ROOT = path.resolve(__dirname, '..');
const HISTORICAL_ROOT = path.join(SCRAPER_ROOT, 'data', 'historical');

function parseArgs(argv) {
  const args = { event: 'ulysses_2020', labels: null, barangayFeatures: null };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    const next = argv[i + 1];
    if (token === '--event' && next) args.event = next, i += 1;
    else if (token === '--input' && next) args.input = next, i += 1;
    else if (token === '--labels' && next) args.labels = next, i += 1;
    else if (token === '--barangay-features' && next) args.barangayFeatures = next, i += 1;
    else if (token === '--help' || token === '-h') args.help = true;
  }
  return args;
}

function printHelp() {
  console.log(`\nBuild historical model features\n\n` +
    `Usage:\n` +
    `  node scripts/buildTrainingDataset.js --event ulysses_2020 [options]\n\n` +
    `Options:\n` +
    `  --input <csv>               Collected PAGASA water-level CSV\n` +
    `  --labels <csv>              Event/barangay target labels\n` +
    `  --barangay-features <csv>   Optional PREP static barangay feature table\n`);
}

function num(value) {
  if (value === null || value === undefined || value === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function safeDiff(a, b) {
  return Number.isFinite(a) && Number.isFinite(b) ? Number((a - b).toFixed(3)) : null;
}

function nearestEarlier(rows, index, minutes, toleranceMinutes = 15) {
  const currentTime = new Date(rows[index].observed_at).getTime();
  const target = currentTime - minutes * 60000;
  let best = null;
  let bestDiff = Infinity;

  for (let i = index - 1; i >= 0; i -= 1) {
    const candidateTime = new Date(rows[i].observed_at).getTime();
    if (!Number.isFinite(candidateTime)) continue;
    const diff = Math.abs(candidateTime - target);
    if (diff < bestDiff) {
      best = rows[i];
      bestDiff = diff;
    }
    if (candidateTime < target - toleranceMinutes * 60000) break;
  }

  return bestDiff <= toleranceMinutes * 60000 ? best : null;
}

function slug(value) {
  return String(value || '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

function buildHydrologyFeatures(rows) {
  const grouped = new Map();
  for (const row of rows) {
    if (!grouped.has(row.station_code)) grouped.set(row.station_code, []);
    grouped.get(row.station_code).push({
      ...row,
      water_level_m: num(row.water_level_m),
      alert_threshold_m: num(row.alert_threshold_m),
      alarm_threshold_m: num(row.alarm_threshold_m),
      critical_threshold_m: num(row.critical_threshold_m)
    });
  }

  const features = [];
  for (const stationRows of grouped.values()) {
    stationRows.sort((a, b) => new Date(a.observed_at) - new Date(b.observed_at));
    for (let i = 0; i < stationRows.length; i += 1) {
      const row = stationRows[i];
      const lag30 = nearestEarlier(stationRows, i, 30);
      const lag60 = nearestEarlier(stationRows, i, 60);
      const lag120 = nearestEarlier(stationRows, i, 120);
      const delta30 = safeDiff(row.water_level_m, lag30?.water_level_m);
      const delta60 = safeDiff(row.water_level_m, lag60?.water_level_m);
      const delta120 = safeDiff(row.water_level_m, lag120?.water_level_m);

      features.push({
        event_id: row.event_id,
        station_code: row.station_code,
        station_name: row.station_name,
        observed_at: row.observed_at,
        water_level_m: row.water_level_m,
        water_level_30m_m: lag30?.water_level_m ?? null,
        water_level_1h_m: lag60?.water_level_m ?? null,
        water_level_2h_m: lag120?.water_level_m ?? null,
        delta_30m_m: delta30,
        delta_1h_m: delta60,
        delta_2h_m: delta120,
        rise_rate_30m_m_per_hr: Number.isFinite(delta30) ? Number((delta30 * 2).toFixed(3)) : null,
        rise_rate_1h_m_per_hr: delta60,
        alert_threshold_m: row.alert_threshold_m,
        alarm_threshold_m: row.alarm_threshold_m,
        critical_threshold_m: row.critical_threshold_m,
        distance_to_alert_m: Number.isFinite(row.alert_threshold_m) ? safeDiff(row.alert_threshold_m, row.water_level_m) : null,
        distance_to_alarm_m: Number.isFinite(row.alarm_threshold_m) ? safeDiff(row.alarm_threshold_m, row.water_level_m) : null,
        distance_to_critical_m: Number.isFinite(row.critical_threshold_m) ? safeDiff(row.critical_threshold_m, row.water_level_m) : null,
        status: row.status,
        quality_flag: row.quality_flag,
        source: row.source
      });
    }
  }
  return features.sort((a, b) => new Date(a.observed_at) - new Date(b.observed_at));
}

function eventPeakFeatures(features) {
  const grouped = new Map();
  for (const row of features) {
    if (!grouped.has(row.station_code)) grouped.set(row.station_code, []);
    grouped.get(row.station_code).push(row);
  }

  const wide = {};
  for (const [stationCode, rows] of grouped.entries()) {
    const peak = rows.reduce((best, row) => !best || row.water_level_m > best.water_level_m ? row : best, null);
    const maxRise1h = rows.reduce((best, row) => {
      if (!Number.isFinite(row.rise_rate_1h_m_per_hr)) return best;
      return best === null || row.rise_rate_1h_m_per_hr > best ? row.rise_rate_1h_m_per_hr : best;
    }, null);
    const prefix = slug(peak?.station_name || stationCode);
    wide[`${prefix}_station_code`] = stationCode;
    wide[`${prefix}_peak_water_level_m`] = peak?.water_level_m ?? null;
    wide[`${prefix}_peak_observed_at`] = peak?.observed_at ?? null;
    wide[`${prefix}_max_rise_1h_m_per_hr`] = maxRise1h;
    wide[`${prefix}_critical_threshold_m`] = peak?.critical_threshold_m ?? null;
  }
  return wide;
}

function normalizeBarangayName(value) {
  return String(value || '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

function joinBarangayTraining(eventId, peakFeatures, labels, staticFeatures) {
  const staticByName = new Map(
    staticFeatures.map((row) => [normalizeBarangayName(row.barangay_name), row])
  );

  return labels.map((label) => {
    const barangayName = label.barangay_name || label.barangay || '';
    const staticRow = staticByName.get(normalizeBarangayName(barangayName)) || {};
    const floodedRaw = label.flooded ?? label.historical_flooded ?? '';
    const flooded = floodedRaw === '' ? '' : Number(floodedRaw);
    const validTarget = flooded === 0 || flooded === 1;

    return {
      event_id: eventId,
      barangay_name: barangayName,
      flooded: validTarget ? flooded : '',
      severity: label.severity || label.observed_severity || '',
      observed_flood_area_pct: label.observed_flood_area_pct || '',
      label_confidence: label.confidence || label.evidence_confidence || '',
      label_source_url: label.source_url || '',
      use_for_training: validTarget ? 'YES' : 'NO',
      exclusion_reason: validTarget ? '' : 'Target is unknown; never infer not-flooded from absence in reports.',
      ...peakFeatures,
      ...Object.fromEntries(
        Object.entries(staticRow)
          .filter(([key]) => key !== 'barangay_name')
          .map(([key, value]) => [`static_${key}`, value])
      )
    };
  });
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) return printHelp();

  const eventKey = args.event.replace(/[^a-zA-Z0-9_-]+/g, '_');
  const processedDir = path.join(HISTORICAL_ROOT, 'processed');
  const input = args.input
    ? path.resolve(args.input)
    : path.join(processedDir, `${eventKey}_water_levels.csv`);

  if (!fs.existsSync(input)) {
    throw new Error(`Historical water-level input not found: ${input}. Run collectHistoricalPagasa.js first.`);
  }

  const rawRows = readCsv(input);
  if (!rawRows.length) throw new Error(`No historical rows found in ${input}.`);
  fs.mkdirSync(processedDir, { recursive: true });

  const features = buildHydrologyFeatures(rawRows);
  const hydrologyPath = path.join(processedDir, `${eventKey}_hydrology_features.csv`);
  writeCsv(hydrologyPath, features);

  const peakFeatures = eventPeakFeatures(features);
  const eventPeakPath = path.join(processedDir, `${eventKey}_event_peak_features.csv`);
  writeCsv(eventPeakPath, [{ event_id: eventKey, ...peakFeatures }]);

  const defaultLabels = path.join(HISTORICAL_ROOT, 'labels', `${eventKey}_barangay_labels_PROVISIONAL.csv`);
  const labelsPath = args.labels ? path.resolve(args.labels) : (fs.existsSync(defaultLabels) ? defaultLabels : null);
  const staticPath = args.barangayFeatures ? path.resolve(args.barangayFeatures) : null;

  if (labelsPath && fs.existsSync(labelsPath)) {
    const labels = readCsv(labelsPath);
    const staticFeatures = staticPath && fs.existsSync(staticPath) ? readCsv(staticPath) : [];
    const candidates = joinBarangayTraining(eventKey, peakFeatures, labels, staticFeatures);
    const candidatePath = path.join(processedDir, `${eventKey}_barangay_training_candidates.csv`);
    writeCsv(candidatePath, candidates);

    const labeled = candidates.filter((row) => row.use_for_training === 'YES');
    const labeledPath = path.join(processedDir, `${eventKey}_barangay_training_labeled.csv`);
    writeCsv(labeledPath, labeled, candidates[0] ? Object.keys(candidates[0]) : []);

    const classes = new Set(labeled.map((row) => String(row.flooded)));
    console.log(`[Training] Barangay candidates: ${candidates.length}; labeled rows: ${labeled.length}; target classes: ${[...classes].join(', ') || 'none'}.`);
    if (classes.size < 2) {
      console.warn('[Training] NOT READY for a binary classifier: the labeled data do not contain both flooded=1 and flooded=0 classes. Do not convert unknown barangays into negatives.');
    }
    console.warn('[Training] One historical event is not enough for a defensible train/test accuracy claim. Add multiple independent flood events and validate by event, not by random timestamp rows.');
  } else {
    console.warn('[Training] No barangay label file found. Hydrology features were created, but no classification target dataset was produced.');
  }

  console.log(`[Training] Hydrology features: ${hydrologyPath}`);
  console.log(`[Training] Event peak features: ${eventPeakPath}`);
}

try {
  main();
} catch (error) {
  console.error('[Training] Build failed:', error.message);
  process.exitCode = 1;
}
