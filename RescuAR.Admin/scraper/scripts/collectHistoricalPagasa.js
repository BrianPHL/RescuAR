import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  fetchPagasaStationHistory,
  normalizeHistoryRows,
  pagasaTimestampFromYmdhm,
  ymdhmFromDateInput
} from '../lib/pagasaClient.js';
import {
  HISTORICAL_PAGASA_STATIONS,
  findPagasaStation
} from '../config/pagasaStations.js';
import { writeCsv } from '../lib/csv.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SCRAPER_ROOT = path.resolve(__dirname, '..');
const HISTORICAL_ROOT = path.join(SCRAPER_ROOT, 'data', 'historical');

const DEFAULTS = {
  event: 'ulysses_2020',
  start: '2020-11-11 00:00',
  end: '2020-11-13 23:59',
  stepHours: 12,
  delayMs: 350,
  stations: 'all'
};

function parseArgs(argv) {
  const args = { ...DEFAULTS };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    const next = argv[i + 1];
    if (token === '--event' && next) args.event = next, i += 1;
    else if (token === '--start' && next) args.start = next, i += 1;
    else if (token === '--end' && next) args.end = next, i += 1;
    else if (token === '--step-hours' && next) args.stepHours = Number(next), i += 1;
    else if (token === '--delay-ms' && next) args.delayMs = Number(next), i += 1;
    else if (token === '--stations' && next) args.stations = next, i += 1;
    else if (token === '--help' || token === '-h') args.help = true;
  }
  return args;
}

function printHelp() {
  console.log(`\nHistorical PAGASA collector\n\n` +
    `Usage:\n` +
    `  node scripts/collectHistoricalPagasa.js [options]\n\n` +
    `Options:\n` +
    `  --event <name>          Output event identifier (default: ulysses_2020)\n` +
    `  --start <date>          Manila time, e.g. "2020-11-11 00:00"\n` +
    `  --end <date>            Manila time, e.g. "2020-11-13 23:59"\n` +
    `  --stations <list>       all or comma-separated station keys/codes\n` +
    `  --step-hours <n>        Query spacing (default: 12)\n` +
    `  --delay-ms <n>          Delay between source calls (default: 350)\n` +
    `\nExamples:\n` +
    `  npm run collect:ulysses\n` +
    `  npm run collect:historical -- --start "2024-07-24 00:00" --end "2024-07-25 00:00" --event carina_2024\n`);
}

function manilaDateFromInput(value) {
  const ymdhm = ymdhmFromDateInput(value);
  const display = pagasaTimestampFromYmdhm(ymdhm);
  if (!ymdhm || !display) return null;
  return new Date(`${display.replace(' ', 'T')}:00+08:00`);
}

function ymdhmFromDateManila(date) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Manila',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).formatToParts(date);
  const map = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${map.year}${map.month}${map.day}${map.hour}${map.minute}`;
}

function selectStations(value) {
  if (!value || value.toLowerCase() === 'all') return HISTORICAL_PAGASA_STATIONS;
  const requested = value.split(',').map((item) => item.trim()).filter(Boolean);
  const selected = requested.map(findPagasaStation).filter(Boolean);
  if (selected.length !== requested.length) {
    const found = new Set(selected.map((station) => station.key));
    const missing = requested.filter((item) => !findPagasaStation(item));
    throw new Error(`Unknown station identifier(s): ${missing.join(', ')}. Matched: ${[...found].join(', ') || 'none'}.`);
  }
  return selected;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, Math.max(0, ms)));
}

function withinRange(iso, startMs, endMs) {
  const ms = new Date(iso).getTime();
  return Number.isFinite(ms) && ms >= startMs && ms <= endMs;
}

function summarize(rows, stations, event, start, end) {
  const stationSummaries = stations.map((station) => {
    const stationRows = rows.filter((row) => row.station_code === station.station_code);
    const levels = stationRows.map((row) => row.level).filter(Number.isFinite);
    const peak = stationRows.reduce((best, row) => {
      if (!best || row.level > best.level) return row;
      return best;
    }, null);
    return {
      station_code: station.station_code,
      station_name: station.display_name,
      observations: stationRows.length,
      first_observation_at: stationRows[0]?.observed_at || null,
      last_observation_at: stationRows.at(-1)?.observed_at || null,
      min_water_level_m: levels.length ? Math.min(...levels) : null,
      max_water_level_m: peak?.level ?? null,
      peak_observed_at: peak?.observed_at ?? null
    };
  });

  return {
    event,
    requested_start_manila: start,
    requested_end_manila: end,
    collected_at: new Date().toISOString(),
    source: 'PAGASA_FFWWS /water/detail_list.do',
    total_observations: rows.length,
    stations: stationSummaries
  };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) return printHelp();

  if (!Number.isFinite(args.stepHours) || args.stepHours <= 0 || args.stepHours > 24) {
    throw new Error('--step-hours must be greater than 0 and no more than 24.');
  }
  if (!Number.isFinite(args.delayMs) || args.delayMs < 0) {
    throw new Error('--delay-ms must be zero or greater.');
  }

  const startDate = manilaDateFromInput(args.start);
  const endDate = manilaDateFromInput(args.end);
  if (!startDate || !endDate || endDate < startDate) {
    throw new Error('Invalid --start/--end range. Use Manila time such as "2020-11-11 00:00".');
  }

  const durationDays = (endDate - startDate) / 86400000;
  if (durationDays > 31) {
    throw new Error('For source-friendly collection, one run is limited to 31 days. Split longer archives into monthly/event batches.');
  }

  const stations = selectStations(args.stations);
  const eventKey = args.event.replace(/[^a-zA-Z0-9_-]+/g, '_');
  const rawDir = path.join(HISTORICAL_ROOT, 'raw', eventKey);
  const processedDir = path.join(HISTORICAL_ROOT, 'processed');
  fs.mkdirSync(rawDir, { recursive: true });
  fs.mkdirSync(processedDir, { recursive: true });

  const queryTimes = [];
  for (let cursor = new Date(startDate); cursor <= endDate; cursor = new Date(cursor.getTime() + args.stepHours * 3600000)) {
    queryTimes.push(ymdhmFromDateManila(cursor));
  }
  // Ensure the requested end is queried even if it does not fall exactly on a step boundary.
  const endYmdhm = ymdhmFromDateManila(endDate);
  if (queryTimes.at(-1) !== endYmdhm) queryTimes.push(endYmdhm);

  console.log(`[Historical] Event: ${eventKey}`);
  console.log(`[Historical] Range: ${args.start} -> ${args.end} Asia/Manila`);
  console.log(`[Historical] Stations: ${stations.map((station) => `${station.display_name} (${station.station_code})`).join(', ')}`);
  console.log(`[Historical] Query windows per station: ${queryTimes.length}`);

  const deduped = new Map();
  let requests = 0;

  for (const station of stations) {
    for (const ymdhm of queryTimes) {
      const rawRows = await fetchPagasaStationHistory(station.station_code, ymdhm);
      requests += 1;

      const rawPath = path.join(rawDir, `${station.station_code}_${ymdhm}.json`);
      fs.writeFileSync(rawPath, JSON.stringify({
        event: eventKey,
        station,
        requested_ymdhm: ymdhm,
        requested_time_manila: pagasaTimestampFromYmdhm(ymdhm),
        retrieved_at: new Date().toISOString(),
        endpoint: '/water/detail_list.do',
        rows: rawRows
      }, null, 2));

      const normalized = normalizeHistoryRows(rawRows, station);
      for (const row of normalized) {
        if (!withinRange(row.observed_at, startDate.getTime(), endDate.getTime())) continue;
        deduped.set(`${row.station_code}|${row.observed_at}`, {
          event_id: eventKey,
          ...row
        });
      }

      console.log(`[Historical] ${station.display_name} ${ymdhhmLabel(ymdhm)}: ${rawRows.length} raw / ${normalized.length} valid row(s).`);
      await sleep(args.delayMs);
    }
  }

  const rows = [...deduped.values()].sort((a, b) => {
    if (a.station_code !== b.station_code) return a.station_code.localeCompare(b.station_code);
    return new Date(a.observed_at) - new Date(b.observed_at);
  });

  const csvRows = rows.map((row) => ({
    event_id: row.event_id,
    station_code: row.station_code,
    station_name: row.station_name,
    source_station_name: row.source_station_name,
    observed_at: row.observed_at,
    water_level_m: row.level,
    raw_water_level: row.raw_water_level,
    quality_flag: row.quality_flag,
    alert_threshold_m: row.alert_threshold_m,
    alarm_threshold_m: row.alarm_threshold_m,
    critical_threshold_m: row.critical_threshold_m,
    status: row.status,
    source: row.source
  }));

  const csvPath = path.join(processedDir, `${eventKey}_water_levels.csv`);
  const jsonPath = path.join(processedDir, `${eventKey}_water_levels.json`);
  const summaryPath = path.join(processedDir, `${eventKey}_collection_summary.json`);
  writeCsv(csvPath, csvRows);
  fs.writeFileSync(jsonPath, JSON.stringify(csvRows, null, 2));
  fs.writeFileSync(summaryPath, JSON.stringify({
    ...summarize(rows, stations, eventKey, args.start, args.end),
    source_requests: requests,
    raw_directory: path.relative(SCRAPER_ROOT, rawDir),
    processed_csv: path.relative(SCRAPER_ROOT, csvPath)
  }, null, 2));

  console.log(`[Historical] Complete. ${rows.length} unique observation(s) saved.`);
  console.log(`[Historical] CSV: ${csvPath}`);
  console.log(`[Historical] Summary: ${summaryPath}`);
  if (!rows.length) {
    console.warn('[Historical] The endpoint returned no valid observations inside the requested range. This can mean the archive does not retain that date/station or the historical station code differs.');
  }
}

function ymdhhmLabel(ymdhm) {
  return pagasaTimestampFromYmdhm(ymdhm) || ymdhm;
}

main().catch((error) => {
  console.error('[Historical] Collection failed:', error.message);
  process.exitCode = 1;
});
