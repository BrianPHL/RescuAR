// PAGASA's detail feed is observed at roughly 10-minute intervals while the worker checks every 5 minutes.
// Freshness is based on the official PAGASA observation timestamp, never the scraper execution time.
export const TELEMETRY_FRESH_MAX_MS = 15 * 60 * 1000;
export const TELEMETRY_DELAYED_MAX_MS = 30 * 60 * 1000;
export const TELEMETRY_STALE_MAX_MS = 60 * 60 * 1000;

export function formatTelemetryAge(ageMs) {
  if (!Number.isFinite(ageMs) || ageMs < 0) return 'unknown age';
  const totalMinutes = Math.floor(ageMs / 60000);
  if (totalMinutes < 1) return 'just now';
  if (totalMinutes < 60) return `${totalMinutes} min ago`;
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours < 24) return minutes ? `${hours} hr ${minutes} min ago` : `${hours} hr ago`;
  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? '' : 's'} ago`;
}

function unavailable(description, ageText = 'No timestamp') {
  return {
    status: 'unavailable', label: 'Data Unavailable', description, ageMs: null, ageText,
    color: '#475569', backgroundColor: '#f8fafc', borderColor: '#cbd5e1', dotColor: '#64748b',
    isFresh: false, isUsableAsLive: false
  };
}

export function getTelemetryFreshness(observedAt, nowMs = Date.now()) {
  if (!observedAt) return unavailable('No verified PAGASA observation timestamp received');
  const timestampMs = new Date(observedAt).getTime();
  if (!Number.isFinite(timestampMs)) return unavailable('Invalid PAGASA observation timestamp', 'Invalid timestamp');

  if (timestampMs > nowMs + 5 * 60 * 1000) {
    return unavailable('PAGASA observation timestamp is unexpectedly in the future', 'Future timestamp');
  }
  const ageMs = Math.max(0, nowMs - timestampMs);
  const ageText = formatTelemetryAge(ageMs);

  if (ageMs <= TELEMETRY_FRESH_MAX_MS) {
    return { status: 'fresh', label: 'Updated', description: 'Latest verified observation is within the current 15-minute display window', ageMs, ageText,
      color: '#15803d', backgroundColor: '#f0fdf4', borderColor: '#bbf7d0', dotColor: '#16a34a', isFresh: true, isUsableAsLive: true };
  }
  if (ageMs <= TELEMETRY_DELAYED_MAX_MS) {
    return { status: 'delayed', label: 'Awaiting PAGASA Update', description: 'The latest verified observation is 15–30 minutes old; this does not itself indicate rising flood risk', ageMs, ageText,
      color: '#a16207', backgroundColor: '#fefce8', borderColor: '#fde68a', dotColor: '#ca8a04', isFresh: false, isUsableAsLive: false };
  }
  if (ageMs <= TELEMETRY_STALE_MAX_MS) {
    return { status: 'stale', label: 'Outdated Reading', description: 'The latest verified observation is 30–60 minutes old; do not treat it as a current flood-risk reading', ageMs, ageText,
      color: '#92400e', backgroundColor: '#fffbeb', borderColor: '#fde68a', dotColor: '#d97706', isFresh: false, isUsableAsLive: false };
  }

  return { status: 'unavailable', label: 'Data Unavailable', description: 'No verified PAGASA observation within the last hour; this does not establish current flood conditions', ageMs, ageText,
    color: '#475569', backgroundColor: '#f8fafc', borderColor: '#cbd5e1', dotColor: '#64748b', isFresh: false, isUsableAsLive: false };
}
