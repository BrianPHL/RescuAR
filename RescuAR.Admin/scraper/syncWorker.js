import { createClient } from '@supabase/supabase-js';
import axios from 'axios';
import * as cheerio from 'cheerio';
import cron from 'node-cron';
import dotenv from 'dotenv';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Local development loads the parent .env reliably regardless of the shell cwd.
// Railway injects process.env directly, so this is harmless in production.
dotenv.config({ path: path.resolve(__dirname, '../.env') });

const SUPABASE_URL = process.env.SUPABASE_URL;
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY;

if (!SUPABASE_URL || !SUPABASE_SERVICE_ROLE_KEY) {
  throw new Error(
    '[Telemetry] Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY. ' +
    'Set both in the server/worker environment. Never expose the service-role key through VITE_* variables.'
  );
}

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, {
  auth: {
    persistSession: false,
    autoRefreshToken: false
  }
});

console.log('[Telemetry] Server-side Supabase client initialized for latest + historical telemetry persistence.');

const MAX_REASONABLE_LEVEL_METERS = 50;
const PAGASA_WATER_URL =
  'https://pasig-marikina-tullahanffws.pagasa.dost.gov.ph/water/table.do';

const BANTAYBAHA_GAUGES = [
  {
    url: 'https://bantaybaha.com/gauges/1',
    stationName: 'Sto. Niño Station'
  },
  {
    url: 'https://bantaybaha.com/gauges/6',
    stationName: 'Rodriguez Station'
  }
];

const HTTP_HEADERS = {
  'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0 Safari/537.36',
  Accept: 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
  'Accept-Language': 'en-US,en;q=0.9'
};

const STATION_MAP = {
  'sto nino': 'Sto. Niño Station',
  'sto niño': 'Sto. Niño Station',
  nangka: 'Nangka Station',
  rodriguez: 'Rodriguez Station',
  burgos: 'San Jose Station',
  montalban: 'San Jose Station',
  'tumana bridge': 'Tumana Station'
};

function normalizeText(value = '') {
  return String(value)
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

function isValidLevel(value) {
  const level = Number(value);
  return Number.isFinite(level) && level >= 0 && level <= MAX_REASONABLE_LEVEL_METERS;
}

function calculateStatus(stationName, level) {
  const numLevel = Number(level) || 0;
  const name = normalizeText(stationName);
  let thresholds = { alarm1: 15.0, alarm2: 16.0, alarm3: 18.0 };

  if (name.includes('rodriguez')) thresholds = { alarm1: 28.8, alarm2: 29.8, alarm3: 30.7 };
  else if (name.includes('nangka')) thresholds = { alarm1: 16.5, alarm2: 17.1, alarm3: 17.7 };
  else if (name.includes('san jose') || name.includes('montalban') || name.includes('burgos')) {
    thresholds = { alarm1: 22.4, alarm2: 23.0, alarm3: 23.6 };
  }

  if (numLevel >= thresholds.alarm3) return '3rd Alarm';
  if (numLevel >= thresholds.alarm2) return '2nd Alarm';
  if (numLevel >= thresholds.alarm1) return '1st Alarm';
  return 'Normal';
}

function mapStationName(rawName) {
  const normalized = normalizeText(rawName);
  for (const [key, mappedName] of Object.entries(STATION_MAP)) {
    if (normalized.includes(normalizeText(key))) return mappedName;
  }
  return null;
}

function manilaDateParts(date = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Manila',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  }).formatToParts(date);

  return Object.fromEntries(
    parts
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value])
  );
}

function toManilaIso(year, month, day, hour24, minute) {
  const y = String(year).padStart(4, '0');
  const m = String(month).padStart(2, '0');
  const d = String(day).padStart(2, '0');
  const h = String(hour24).padStart(2, '0');
  const min = String(minute).padStart(2, '0');
  const parsed = new Date(`${y}-${m}-${d}T${h}:${min}:00+08:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

function parseClockOnManilaDate(timeText, date = new Date()) {
  const match = String(timeText).trim().match(/(\d{1,2}):(\d{2})\s*(AM|PM)/i);
  if (!match) return null;

  let hour = Number(match[1]);
  const minute = Number(match[2]);
  const meridiem = match[3].toUpperCase();

  if (hour === 12) hour = 0;
  if (meridiem === 'PM') hour += 12;

  const { year, month, day } = manilaDateParts(date);
  return toManilaIso(year, month, day, hour, minute);
}

function parseBantayBahaTimestamp(label, latestObservedAt = null, now = new Date()) {
  const text = String(label || '').replace(/\s+/g, ' ').trim();
  if (!text) return null;

  // Current rows are typically like "11:00 AM today".
  if (/\btoday\b/i.test(text)) {
    return parseClockOnManilaDate(text, now);
  }

  if (/\byesterday\b/i.test(text)) {
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
    return parseClockOnManilaDate(text, yesterday);
  }

  // Historical rows are usually relative to the latest observation.
  const hoursAgo = text.match(/(\d+(?:\.\d+)?)\s*hours?\s*ago/i);
  if (hoursAgo) {
    const base = latestObservedAt ? new Date(latestObservedAt) : now;
    return new Date(base.getTime() - Number(hoursAgo[1]) * 60 * 60 * 1000).toISOString();
  }

  const minutesAgo = text.match(/(\d+)\s*minutes?\s*ago/i);
  if (minutesAgo) {
    const base = latestObservedAt ? new Date(latestObservedAt) : now;
    return new Date(base.getTime() - Number(minutesAgo[1]) * 60 * 1000).toISOString();
  }

  // Defensive fallback for a fully qualified date/time label.
  const parsed = Date.parse(text);
  if (!Number.isNaN(parsed)) return new Date(parsed).toISOString();

  return null;
}

function parsePagasaPageTimestamp($) {
  const pageText = $('body').text().replace(/\s+/g, ' ');
  const match = pageText.match(/Time\s*:\s*(\d{4})-(\d{2})-(\d{2})\s+(\d{1,2}):(\d{2})/i);
  if (!match) return null;

  return toManilaIso(
    Number(match[1]),
    Number(match[2]),
    Number(match[3]),
    Number(match[4]),
    Number(match[5])
  );
}

function uniqueObservations(observations) {
  const deduped = new Map();

  for (const observation of observations) {
    if (!observation?.station_name || !observation?.observed_at || !isValidLevel(observation.level)) continue;
    const key = `${observation.station_name}|${observation.observed_at}`;
    if (!deduped.has(key)) deduped.set(key, observation);
  }

  return [...deduped.values()].sort(
    (a, b) => new Date(a.observed_at).getTime() - new Date(b.observed_at).getTime()
  );
}

async function fetchPagasaDirectly() {
  console.log(`[Scraper] PAGASA URL: ${PAGASA_WATER_URL}`);

  try {
    const response = await axios.get(PAGASA_WATER_URL, {
      timeout: 15000,
      headers: HTTP_HEADERS
    });

    const $ = cheerio.load(response.data);
    const pageObservedAt = parsePagasaPageTimestamp($);
    const observations = [];

    $('table tr').each((_, row) => {
      const cols = $(row).find('td');
      if (cols.length < 5) return;

      const rawName = $(cols[0]).text().trim();
      const stationName = mapStationName(rawName);
      if (!stationName) return;

      // PAGASA table.do may expose Current/-30m/-1h/-2h before Alert/Alarm/Critical.
      // Current is the first numeric cell after Station; thresholds are the last 3 cells.
      const currentLevel = Number.parseFloat($(cols[1]).text().trim().replace(/\(\*\)/g, ''));
      const alertThreshold = Number.parseFloat($(cols[cols.length - 3]).text().trim());
      const alarmThreshold = Number.parseFloat($(cols[cols.length - 2]).text().trim());
      const criticalThreshold = Number.parseFloat($(cols[cols.length - 1]).text().trim());

      if (!isValidLevel(currentLevel)) return;

      let status = calculateStatus(stationName, currentLevel);
      if (Number.isFinite(criticalThreshold) && currentLevel >= criticalThreshold) status = '3rd Alarm';
      else if (Number.isFinite(alarmThreshold) && currentLevel >= alarmThreshold) status = '2nd Alarm';
      else if (Number.isFinite(alertThreshold) && currentLevel >= alertThreshold) status = '1st Alarm';

      observations.push({
        station_name: stationName,
        level: currentLevel,
        status,
        source: 'PAGASA',
        observed_at: pageObservedAt || new Date().toISOString()
      });
    });

    const verified = uniqueObservations(observations);
    if (verified.length > 0) {
      console.log(`[Scraper] PAGASA returned ${verified.length} verified current observation(s).`);
      return verified;
    }

    console.warn('[Scraper] PAGASA responded but contained no usable current water-level rows.');
    return [];
  } catch (error) {
    console.warn(`[Scraper] PAGASA unavailable (${error.message}). Falling back to BantayBaha.`);
    return [];
  }
}

function findDetailsTable($) {
  let selected = null;

  $('table').each((_, table) => {
    if (selected) return;
    const headers = $(table)
      .find('th')
      .map((__, th) => normalizeText($(th).text()))
      .get();

    const hasTime = headers.some((header) => header === 'time' || header.includes('time'));
    const hasLevel = headers.some((header) => header.includes('level'));
    if (hasTime && hasLevel) selected = table;
  });

  return selected;
}

function parseBantayBahaGaugeHtml(html, stationName, url) {
  const $ = cheerio.load(html);
  const table = findDetailsTable($);
  const now = new Date();
  const rows = [];

  if (table) {
    $(table).find('tbody tr, tr').each((_, row) => {
      const cells = $(row).find('td');
      if (cells.length < 2) return;

      const timeLabel = $(cells[0]).text().replace(/\s+/g, ' ').trim();
      const levelText = $(cells[1]).text().replace(/,/g, '').trim();
      const level = Number.parseFloat(levelText.match(/-?\d+(?:\.\d+)?/)?.[0]);

      if (!timeLabel || !isValidLevel(level)) return;
      rows.push({ timeLabel, level });
    });
  }

  // Fallback for a changed HTML wrapper while retaining the same visible Details text.
  if (rows.length === 0) {
    const text = $('body').text().replace(/\s+/g, ' ');
    const current = text.match(/(\d{1,2}:\d{2}\s*(?:AM|PM)\s+today)\s+(\d+(?:\.\d+)?)/i);
    if (current && isValidLevel(Number(current[2]))) {
      rows.push({ timeLabel: current[1], level: Number(current[2]) });
    }
  }

  if (rows.length === 0) {
    throw new Error(`No valid Details rows found at ${url}`);
  }

  const latestObservedAt = parseBantayBahaTimestamp(rows[0].timeLabel, null, now);
  if (!latestObservedAt) {
    throw new Error(`Could not parse latest source timestamp "${rows[0].timeLabel}" at ${url}`);
  }

  const observations = [];
  for (const row of rows) {
    const observedAt = parseBantayBahaTimestamp(row.timeLabel, latestObservedAt, now);
    if (!observedAt) continue;

    observations.push({
      station_name: stationName,
      level: row.level,
      status: calculateStatus(stationName, row.level),
      source: 'BantayBaha',
      observed_at: observedAt
    });
  }

  return uniqueObservations(observations);
}

async function fetchBantayBahaFallback() {
  const observations = [];

  for (const gauge of BANTAYBAHA_GAUGES) {
    try {
      console.log(`[Scraper] BantayBaha gauge: ${gauge.url}`);
      const response = await axios.get(gauge.url, {
        timeout: 15000,
        headers: {
          ...HTTP_HEADERS,
          Referer: 'https://bantaybaha.com/'
        }
      });

      const gaugeObservations = parseBantayBahaGaugeHtml(
        response.data,
        gauge.stationName,
        gauge.url
      );

      const latest = gaugeObservations[gaugeObservations.length - 1];
      console.log(
        `[Scraper] BantayBaha ${gauge.stationName}: latest ${latest.level}m @ ${latest.observed_at}; ` +
        `${gaugeObservations.length} source observation(s) available for history.`
      );

      observations.push(...gaugeObservations);
    } catch (error) {
      console.warn(`[Scraper] BantayBaha ${gauge.stationName} unavailable/unparseable: ${error.message}`);
    }
  }

  return uniqueObservations(observations);
}

async function fetchVerifiedTelemetry() {
  const pagasa = await fetchPagasaDirectly();
  if (pagasa.length > 0) return pagasa;

  console.log('[Scraper] Using BantayBaha fallback.');
  return fetchBantayBahaFallback();
}

async function historyExists(observation) {
  const { data, error } = await supabase
    .from('river_level_history')
    .select('id')
    .eq('station_name', observation.station_name)
    .eq('observed_at', observation.observed_at)
    .limit(1);

  if (error) throw error;
  return Array.isArray(data) && data.length > 0;
}

async function persistHistoryObservation(observation) {
  try {
    if (await historyExists(observation)) {
      console.log(
        `↪ History already stored ${observation.station_name} @ ${observation.observed_at}; duplicate skipped.`
      );
      return 'duplicate';
    }

    const { data, error } = await supabase
      .from('river_level_history')
      .insert({
        station_name: observation.station_name,
        level: observation.level,
        status: observation.status,
        source: observation.source,
        observed_at: observation.observed_at
      })
      .select('id, station_name, level, source, observed_at');

    if (error) {
      // Unique constraint still protects against races between overlapping instances.
      if (error.code === '23505') {
        console.log(
          `↪ History duplicate protected by database ${observation.station_name} @ ${observation.observed_at}.`
        );
        return 'duplicate';
      }
      throw error;
    }

    const stored = data?.[0];
    console.log(
      `🕒 Stored history ${stored?.station_name ?? observation.station_name}: ` +
      `${stored?.level ?? observation.level}m (${stored?.source ?? observation.source}) @ ` +
      `${stored?.observed_at ?? observation.observed_at}`
    );
    return 'inserted';
  } catch (error) {
    console.error(`❌ Failed history insert for ${observation.station_name}:`, error.message);
    return 'error';
  }
}

async function persistLatestObservation(observation) {
  try {
    const { data: existing, error: readError } = await supabase
      .from('monitoring_stations')
      .select('updated_at')
      .eq('station_name', observation.station_name)
      .maybeSingle();

    if (readError) throw readError;

    const existingTime = existing?.updated_at ? new Date(existing.updated_at).getTime() : null;
    const candidateTime = new Date(observation.observed_at).getTime();

    if (existingTime && Number.isFinite(existingTime) && existingTime > candidateTime) {
      console.log(
        `↪ Latest row for ${observation.station_name} is newer than source observation; latest upsert skipped.`
      );
      return 'older';
    }

    const { error } = await supabase
      .from('monitoring_stations')
      .upsert(
        {
          station_name: observation.station_name,
          level: observation.level,
          status: observation.status,
          // Important: freshness is based on source observation time, not scrape time.
          updated_at: observation.observed_at
        },
        { onConflict: 'station_name' }
      );

    if (error) throw error;

    console.log(
      `✅ Synced latest ${observation.station_name}: ${observation.level}m -> ${observation.status} ` +
      `@ ${observation.observed_at}`
    );
    return 'updated';
  } catch (error) {
    console.error(`❌ Failed latest-reading update for ${observation.station_name}:`, error.message);
    return 'error';
  }
}

let syncInProgress = false;

async function syncToSupabase() {
  if (syncInProgress) {
    console.warn('[Telemetry] Previous sync is still running; this scheduled cycle is skipped.');
    return;
  }

  syncInProgress = true;

  try {
    const observations = await fetchVerifiedTelemetry();

    if (observations.length === 0) {
      console.warn(
        '[Telemetry] No verified live/source observations available. ' +
        'Supabase latest values and timestamps are left unchanged.'
      );
      return;
    }

    const verified = uniqueObservations(observations);
    console.log(`[Telemetry] Persisting ${verified.length} verified observation(s).`);

    // Persist every timestamped source observation to history. This allows the
    // BantayBaha Details rows to bootstrap up to ~24 hours of genuine history.
    for (const observation of verified) {
      await persistHistoryObservation(observation);
    }

    // monitoring_stations is a latest-state table: only the newest observation
    // for each station is allowed to update it.
    const latestByStation = new Map();
    for (const observation of verified) {
      const current = latestByStation.get(observation.station_name);
      if (!current || new Date(observation.observed_at) > new Date(current.observed_at)) {
        latestByStation.set(observation.station_name, observation);
      }
    }

    for (const latest of latestByStation.values()) {
      await persistLatestObservation(latest);
    }
  } catch (error) {
    console.error('[Telemetry] Sync cycle failed:', error);
  } finally {
    syncInProgress = false;
  }
}

// Run once immediately, then every five minutes.
await syncToSupabase();
cron.schedule('*/5 * * * *', () => {
  void syncToSupabase();
});

console.log('[Telemetry] Worker scheduled: every 5 minutes.');
