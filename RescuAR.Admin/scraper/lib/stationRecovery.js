import { matchConfiguredStations, hasCompleteThresholds } from './stationSelection.js';

// Keep recovery matching independently testable without network dependencies.
function parsePagasaNumber(value) {
  if (value === null || value === undefined || value === '') return null;
  const match = String(value).replace(/\(\*\)/g, '').trim().match(/^-?\d+(?:\.\d+)?/);
  return match ? Number(match[0]) : null;
}
function isValidWaterLevel(value) {
  return Number.isFinite(value) && value >= 0 && value <= 100;
}
function toManilaIsoFromTimestamp(value) {
  const match = String(value ?? '').match(/^(\d{4})-(\d{2})-(\d{2}) (\d{1,2}):(\d{2})$/);
  if (!match) return null;
  const date = new Date(`${match[1]}-${match[2]}-${match[3]}T${match[4].padStart(2, '0')}:${match[5]}:00+08:00`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}


// These are separate verified PAGASA gauges at the same monitoring point. Never
// conflate their history or display names; only switch the active catalog row.
export const VERIFIED_ALTERNATE_CODES = new Map([
  ['11104201', '11104204'],
  ['11104204', '11104201']
]);

export function normalizeSourceStationName(value) {
  return String(value || '').normalize('NFKD').replace(/[\u0300-\u036f]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
}

function stationCode(value) {
  const code = String(value ?? '').trim();
  return /^\d{8}$/.test(code) ? code : null;
}

/**
 * Only propose changes for missing active stations. A new code must have an
 * EXACT normalized source-name match; known alternate gauges also require the
 * separately verified code/name from the catalog. Neither fuzzy names nor
 * other live stations are considered replacement evidence.
 */
export function detectStationSwitches(rawRows, activeStations, catalogStations) {
  const { missing, unexpected, duplicates } = matchConfiguredStations(rawRows, activeStations);
  const activeCodes = new Set(activeStations.map((s) => String(s.station_code).trim()));
  const catalogByCode = new Map(catalogStations.map((s) => [String(s.station_code).trim(), s]));
  const duplicateCodes = new Set(duplicates);
  const rawCodeCounts = new Map();
  for (const row of rawRows) {
    const code = stationCode(row?.obscd);
    if (code) rawCodeCounts.set(code, (rawCodeCounts.get(code) || 0) + 1);
  }
  const candidates = [];
  const ambiguous = [];
  const claimedCodes = new Set();

  for (const current of missing) {
    const oldCode = String(current.station_code).trim();
    const currentName = normalizeSourceStationName(current.source_station_name);
    const alternativeCode = VERIFIED_ALTERNATE_CODES.get(oldCode);
    const matches = [];

    for (const { code, name } of unexpected) {
      const nextCode = stationCode(code);
      if (!nextCode || duplicateCodes.has(nextCode) || rawCodeCounts.get(nextCode) !== 1 || activeCodes.has(nextCode)) continue;
      const nextName = normalizeSourceStationName(name);
      if (!nextName) continue;

      if (currentName && nextName === currentName && !catalogByCode.has(nextCode)) {
        matches.push({ mode: 'code_change', newCode: nextCode, name });
      } else if (nextCode === alternativeCode) {
        const alternative = catalogByCode.get(nextCode);
        if (alternative && alternative.enabled === false &&
            nextName === normalizeSourceStationName(alternative.source_station_name)) {
          matches.push({ mode: 'alternate', newCode: nextCode, name, alternative });
        }
      }
    }

    if (matches.length !== 1 || claimedCodes.has(matches[0]?.newCode)) {
      if (matches.length) ambiguous.push({ oldCode, matches: matches.map((match) => match.newCode) });
      continue;
    }

    const match = matches[0];
    const raw = rawRows.find((row) => stationCode(row?.obscd) === match.newCode);
    if (!raw) continue;
    claimedCodes.add(match.newCode);
    candidates.push({
      mode: match.mode,
      oldStation: current,
      newStation: match.mode === 'alternate'
        ? { ...match.alternative, station_code: match.newCode }
        : { ...current, station_code: match.newCode, source_station_name: match.name },
      raw
    });
  }

  return { candidates, ambiguous };
}

/** Require a recent, timestamped detail reading with safe thresholds. */
export function verifyStationSwitchData(raw, historyRows, checkedAt, maxAgeMs = 30 * 60 * 1000) {
  const now = Date.parse(checkedAt);
  if (!Number.isFinite(now) || !isValidWaterLevel(parsePagasaNumber(raw?.wl))) return false;
  if (!Array.isArray(historyRows) || !historyRows.length) return false;
  const latest = historyRows
    .map((row) => ({ ...row, observed: toManilaIsoFromTimestamp(row?.timestr) }))
    .filter((row) => row.observed && isValidWaterLevel(parsePagasaNumber(row?.wl)))
    .sort((a, b) => Date.parse(b.observed) - Date.parse(a.observed))[0];
  if (!latest) return false;
  const age = now - Date.parse(latest.observed);
  if (age < -5 * 60 * 1000 || age > maxAgeMs) return false;
  const thresholds = {
    alert_threshold_m: parsePagasaNumber(raw?.alertwl) ?? parsePagasaNumber(latest.alertwl),
    alarm_threshold_m: parsePagasaNumber(raw?.alarmwl) ?? parsePagasaNumber(latest.alarmwl),
    critical_threshold_m: parsePagasaNumber(raw?.criticalwl) ?? parsePagasaNumber(latest.criticalwl)
  };
  return hasCompleteThresholds(thresholds) &&
    thresholds.alert_threshold_m <= thresholds.alarm_threshold_m &&
    thresholds.alarm_threshold_m <= thresholds.critical_threshold_m;
}

export function isRecentStationObservation(observedAt, checkedAt, maxAgeMs = 30 * 60 * 1000) {
  const observed = Date.parse(observedAt);
  const now = Date.parse(checkedAt);
  return Number.isFinite(observed) && Number.isFinite(now) &&
    observed >= now - maxAgeMs && observed <= now + 5 * 60 * 1000;
}

/** Calculate a 30-minute trend only when detail observations actually support it. */
export function deriveTrendFromHistory(historyRows, latestRow, tolerance = 0.05) {
  const latestTime = Date.parse(latestRow?.observed_at);
  const latestLevel = Number(latestRow?.level);
  if (!Number.isFinite(latestTime) || !Number.isFinite(latestLevel)) return null;
  const candidates = (historyRows || []).map((row) => ({
    time: Date.parse(row.observed_at), level: Number(row.level)
  })).filter((row) =>
    Number.isFinite(row.time) && Number.isFinite(row.level) &&
    latestTime - row.time >= 20 * 60000 && latestTime - row.time <= 40 * 60000
  ).sort((a, b) => Math.abs(latestTime - a.time - 30 * 60000) - Math.abs(latestTime - b.time - 30 * 60000));
  if (!candidates.length) return null;
  const delta = Number((latestLevel - candidates[0].level).toFixed(3));
  return {
    trend: delta > tolerance ? 'Rising' : delta < -tolerance ? 'Falling' : 'Stable',
    delta_30m_m: delta,
    water_level_30m_m: candidates[0].level
  };
}
