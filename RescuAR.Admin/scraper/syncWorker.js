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
import {
  matchConfiguredStations,
  isUsableNewObservation,
  hasCompleteThresholds
} from './lib/stationSelection.js';

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

async function getLastKnownStationMetadata(stationCode) {
  const { data, error } = await supabase
    .from('monitoring_stations')
    .select('source_station_name, observed_at, alert_threshold_m, alarm_threshold_m, critical_threshold_m')
    .eq('station_code', stationCode)
    .eq('source', PAGASA_SOURCE_ID)
    .limit(1);

  if (error) throw error;
  const last = data?.[0] || {};
  const asNumberOrNull = (value) => {
    if (value === null || value === undefined || value === '') return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  };
  return {
    source_station_name: last.source_station_name || null,
    observed_at: last.observed_at || null,
    alert_threshold_m: asNumberOrNull(last.alert_threshold_m),
    alarm_threshold_m: asNumberOrNull(last.alarm_threshold_m),
    critical_threshold_m: asNumberOrNull(last.critical_threshold_m)
  };
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

/**
 * A station may disappear from table_list.do while its detail_list.do endpoint
 * still has valid readings. Attempt a per-code recovery without pretending that
 * an old (or future-dated) observation is current.
 */
async function recoverMissingStation(station, ymdhm, checkedAt) {
  const code = String(station.station_code).trim();
  console.warn(`[Scraper] MISSING from table_list.do: ${station.display_name} (${code}). Checking detail_list.do.`);

  try {
    const rawHistory = await fetchPagasaStationHistory(code, ymdhm);
    const storedHistoryAt = await getLatestStoredObservationTime(code);
    const metadata = await getLastKnownStationMetadata(code);
    const storedAt = [storedHistoryAt, metadata.observed_at]
      .filter(Boolean).sort((a, b) => Date.parse(b) - Date.parse(a))[0] || null;
    const historyRows = normalizeHistoryRows(rawHistory, station, metadata)
      .filter((row) => isUsableNewObservation(row.observed_at, null, checkedAt));

    // Only recover observations with the PAGASA thresholds needed to classify
    // them safely. We never write a fabricated "Normal" status.
    const safeHistoryRows = historyRows.filter(hasCompleteThresholds);
    const newRows = safeHistoryRows.filter((row) => isUsableNewObservation(row.observed_at, storedAt, checkedAt));
    if (!newRows.length) {
      const reason = historyRows.length && !safeHistoryRows.length
        ? 'detail data lacks verified water-level thresholds'
        : 'no NEW valid detail readings';
      console.warn(`[Scraper] ${station.display_name} (${code}): ${reason}; preserving last observation.`);
      return null;
    }

    await persistHistoryRows(newRows);
    const latest = newRows[newRows.length - 1];
    const snapshot = {
      station_code: code,
      station_name: station.display_name,
      source_station_name: metadata.source_station_name || station.source_station_name,
      alert_threshold_m: latest.alert_threshold_m,
      alarm_threshold_m: latest.alarm_threshold_m,
      critical_threshold_m: latest.critical_threshold_m,
      // No 30-minute value from the current table: trend must not be "Stable".
      water_level_30m_m: null,
      water_level_1h_m: null,
      water_level_2h_m: null,
      trend: 'Unknown',
      delta_30m_m: null
    };
    await persistLatestObservation(snapshot, latest, checkedAt);
    const ageMinutes = Math.round((Date.parse(checkedAt) - Date.parse(latest.observed_at)) / 60000);
    console.warn(
      `[Scraper] ${station.display_name} (${code}): RECOVERED ${newRows.length} detail observation(s), ` +
      `newest ${latest.observed_at} (${ageMinutes} min old). Source-list entry still missing; trend Unknown.`
    );
    return latest.observed_at;
  } catch (error) {
    // A single station's missing detail request must not abort updates for the others.
    console.warn(`[Scraper] ${station.display_name} (${code}): detail recovery failed: ${error.message}`);
    return null;
  }
}

async function fetchAndPersistPagasaTelemetry() {
  const checkedAt = new Date().toISOString();
  const { searchTime, ymdhm } = await fetchPagasaReferenceTime();
  console.log(`[Scraper] PAGASA reference time ${searchTime} (${ymdhm}).`);

  const configuredStations = await getConfiguredLiveStations();
  console.log(`[Scraper] Configured enabled station codes: ${configuredStations.map((s) => String(s.station_code).trim()).join(', ')}.`);
  const rawStationList = await fetchPagasaStationList(ymdhm);
  const { selected, missing, unexpected, duplicates } = matchConfiguredStations(rawStationList, configuredStations);
  console.log(
    `[Scraper] table_list.do returned ${rawStationList.length} station(s); ` +
    `${selected.length}/${configuredStations.length} configured stations present.`
  );

  if (missing.length) {
    console.warn(`[Scraper] Missing configured station(s): ${missing.map((s) => `${s.display_name} (${s.station_code})`).join(', ')}.`);
    console.warn(`[Scraper] Returned station codes (diagnostics): ${rawStationList.map((row) => `${String(row?.obscd ?? '').trim()}:${String(row?.obsnm ?? '').trim()}`).join(' | ')}`);
    if (unexpected.length) {
      console.warn(`[Scraper] Unmatched source station codes: ${unexpected.map((s) => `${s.code}:${s.name}`).join(' | ')}`);
    }
  }
  if (duplicates.length) {
    console.warn(`[Scraper] Duplicate configured station codes in source response: ${duplicates.join(', ')}.`);
  }

  let verifiedCount = 0;
  let newestObservationAt = null;
  const recordObservation = (observedAt) => {
    if (!newestObservationAt || new Date(observedAt) > new Date(newestObservationAt)) {
      newestObservationAt = observedAt;
    }
  };

  for (const { raw, station } of selected) {
    const stationCode = String(station.station_code).trim();
    try {
      const snapshot = normalizeSnapshot(raw, station);
      if (!snapshot) {
        console.warn(`[Scraper] ${station.display_name} (${stationCode}): current snapshot is invalid; skipped.`);
        continue;
      }

      const rawHistory = await fetchPagasaStationHistory(stationCode, ymdhm);
      const historyRows = normalizeHistoryRows(rawHistory, station, snapshot)
        .filter((row) => isUsableNewObservation(row.observed_at, null, checkedAt));
      if (!historyRows.length) {
        console.warn(`[Scraper] ${station.display_name}: detail_list.do returned no valid timestamped observations.`);
        continue;
      }

      const lastStoredAt = await getLatestStoredObservationTime(stationCode);
      const newHistoryRows = historyRows.filter((row) => isUsableNewObservation(row.observed_at, lastStoredAt, checkedAt));
      if (newHistoryRows.length) {
        await persistHistoryRows(newHistoryRows);
        console.log(`[Telemetry] ${station.display_name}: stored ${newHistoryRows.length} new history row(s).`);
      } else {
        console.log(`[Telemetry] ${station.display_name}: no new observation timestamp; history insert skipped.`);
      }

      const latestHistory = historyRows[historyRows.length - 1];
      // Do not regress the dashboard to an older timestamp if PAGASA returns an old response.
      if (!lastStoredAt || new Date(latestHistory.observed_at) >= new Date(lastStoredAt)) {
        await persistLatestObservation(snapshot, latestHistory, checkedAt);
      } else {
        console.warn(`[Scraper] ${station.display_name}: older source timestamp ignored; stored ${lastStoredAt}.`);
      }

      verifiedCount += 1;
      recordObservation(latestHistory.observed_at);
      console.log(
        `[Telemetry] ${station.display_name}: ${latestHistory.level.toFixed(2)}m, ` +
        `${snapshot.status}, ${snapshot.trend}, observed ${latestHistory.observed_at}.`
      );
    } catch (error) {
      console.warn(`[Telemetry] ${station.display_name} (${stationCode}): failed this cycle: ${error.message}`);
    }
  }

  // Code-specific recovery is intentionally NOT a station-name alias. We only
  // accept timestamped history returned for the already verified PAGASA code.
  for (const station of missing) {
    const recoveredAt = await recoverMissingStation(station, ymdhm, checkedAt);
    if (recoveredAt) {
      verifiedCount += 1;
      recordObservation(recoveredAt);
    }
  }

  await updateSourceStatus({
    source_status: verifiedCount > 0 ? 'reachable' : 'no_data',
    ...(verifiedCount > 0 ? { last_success_at: checkedAt } : {}),
    last_observation_at: newestObservationAt,
    records_received: verifiedCount,
    last_error_code: verifiedCount < configuredStations.length ? 'PARTIAL_STATION_DATA' : null,
    last_error_message: verifiedCount < configuredStations.length
      ? `Only ${verifiedCount}/${configuredStations.length} configured stations produced usable data. See station-specific logs.`
      : null
  });

  console.log(`[Telemetry] Coverage: ${selected.length}/${configuredStations.length} present in source list; ${verifiedCount} verified/recovered this cycle; ${missing.length} absent from source list.`);
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
