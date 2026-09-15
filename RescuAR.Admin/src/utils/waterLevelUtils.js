export const MARIKINA_RIVER_THRESHOLDS = {
  ALARM_1: 15.0, // Monitoring
  ALARM_2: 16.0, // Preparation
  ALARM_3: 18.0, // Evacuation
};

export const RODRIGUEZ_RIVER_THRESHOLDS = {
  ALARM_1: 28.80, // Alert Level (First Warning - Prepares communities)
  ALARM_2: 29.80, // Alarm Level (Second Warning - Readiness for evacuation)
  ALARM_3: 30.70, // Critical Level (Action/Evacuation - Mandatory evacuation in high-risk zones)
};

export const NANGKA_RIVER_THRESHOLDS = {
  ALARM_1: 16.50, // First Alarm
  ALARM_2: 17.10, // Second Alarm
  ALARM_3: 17.70, // Third Alarm (Action / Evacuation)
};

export const MONTALBAN_SAN_JOSE_RIVER_THRESHOLDS = {
  ALARM_1: 22.40, // First Alarm
  ALARM_2: 23.00, // Second Alarm
  ALARM_3: 23.60, // Third Alarm (Evacuation)
};

/**
 * Returns station-specific alert threshold levels (meters)
 */
export function getStationThresholds(stationName = '') {
  const nameLower = (stationName || '').toLowerCase();
  if (nameLower.includes('rodriguez')) {
    return RODRIGUEZ_RIVER_THRESHOLDS;
  }
  if (nameLower.includes('nangka')) {
    return NANGKA_RIVER_THRESHOLDS;
  }
  if (nameLower.includes('san jose') || nameLower.includes('montalban')) {
    return MONTALBAN_SAN_JOSE_RIVER_THRESHOLDS;
  }
  return MARIKINA_RIVER_THRESHOLDS;
}

/**
 * Calculates unified alert status based on water level gauge reading (meters)
 */
export function calculateAlertStatus(level, stationName = 'Sto. Niño') {
  const numLevel = Number(level) || 0;
  const thresholds = getStationThresholds(stationName);

  if (numLevel >= thresholds.ALARM_3) {
    return {
      status: '3rd Alarm',
      label: '3rd Alarm (Evacuation)',
      severity: 'danger',
      color: '#dc2626',
      badgeClass: 'status-badge-3rd-alarm'
    };
  }

  if (numLevel >= thresholds.ALARM_2) {
    return {
      status: '2nd Alarm',
      label: '2nd Alarm (Preparation)',
      severity: 'warning',
      color: '#ea580c',
      badgeClass: 'status-badge-2nd-alarm'
    };
  }

  if (numLevel >= thresholds.ALARM_1) {
    return {
      status: '1st Alarm',
      label: '1st Alarm (Monitoring)',
      severity: 'info',
      color: '#ca8a04',
      badgeClass: 'status-badge-1st-alarm'
    };
  }

  return {
    status: 'Normal',
    label: 'Normal Level',
    severity: 'success',
    color: '#16a34a',
    badgeClass: 'status-badge-normal'
  };
}
