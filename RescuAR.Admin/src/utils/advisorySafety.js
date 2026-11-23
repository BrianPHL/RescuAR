import { getTelemetryFreshness } from './telemetryFreshness.js';

// A reading may be included in an internal draft, but cannot itself establish
// affected barangays, an evacuation order, or authorization to publish.
export function buildSafeAdvisoryDraft(stationData, nowMs = Date.now()) {
  const stationName = typeof stationData?.stationName === 'string' && stationData.stationName.trim()
    ? stationData.stationName.trim() : 'Unspecified monitoring station';
  const rawLevel = stationData?.level;
  const level = rawLevel === null || rawLevel === undefined || rawLevel === '' ? NaN : Number(rawLevel);
  const hasLevel = Number.isFinite(level) && level >= 0;
  const freshness = getTelemetryFreshness(stationData?.observedAt, nowMs);
  const sourceVerified = stationData?.thresholdVerified === true;
  const assessment = sourceVerified && freshness.isUsableAsLive && hasLevel
    ? (['Alert', 'Alarm', 'Critical', 'Normal'].includes(stationData?.alertStatus)
        ? stationData.alertStatus : 'Not assessed')
    : 'Not assessed';

  const timeLabel = stationData?.observedAt && Number.isFinite(Date.parse(stationData.observedAt))
    ? new Date(stationData.observedAt).toISOString() : 'Unavailable';
  const readingLabel = hasLevel ? `${level.toFixed(2)} m` : 'Unavailable';
  return {
    subject: `DRAFT — Water-level information (${stationName})`,
    severity: 'Low', // Never infer the severity of an official advisory from a loose measurement.
    affectedAreas: '', // Geographical impact requires independent verification.
    message: [
      'INTERNAL DRAFT — NOT AN OFFICIAL FLOOD WARNING OR EVACUATION ORDER.',
      `Station: ${stationName}`,
      `Recorded water level: ${readingLabel}`,
      `PAGASA observation time (UTC): ${timeLabel}`,
      `Observation freshness: ${freshness.label}`,
      `Threshold-based assessment: ${assessment}`,
      '',
      'Affected areas: To be verified by authorized personnel.',
      'Review the official PAGASA data and LGU guidance before publishing.',
      'A water-level observation does not itself establish an evacuation order.'
    ].join('\n'),
    escalationActions: 'Follow instructions issued and verified by authorized local disaster-response officials.',
  };
}
