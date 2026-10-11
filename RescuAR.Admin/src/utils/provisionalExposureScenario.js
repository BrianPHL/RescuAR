import { calculateAlertStatus } from './waterLevelUtils.js';

// Provisional project rule only. These mappings are NOT a calibrated hydraulic equivalence
// between a PAGASA gauge elevation and a LiPAD/UP DREAM return-period flood polygon.
export const PROVISIONAL_EXPOSURE_RULES = Object.freeze({
  Alert: Object.freeze({
    returnPeriod: 5,
    minimumHazard: 'Low',
    shortLabel: '5-Year • Low+',
    explanation: 'PAGASA Alert level reached → show the 5-year reference hazard scenario at Low or worse.'
  }),
  Alarm: Object.freeze({
    returnPeriod: 25,
    minimumHazard: 'Medium',
    shortLabel: '25-Year • Medium+',
    explanation: 'PAGASA Alarm level reached → show the 25-year reference hazard scenario at Medium or worse.'
  }),
  Critical: Object.freeze({
    returnPeriod: 100,
    minimumHazard: 'High',
    shortLabel: '100-Year • High',
    explanation: 'PAGASA Critical level reached → show the 100-year reference hazard scenario at High severity.'
  })
});

function numberOrNull(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function deriveProvisionalExposureScenario({
  telemetry,
  freshness,
  sourceStatus
} = {}) {
  const level = numberOrNull(telemetry?.level);
  const stationName = telemetry?.station_name || '';
  const alertStatus = calculateAlertStatus(level, stationName, telemetry);

  if (!telemetry || level === null) {
    return {
      active: false,
      state: 'unavailable',
      status: 'Unavailable',
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'No automatic scenario',
      reason: 'No verified PAGASA station observation is available.'
    };
  }

  const workerHealthy = sourceStatus?.worker_status === 'healthy';
  const sourceReachable = sourceStatus?.source_status === 'reachable';
  const telemetryFresh = Boolean(freshness?.isUsableAsLive);

  if (!workerHealthy || !sourceReachable) {
    return {
      active: false,
      state: 'suspended',
      status: alertStatus.status,
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'Automatic mapping suspended',
      reason: 'Automatic scenario selection is suspended because the PAGASA worker/source is not currently healthy and reachable.'
    };
  }

  if (!telemetryFresh) {
    return {
      active: false,
      state: 'suspended',
      status: alertStatus.status,
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'Automatic mapping suspended',
      reason: `Automatic scenario selection requires Fresh telemetry. Current freshness: ${freshness?.label || 'Unavailable'}.`
    };
  }

  if (alertStatus.status === 'Normal') {
    return {
      active: false,
      state: 'monitoring',
      status: 'Normal',
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'Monitoring only',
      reason: 'The verified PAGASA level is below Alert. No automatic reference-exposure escalation is activated.'
    };
  }

  const rule = PROVISIONAL_EXPOSURE_RULES[alertStatus.status];
  if (!rule) {
    return {
      active: false,
      state: 'unavailable',
      status: alertStatus.status,
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'No automatic scenario',
      reason: 'No provisional reference rule is configured for the current PAGASA status.'
    };
  }

  return {
    active: true,
    state: 'active',
    status: alertStatus.status,
    ...rule,
    reason: rule.explanation
  };
}

export function describeProvisionalRules() {
  return [
    { condition: 'Below Alert', scenario: 'Monitoring only', severity: 'No automatic exposure request' },
    { condition: 'Alert', scenario: '5-Year', severity: 'Low or worse' },
    { condition: 'Alarm', scenario: '25-Year', severity: 'Medium or worse' },
    { condition: 'Critical', scenario: '100-Year', severity: 'High only' }
  ];
}
