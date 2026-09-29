// Telemetry freshness policy is aligned with the scraper cadence and project documentation:
// - scraper runs every 5 minutes
// - two missed/late cycles (10 minutes) => Stale
// - prolonged absence of verified telemetry (30 minutes) => Unavailable
export const TELEMETRY_FRESH_MAX_MS = 10 * 60 * 1000;
export const TELEMETRY_UNAVAILABLE_AFTER_MS = 30 * 60 * 1000;

export function formatTelemetryAge(ageMs) {
  if (!Number.isFinite(ageMs) || ageMs < 0) return 'unknown age';

  const totalSeconds = Math.floor(ageMs / 1000);
  if (totalSeconds < 60) return 'just now';

  const totalMinutes = Math.floor(totalSeconds / 60);
  if (totalMinutes < 60) return `${totalMinutes} min ago`;

  const totalHours = Math.floor(totalMinutes / 60);
  const remainingMinutes = totalMinutes % 60;
  if (totalHours < 24) {
    return remainingMinutes > 0
      ? `${totalHours} hr ${remainingMinutes} min ago`
      : `${totalHours} hr ago`;
  }

  const totalDays = Math.floor(totalHours / 24);
  return `${totalDays} day${totalDays === 1 ? '' : 's'} ago`;
}

export function getTelemetryFreshness(updatedAt, nowMs = Date.now()) {
  if (!updatedAt) {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description: 'No timestamp received',
      ageMs: null,
      ageText: 'No timestamp',
      color: '#b91c1c',
      backgroundColor: '#fef2f2',
      borderColor: '#fecaca',
      dotColor: '#dc2626',
      isFresh: false,
      isUsableAsLive: false
    };
  }

  const timestampMs = new Date(updatedAt).getTime();
  if (!Number.isFinite(timestampMs)) {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description: 'Invalid telemetry timestamp',
      ageMs: null,
      ageText: 'Invalid timestamp',
      color: '#b91c1c',
      backgroundColor: '#fef2f2',
      borderColor: '#fecaca',
      dotColor: '#dc2626',
      isFresh: false,
      isUsableAsLive: false
    };
  }

  // Allow small clock differences without marking a record invalid.
  const ageMs = Math.max(0, nowMs - timestampMs);
  const ageText = formatTelemetryAge(ageMs);

  if (ageMs <= TELEMETRY_FRESH_MAX_MS) {
    return {
      status: 'fresh',
      label: 'Fresh',
      description: 'Telemetry is within the expected update window',
      ageMs,
      ageText,
      color: '#15803d',
      backgroundColor: '#f0fdf4',
      borderColor: '#bbf7d0',
      dotColor: '#16a34a',
      isFresh: true,
      isUsableAsLive: true
    };
  }

  if (ageMs <= TELEMETRY_UNAVAILABLE_AFTER_MS) {
    return {
      status: 'stale',
      label: 'Stale',
      description: 'Telemetry missed the expected update window',
      ageMs,
      ageText,
      color: '#a16207',
      backgroundColor: '#fefce8',
      borderColor: '#fde68a',
      dotColor: '#ca8a04',
      isFresh: false,
      isUsableAsLive: false
    };
  }

  return {
    status: 'unavailable',
    label: 'Unavailable',
    description: 'No verified telemetry for more than 30 minutes',
    ageMs,
    ageText,
    color: '#b91c1c',
    backgroundColor: '#fef2f2',
    borderColor: '#fecaca',
    dotColor: '#dc2626',
    isFresh: false,
    isUsableAsLive: false
  };
}
