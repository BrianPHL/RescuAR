import assert from 'node:assert/strict';
import { deriveProvisionalExposureScenario } from '../src/utils/provisionalExposureScenario.js';

const fresh = { isUsableAsLive: true, label: 'Fresh' };
const healthy = { worker_status: 'healthy', source_status: 'reachable' };

function telemetry(level) {
  return {
    station_name: 'Sto. Niño Station',
    level,
    alert_threshold_m: 15,
    alarm_threshold_m: 16,
    critical_threshold_m: 17
  };
}

assert.equal(deriveProvisionalExposureScenario({ telemetry: telemetry(14.9), freshness: fresh, sourceStatus: healthy }).active, false);

const alert = deriveProvisionalExposureScenario({ telemetry: telemetry(15.2), freshness: fresh, sourceStatus: healthy });
assert.deepEqual([alert.active, alert.returnPeriod, alert.minimumHazard], [true, 5, 'Low']);

const alarm = deriveProvisionalExposureScenario({ telemetry: telemetry(16.2), freshness: fresh, sourceStatus: healthy });
assert.deepEqual([alarm.active, alarm.returnPeriod, alarm.minimumHazard], [true, 25, 'Medium']);

const critical = deriveProvisionalExposureScenario({ telemetry: telemetry(17.2), freshness: fresh, sourceStatus: healthy });
assert.deepEqual([critical.active, critical.returnPeriod, critical.minimumHazard], [true, 100, 'High']);

const delayed = deriveProvisionalExposureScenario({ telemetry: telemetry(17.2), freshness: { isUsableAsLive: false, label: 'Delayed' }, sourceStatus: healthy });
assert.equal(delayed.active, false);
assert.equal(delayed.state, 'suspended');

const failedSource = deriveProvisionalExposureScenario({ telemetry: telemetry(17.2), freshness: fresh, sourceStatus: { worker_status: 'healthy', source_status: 'failed' } });
assert.equal(failedSource.active, false);
assert.equal(failedSource.state, 'suspended');

console.log('Option B provisional exposure rule tests passed.');

const calibratedRuleConfig = {
  version: 'test-calibrated',
  approved: true,
  source: 'test',
  rules: {
    Alert: { return_period: 25, minimum_hazard: 'Low' },
    Alarm: { return_period: 100, minimum_hazard: 'Low' },
    Critical: { return_period: 100, minimum_hazard: 'Medium' },
  }
};

const calibratedAlarm = deriveProvisionalExposureScenario({
  telemetry: telemetry(16.2),
  freshness: fresh,
  sourceStatus: healthy,
  ruleConfig: calibratedRuleConfig,
});
assert.deepEqual(
  [calibratedAlarm.active, calibratedAlarm.returnPeriod, calibratedAlarm.minimumHazard, calibratedAlarm.calibrated],
  [true, 100, 'Low', true]
);
console.log('Approved calibration rule override test passed.');
