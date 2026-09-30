import { createClient } from '@supabase/supabase-js';
import axios from 'axios';
import * as cheerio from 'cheerio';
import cron from 'node-cron';
import dotenv from 'dotenv';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Local development loads the parent .env regardless of shell cwd.
// Railway injects process.env directly.
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

console.log('[Telemetry] PAGASA-only server-side Supabase client initialized.');

const MAX_REASONABLE_LEVEL_METERS = 50;
const PAGASA_WATER_URL =
  'https://pasig-marikina-tullahanffws.pagasa.dost.gov.ph/water/table.do';

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

function toManilaIso(year, month, day, hour24, minute) {
  const y = String(year).padStart(4, '0');
  const m = String(month).padStart(2, '0');
  const d = String(day).padStart(2, '0');
  const h = String(hour24).padStart(2, '0');
  const min = String(minute).padStart(2, '0');
  const parsed = new Date(`${y}-${m}-${d}T${h}:${min}:00+08:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

function parsePagasaPageTimestamp($) {
  const pageText = $('body').text().replace(/\s+/g, ' ');

  // Known PAGASA presentation: Time: YYYY-MM-DD HH:mm
  let match = pageText.match(/Time\s*:\s*(\d{4})-(\d{2})-(\d{2})\s+(\d{1,2}):(\d{2})/i);
  if (match) {
    return toManilaIso(
      Number(match[1]),
      Number(match[2]),
      Number(match[3]),
      Number(match[4]),
      Number(match[5])
    );
  }

  // Defensive alternate presentation: YYYY/MM/DD HH:mm or YYYY-MM-DD HH:mm.
  match = pageText.match(/(\d{4})[\/-](\d{2})[\/-](\d{2})\s+(\d{1,2}):(\d{2})/);
  if (match) {
    return toManilaIso(
      Number(match[1]),
      Number(match[2]),
      Number(match[3]),
      Number(match[4]),
      Number(match[5])
    );
  }

  return null;
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

async function fetchPagasaTelemetry() {
  console.log(`[Scraper] PAGASA URL: ${PAGASA_WATER_URL}`);

  try {
    const response = await axios.get(PAGASA_WATER_URL, {
      timeout: 20000,
      headers: HTTP_HEADERS,
      maxRedirects: 5
    });

    const $ = cheerio.load(response.data);
    const observedAt = parsePagasaPageTimestamp($);

    if (!observedAt) {
      console.warn(
        '[Scraper] PAGASA responded but no official observation timestamp could be parsed. ' +
        'The cycle is rejected rather than inventing a timestamp.'
      );
      return [];
    }

    const observations = [];

    $('table tr').each((_, row) => {
      const cols = $(row).find('td');
      if (cols.length < 2) return;

      const rawName = $(cols[0]).text().trim();
      const stationName = mapStationName(rawName);
      if (!stationName) return;

      // Current water level is the first numeric cell after station name.
      const currentText = $(cols[1]).text().trim().replace(/\(\*\)/g, '');
      const currentLevel = Number.parseFloat(currentText.match(/-?\d+(?:\.\d+)?/)?.[0]);
      if (!isValidLevel(currentLevel)) return;

      // When PAGASA provides Alert/Alarm/Critical columns, use them. Otherwise
      // use the station threshold map already defined in RescuAR.
      let status = calculateStatus(stationName, currentLevel);
      if (cols.length >= 5) {
        const alertThreshold = Number.parseFloat($(cols[cols.length - 3]).text().trim());
        const alarmThreshold = Number.parseFloat($(cols[cols.length - 2]).text().trim());
        const criticalThreshold = Number.parseFloat($(cols[cols.length - 1]).text().trim());

        if (Number.isFinite(criticalThreshold) && currentLevel >= criticalThreshold) status = '3rd Alarm';
        else if (Number.isFinite(alarmThreshold) && currentLevel >= alarmThreshold) status = '2nd Alarm';
        else if (Number.isFinite(alertThreshold) && currentLevel >= alertThreshold) status = '1st Alarm';
      }

      observations.push({
        station_name: stationName,
        level: currentLevel,
        status,
        source: 'PAGASA',
        observed_at: observedAt
      });
    });

    const verified = uniqueObservations(observations);

    if (verified.length === 0) {
      console.warn(
        '[Scraper] PAGASA responded but contained no usable current water-level rows. ' +
        'No database values will be changed.'
      );
      return [];
    }

    console.log(
      `[Scraper] PAGASA returned ${verified.length} verified observation(s) @ ${observedAt}.`
    );
    return verified;
  } catch (error) {
    console.warn(
      `[Scraper] PAGASA request failed (${error.message}). ` +
      'No third-party fallback is configured; existing verified values remain unchanged.'
    );
    return [];
  }
}

async function historyExists(observation) {
  const { data, error } = await supabase
    .from('river_level_history')
    .select('id')
    .eq('station_name', observation.station_name)
    .eq('observed_at', observation.observed_at)
    .eq('source', 'PAGASA')
    .limit(1);

  if (error) throw error;
  return Array.isArray(data) && data.length > 0;
}

async function persistHistoryObservation(observation) {
  try {
    if (await historyExists(observation)) {
      console.log(
        `↪ PAGASA history already stored ${observation.station_name} @ ${observation.observed_at}; duplicate skipped.`
      );
      return 'duplicate';
    }

    const { data, error } = await supabase
      .from('river_level_history')
      .insert({
        station_name: observation.station_name,
        level: observation.level,
        status: observation.status,
        source: 'PAGASA',
        observed_at: observation.observed_at
      })
      .select('id, station_name, level, source, observed_at');

    if (error) {
      if (error.code === '23505') {
        console.log(
          `↪ PAGASA history duplicate protected by database ${observation.station_name} @ ${observation.observed_at}.`
        );
        return 'duplicate';
      }
      throw error;
    }

    const stored = data?.[0];
    console.log(
      `🕒 Stored PAGASA history ${stored?.station_name ?? observation.station_name}: ` +
      `${stored?.level ?? observation.level}m @ ${stored?.observed_at ?? observation.observed_at}`
    );
    return 'inserted';
  } catch (error) {
    console.error(`❌ Failed PAGASA history insert for ${observation.station_name}:`, error.message);
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
        `↪ Latest row for ${observation.station_name} is newer than the PAGASA observation; latest upsert skipped.`
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
          // Freshness is based on official PAGASA observation time, not scrape time.
          updated_at: observation.observed_at
        },
        { onConflict: 'station_name' }
      );

    if (error) throw error;

    console.log(
      `✅ Synced PAGASA latest ${observation.station_name}: ${observation.level}m -> ${observation.status} ` +
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
    console.warn('[Telemetry] Previous PAGASA sync is still running; this scheduled cycle is skipped.');
    return;
  }

  syncInProgress = true;

  try {
    const observations = await fetchPagasaTelemetry();

    if (observations.length === 0) {
      console.warn(
        '[Telemetry] No verified PAGASA observations available. ' +
        'Supabase latest values and timestamps are left unchanged so frontend freshness can become Stale/Unavailable naturally.'
      );
      return;
    }

    const verified = uniqueObservations(observations);
    console.log(`[Telemetry] Persisting ${verified.length} verified PAGASA observation(s).`);

    for (const observation of verified) {
      await persistHistoryObservation(observation);
    }

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
    console.error('[Telemetry] PAGASA sync cycle failed:', error);
  } finally {
    syncInProgress = false;
  }
}

// Run once immediately, then poll the official PAGASA source every five minutes.
await syncToSupabase();
cron.schedule('*/5 * * * *', () => {
  void syncToSupabase();
});

console.log('[Telemetry] PAGASA-only worker scheduled: every 5 minutes.');
