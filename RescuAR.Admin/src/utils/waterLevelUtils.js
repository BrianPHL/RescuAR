// Fallback thresholds are retained only for backward compatibility and UI resilience.
// Normal operation should use alert/alarm/critical thresholds supplied by PAGASA.
export const FALLBACK_THRESHOLDS = {
  'Rodriguez Station': { ALARM_1: 28.8, ALARM_2: 29.8, ALARM_3: 30.7 },
  'Montalban Station': { ALARM_1: 22.4, ALARM_2: 23.0, ALARM_3: 23.6 },
  'Nangka Station': { ALARM_1: 16.5, ALARM_2: 17.1, ALARM_3: 17.7 },
  'Sto. Niño Station': { ALARM_1: 15.0, ALARM_2: 16.0, ALARM_3: 17.0 },
  'Tumana Station': { ALARM_1: 17.26, ALARM_2: 18.26, ALARM_3: 19.26 }
};

function numberOrNull(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function getStationThresholds(stationName = '', sourceThresholds = null) {
  const alert = numberOrNull(sourceThresholds?.alert_threshold_m ?? sourceThresholds?.alert);
  const alarm = numberOrNull(sourceThresholds?.alarm_threshold_m ?? sourceThresholds?.alarm);
  const critical = numberOrNull(sourceThresholds?.critical_threshold_m ?? sourceThresholds?.critical);

  if (alert !== null || alarm !== null || critical !== null) {
    return {
      ALARM_1: alert,
      ALARM_2: alarm,
      ALARM_3: critical,
      source: 'PAGASA'
    };
  }

  const exact = FALLBACK_THRESHOLDS[stationName];
  if (exact) return { ...exact, source: 'Fallback' };

  const name = String(stationName).toLowerCase();
  const match = Object.entries(FALLBACK_THRESHOLDS).find(([key]) =>
    name.includes(key.replace(' Station', '').toLowerCase())
  );

  return match
    ? { ...match[1], source: 'Fallback' }
    : { ALARM_1: null, ALARM_2: null, ALARM_3: null, source: 'Unavailable' };
}

export function calculateAlertStatus(level, stationName = '', sourceThresholds = null) {
  const numericLevel = numberOrNull(level);
  if (numericLevel === null) {
    return {
      status: 'Unavailable', label: 'Unavailable', severity: 'danger', color: '#b91c1c',
      badgeClass: 'status-badge-unavailable'
    };
  }

  const thresholds = getStationThresholds(stationName, sourceThresholds);

  if (thresholds.ALARM_3 !== null && numericLevel >= thresholds.ALARM_3) {
    return {
      status: 'Critical', label: 'Critical Level', severity: 'danger', color: '#dc2626',
      badgeClass: 'status-badge-3rd-alarm'
    };
  }
  if (thresholds.ALARM_2 !== null && numericLevel >= thresholds.ALARM_2) {
    return {
      status: 'Alarm', label: 'Alarm Level', severity: 'warning', color: '#ea580c',
      badgeClass: 'status-badge-2nd-alarm'
    };
  }
  if (thresholds.ALARM_1 !== null && numericLevel >= thresholds.ALARM_1) {
    return {
      status: 'Alert', label: 'Alert Level', severity: 'info', color: '#ca8a04',
      badgeClass: 'status-badge-1st-alarm'
    };
  }

  return {
    status: 'Normal', label: 'Normal Level', severity: 'success', color: '#16a34a',
    badgeClass: 'status-badge-normal'
  };
}

export function formatTrend(trend) {
  if (trend === 'Rising') return { label: 'Rising', symbol: '↑', color: '#dc2626' };
  if (trend === 'Falling') return { label: 'Falling', symbol: '↓', color: '#0284c7' };
  if (trend === 'Stable') return { label: 'Stable', symbol: '→', color: '#64748b' };
  return { label: 'Unknown', symbol: '—', color: '#64748b' };
}
