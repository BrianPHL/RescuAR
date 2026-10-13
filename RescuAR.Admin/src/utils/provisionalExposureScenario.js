import { calculateAlertStatus } from './waterLevelUtils.js';

// Safe fallback only. An approved PREP calibration configuration can replace
// these mappings at runtime through GET /v1/exposure/rules.
export const PROVISIONAL_EXPOSURE_RULES = Object.freeze({
  Alert: Object.freeze({
    returnPeriod: 5,
    minimumHazard: 'Low'
  }),
  Alarm: Object.freeze({
    returnPeriod: 25,
    minimumHazard: 'Medium'
  }),
  Critical: Object.freeze({
    returnPeriod: 100,
    minimumHazard: 'High'
  })
});

function numberOrNull(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function normalizeRule(rule, fallback) {
  const returnPeriod = Number(rule?.return_period ?? rule?.returnPeriod);
  const minimumHazard = String(rule?.minimum_hazard ?? rule?.minimumHazard ?? '').trim();
  const validReturnPeriod = [5, 25, 100].includes(returnPeriod);
  const validHazard = ['Low', 'Medium', 'High'].includes(minimumHazard);

  if (!validReturnPeriod || !validHazard) return fallback;
  return { returnPeriod, minimumHazard };
}

export function normalizeExposureRuleConfig(payload) {
  const config = payload?.config ?? payload ?? {};
  const supplied = config?.rules ?? {};

  return {
    version: config?.version || 'provisional-1.0.0',
    approved: Boolean(config?.approved),
    source: config?.source || 'project_provisional_rule',
    calibrationEventCount: Number(config?.calibration_event_count || 0),
    holdoutEventCount: Number(config?.holdout_event_count || 0),
    rules: {
      Alert: normalizeRule(supplied.Alert, PROVISIONAL_EXPOSURE_RULES.Alert),
      Alarm: normalizeRule(supplied.Alarm, PROVISIONAL_EXPOSURE_RULES.Alarm),
      Critical: normalizeRule(supplied.Critical, PROVISIONAL_EXPOSURE_RULES.Critical),
    }
  };
}

export const DEFAULT_EXPOSURE_RULE_CONFIG = Object.freeze(
  normalizeExposureRuleConfig({
    approved: false,
    source: 'project_provisional_rule',
    rules: PROVISIONAL_EXPOSURE_RULES,
  })
);

function hazardPhrase(minimumHazard) {
  if (minimumHazard === 'High') return 'High only';
  if (minimumHazard === 'Medium') return 'Medium or worse';
  return 'Low or worse';
}

function shortRuleLabel(rule) {
  return `${rule.returnPeriod}-Year • ${rule.minimumHazard}${rule.minimumHazard === 'High' ? '' : '+'}`;
}

export function deriveProvisionalExposureScenario({
  telemetry,
  freshness,
  sourceStatus,
  ruleConfig = DEFAULT_EXPOSURE_RULE_CONFIG,
} = {}) {
  const normalizedConfig = normalizeExposureRuleConfig(ruleConfig);
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
      calibrated: normalizedConfig.approved,
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
      calibrated: normalizedConfig.approved,
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
      calibrated: normalizedConfig.approved,
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
      calibrated: normalizedConfig.approved,
      reason: 'The verified PAGASA level is below Alert. No automatic reference-exposure escalation is activated.'
    };
  }

  const rule = normalizedConfig.rules[alertStatus.status];
  if (!rule) {
    return {
      active: false,
      state: 'unavailable',
      status: alertStatus.status,
      returnPeriod: null,
      minimumHazard: null,
      shortLabel: 'No automatic scenario',
      calibrated: normalizedConfig.approved,
      reason: 'No reference rule is configured for the current PAGASA status.'
    };
  }

  const sourceLabel = normalizedConfig.approved
    ? `Approved calibrated mapping (${normalizedConfig.version})`
    : 'Provisional project mapping';

  return {
    active: true,
    state: 'active',
    status: alertStatus.status,
    returnPeriod: rule.returnPeriod,
    minimumHazard: rule.minimumHazard,
    shortLabel: shortRuleLabel(rule),
    calibrated: normalizedConfig.approved,
    ruleSource: normalizedConfig.source,
    ruleVersion: normalizedConfig.version,
    reason: `${sourceLabel}: PAGASA ${alertStatus.status} → ${rule.returnPeriod}-year reference scenario at ${hazardPhrase(rule.minimumHazard)}.`
  };
}

export function describeProvisionalRules(ruleConfig = DEFAULT_EXPOSURE_RULE_CONFIG) {
  const normalizedConfig = normalizeExposureRuleConfig(ruleConfig);
  const rows = [
    { condition: 'Below Alert', scenario: 'Monitoring only', severity: 'No automatic exposure request' }
  ];

  for (const condition of ['Alert', 'Alarm', 'Critical']) {
    const rule = normalizedConfig.rules[condition];
    rows.push({
      condition,
      scenario: `${rule.returnPeriod}-Year`,
      severity: hazardPhrase(rule.minimumHazard)
    });
  }

  return rows;
}
