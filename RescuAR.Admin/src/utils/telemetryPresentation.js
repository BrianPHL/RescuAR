// Display-only helpers. They never alter the underlying freshness limits,
// PAGASA measurements, station thresholds, or dispatch permission rules.
import { formatTelemetryAge } from './telemetryFreshness.js';

export const RECENT_SOURCE_CHECK_MAX_MS = 15 * 60 * 1000;

export function getTelemetryConnectionState(sourceStatus, nowMs = Date.now()) {
  const checkedAtMs = sourceStatus?.last_check_at ? Date.parse(sourceStatus.last_check_at) : NaN;
  const ageMs = Number.isFinite(checkedAtMs) ? nowMs - checkedAtMs : null;
  const recentCheck = ageMs !== null && ageMs >= -5 * 60 * 1000 && ageMs <= RECENT_SOURCE_CHECK_MAX_MS;
  const sourceConnected = recentCheck && sourceStatus?.source_status === 'reachable';
  const workerReporting = recentCheck && sourceStatus?.worker_status === 'healthy';

  const sourceLabel = !recentCheck ? 'Connection Not Confirmed' : sourceConnected ? 'Connected' : 'Connection Issue';
  const workerLabel = !recentCheck ? 'No Recent Check' : workerReporting ? 'Reporting Normally' : 'Check Reported Issue';
  const checkAgeText = ageMs === null ? 'No check recorded' : formatTelemetryAge(Math.max(0, ageMs));
  const detail = !recentCheck
    ? 'No recent PAGASA source check is recorded. Previously saved water levels may still be visible, but current connectivity is not confirmed.'
    : !sourceConnected || !workerReporting
      ? `The latest PAGASA source check reported a problem${sourceStatus?.last_error_message ? `: ${sourceStatus.last_error_message}` : '.'} Previously verified observations remain available.`
      : 'The worker recently reached PAGASA. Observation timestamps may still lag behind the latest source check.';

  return { recentCheck, sourceConnected, workerReporting, sourceLabel, workerLabel, checkAgeText, detail, needsAttention: !sourceConnected || !workerReporting };
}

export function hasRecordedStationThresholds(row) {
  const values = ['alert_threshold_m', 'alarm_threshold_m', 'critical_threshold_m'].map((key) => {
    const raw = row?.[key];
    return raw === null || raw === undefined || raw === '' ? null : Number(raw);
  });
  return values.every(Number.isFinite) && values[0] > 0 && values[0] < values[1] && values[1] < values[2];
}

// A measured water elevation is not, by itself, a flood-risk classification.
// Do not paint missing thresholds, stale observations, or absent readings red
// as though an Alert/Alarm/Critical level had been reached.
export function getMonitoringLevelAssessment(level, freshness, thresholdsVerified, calculatedAlert) {
  if (!Number.isFinite(level)) {
    return { status: 'Unavailable', label: 'Level Not Available', color: '#64748b',
      explanation: 'No valid verified water-level value is available. Flood risk cannot be classified.' };
  }
  if (!thresholdsVerified) {
    return { status: 'Unverified', label: 'Thresholds Not Verified', color: '#a16207',
      explanation: 'The water level was recorded, but official thresholds for this gauge have not been verified. No flood-risk classification is available.' };
  }
  if (!freshness?.isUsableAsLive) {
    return { status: 'Unavailable', label: 'Risk Not Assessed', color: '#64748b',
      explanation: 'The latest verified observation is too old for a live flood-risk classification. The displayed level is historical.' };
  }
  return calculatedAlert;
}

export function getMonitoringTrend(trend, freshness) {
  if (!freshness?.isUsableAsLive || !['Rising', 'Falling', 'Stable'].includes(trend)) {
    return { symbol: '—', label: 'Insufficient Trend Data', color: '#64748b', available: false };
  }
  const symbols = { Rising: '↑', Falling: '↓', Stable: '→' };
  const colors = { Rising: '#dc2626', Falling: '#0284c7', Stable: '#64748b' };
  return { symbol: symbols[trend], label: trend, color: colors[trend], available: true };
}

// The historical MMDA gauge and the current 2nd gauge use independent
// identities. Only the enabled catalog mapping can drive live exposure status.
export const STO_NINO_KNOWN_CODES = Object.freeze(['11104201', '11104204']);

export function selectActiveStoNinoGauge(catalogRows, stationRows) {
  if (!Array.isArray(catalogRows) || !Array.isArray(stationRows)) return null;
  const enabled = catalogRows
    .filter((row) => row.enabled === true && STO_NINO_KNOWN_CODES.includes(String(row.station_code)))
    .sort((a, b) => Number(b.is_primary === true) - Number(a.is_primary === true));
  const selected = enabled[0];
  return selected
    ? stationRows.find((row) => String(row.station_code) === String(selected.station_code)) || null
    : null;
}
