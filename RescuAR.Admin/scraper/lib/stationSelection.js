/**
 * Match the live PAGASA station list against configured station codes.
 * Only the verified station code is authoritative; names are diagnostic only.
 */
export function matchConfiguredStations(rawStationList, configuredStations) {
  const configuredByCode = new Map(
    configuredStations.map((station) => [String(station.station_code).trim(), station])
  );
  const selected = [];
  const presentCodes = new Set();
  const unexpected = [];
  const duplicates = [];

  for (const row of rawStationList) {
    const code = String(row?.obscd ?? '').trim();
    if (!configuredByCode.has(code)) {
      unexpected.push({ code: code || '(missing)', name: String(row?.obsnm ?? '').trim() });
      continue;
    }
    if (presentCodes.has(code)) {
      duplicates.push(code);
      continue;
    }
    presentCodes.add(code);
    selected.push({ raw: row, station: configuredByCode.get(code) });
  }

  return {
    selected,
    missing: configuredStations.filter((station) => !presentCodes.has(String(station.station_code).trim())),
    unexpected,
    duplicates
  };
}

export function isUsableNewObservation(observedAt, lastStoredAt, checkedAt, futureToleranceMs = 5 * 60 * 1000) {
  const observed = Date.parse(observedAt);
  const checked = Date.parse(checkedAt);
  if (!Number.isFinite(observed) || !Number.isFinite(checked)) return false;
  if (observed > checked + futureToleranceMs) return false;
  if (lastStoredAt && observed <= Date.parse(lastStoredAt)) return false;
  return true;
}

export function hasCompleteThresholds(thresholds) {
  return ['alert_threshold_m', 'alarm_threshold_m', 'critical_threshold_m']
    .every((key) => thresholds[key] !== null && thresholds[key] !== undefined && Number.isFinite(Number(thresholds[key])));
}
