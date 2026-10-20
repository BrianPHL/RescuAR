import { createClient } from '@supabase/supabase-js';
import cron from 'node-cron';
import dotenv from 'dotenv';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

import {
  PAGASA_SOURCE_ID,
  classifyWaterLevelStatus,
  fetchPagasaReferenceTime,
  fetchPagasaStationHistory,
  fetchPagasaStationList,
  normalizeHistoryRows,
  normalizeSnapshot
} from './lib/pagasaClient.js';
import { LIVE_PAGASA_STATIONS } from './config/pagasaStations.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Local development loads the parent .env. Railway injects process.env directly.
dotenv.config({ path: path.resolve(__dirname, '../.env') });

const SUPABASE_URL = process.env.SUPABASE_URL;
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY;

if (!SUPABASE_URL || !SUPABASE_SERVICE_ROLE_KEY) {
  throw new Error(
    '[Telemetry] Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY. ' +
    'Set both only in the server/worker environment. Never expose the service-role key through VITE_* variables.'
  );
}

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, {
  auth: { persistSession: false, autoRefreshToken: false }
});

console.log('[Telemetry] PAGASA JSON telemetry worker initialized.');

async function getConfiguredLiveStations() {
  const { data, error } = await supabase
    .from('station_catalog')
    .select('station_code, source_station_name, display_name, enabled, is_primary')
    .eq('source', PAGASA_SOURCE_ID)
    .eq('enabled', true);

  if (error) {
    console.warn('[Telemetry] station_catalog could not be read; using verified code fallback:', error.message);
    return LIVE_PAGASA_STATIONS;
  }

  if (!data?.length) {
    console.warn('[Telemetry] station_catalog has no enabled PAGASA rows; using verified code fallback.');
    return LIVE_PAGASA_STATIONS;
  }

  return data.map((row) => ({
    key: row.station_code,
    station_code: String(row.station_code),
    source_station_name: row.source_station_name,
    display_name: row.display_name,
    enabled_live: true,
    is_primary: row.is_primary
  }));
}

async function getLatestStoredObservationTime(stationCode) {
  const { data, error } = await supabase
    .from('river_level_history')
    .select('observed_at')
    .eq('station_code', stationCode)
    .eq('source', PAGASA_SOURCE_ID)
    .order('observed_at', { ascending: false })
    .limit(1);

  if (error) throw error;
  return data?.[0]?.observed_at || null;
}

async function persistHistoryRows(rows) {
  if (!rows.length) return 0;

  const { error } = await supabase
    .from('river_level_history')
    .upsert(rows, {
      onConflict: 'station_code,observed_at,source',
      ignoreDuplicates: true
    });

  if (error) throw error;
  return rows.length;
}

async function persistLatestObservation(snapshot, latestHistory, checkedAt) {
  const observedAt = latestHistory?.observed_at;
  if (!observedAt) return false;

  const { error } = await supabase
    .from('monitoring_stations')
    .upsert({
      station_code: snapshot.station_code,
      station_name: snapshot.station_name,
      source_station_name: snapshot.source_station_name,
      level: latestHistory.level,
      raw_water_level: latestHistory.raw_water_level,
      quality_flag: latestHistory.quality_flag,
      water_level_30m_m: snapshot.water_level_30m_m,
      water_level_1h_m: snapshot.water_level_1h_m,
      water_level_2h_m: snapshot.water_level_2h_m,
      alert_threshold_m: snapshot.alert_threshold_m,
      alarm_threshold_m: snapshot.alarm_threshold_m,
      critical_threshold_m: snapshot.critical_threshold_m,
      status: classifyWaterLevelStatus(
        latestHistory.level,
        snapshot.alert_threshold_m,
        snapshot.alarm_threshold_m,
        snapshot.critical_threshold_m
      ),
      trend: snapshot.trend,
      delta_30m_m: snapshot.delta_30m_m,
      observed_at: observedAt,
      source_checked_at: checkedAt,
      source: PAGASA_SOURCE_ID,
      // Backward compatibility during migration. Frontend now prefers observed_at.
      updated_at: observedAt
    }, { onConflict: 'station_name' });

  if (error) throw error;
  return true;
}

async function updateSourceStatus(patch) {
  const now = new Date().toISOString();
  const payload = {
    source: PAGASA_SOURCE_ID,
    worker_status: 'healthy',
    last_check_at: now,
    ...patch,
    updated_at: now
  };

  const { error } = await supabase
    .from('telemetry_source_status')
    .upsert(payload, { onConflict: 'source' });

  if (error) {
    console.warn('[Telemetry] Unable to update telemetry_source_status:', error.message);
  }
}

async function fetchAndPersistPagasaTelemetry() {
  const checkedAt = new Date().toISOString();
  const { searchTime, ymdhm } = await fetchPagasaReferenceTime();
  console.log(`[Scraper] PAGASA reference time ${searchTime} (${ymdhm}).`);

  const configuredStations = await getConfiguredLiveStations();
  const stationByCode = new Map(configuredStations.map((station) => [station.station_code, station]));
  const rawStationList = await fetchPagasaStationList(ymdhm);
  const selected = rawStationList.filter((row) => stationByCode.has(String(row?.obscd || '')));
  console.log(
    `[Scraper] table_list.do returned ${rawStationList.length} station(s); ` +
    `${selected.length} are configured for RescuAR.`
  );

  let verifiedCount = 0;
  let newestObservationAt = null;

  for (const raw of selected) {
    const stationCode = String(raw.obscd);
    const station = stationByCode.get(stationCode);
    const snapshot = normalizeSnapshot(raw, station);
    if (!snapshot) {
      console.warn(`[Scraper] ${station.display_name}: current snapshot is invalid; skipped.`);
      continue;
    }

    const rawHistory = await fetchPagasaStationHistory(stationCode, ymdhm);
    const historyRows = normalizeHistoryRows(rawHistory, station, snapshot);
    if (!historyRows.length) {
      console.warn(`[Scraper] ${station.display_name}: detail_list.do returned no valid timestamped observations.`);
      continue;
    }

    const lastStoredAt = await getLatestStoredObservationTime(stationCode);
    const newHistoryRows = lastStoredAt
      ? historyRows.filter((row) => new Date(row.observed_at) > new Date(lastStoredAt))
      : historyRows;

    if (newHistoryRows.length) {
      await persistHistoryRows(newHistoryRows);
      console.log(`[Telemetry] ${station.display_name}: stored ${newHistoryRows.length} new history row(s).`);
    } else {
      console.log(`[Telemetry] ${station.display_name}: no new observation timestamp; history insert skipped.`);
    }

    const latestHistory = historyRows[historyRows.length - 1];
    await persistLatestObservation(snapshot, latestHistory, checkedAt);

    verifiedCount += 1;
    if (!newestObservationAt || new Date(latestHistory.observed_at) > new Date(newestObservationAt)) {
      newestObservationAt = latestHistory.observed_at;
    }

    console.log(
      `[Telemetry] ${station.display_name}: ${latestHistory.level.toFixed(2)}m, ` +
      `${snapshot.status}, ${snapshot.trend}, observed ${latestHistory.observed_at}.`
    );
  }

  await updateSourceStatus({
    source_status: verifiedCount > 0 ? 'reachable' : 'no_data',
    last_success_at: checkedAt,
    last_observation_at: newestObservationAt,
    records_received: verifiedCount,
    last_error_code: null,
    last_error_message: null
  });

  return verifiedCount;
}

let syncInProgress = false;

async function syncToSupabase() {
  if (syncInProgress) {
    console.warn('[Telemetry] Previous sync is still running; scheduled cycle skipped.');
    return;
  }

  syncInProgress = true;
  try {
    const count = await fetchAndPersistPagasaTelemetry();
    console.log(`[Telemetry] PAGASA sync completed: ${count} verified configured station(s).`);
  } catch (error) {
    console.error('[Telemetry] PAGASA sync failed:', error.message);
    await updateSourceStatus({
      source_status: 'failed',
      records_received: 0,
      last_error_code: error.code || 'SOURCE_REQUEST_FAILED',
      last_error_message: error.message
    });
  } finally {
    syncInProgress = false;
  }
}

// Run once immediately, then poll the official PAGASA JSON-backed source every 5 minutes.
await syncToSupabase();
cron.schedule('*/5 * * * *', () => void syncToSupabase());
console.log('[Telemetry] PAGASA JSON worker scheduled: every 5 minutes.');
