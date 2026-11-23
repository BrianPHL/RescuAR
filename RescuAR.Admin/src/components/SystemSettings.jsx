import React from 'react';
import { Settings, ShieldAlert, Activity } from 'lucide-react';

// Phase 1: Do not expose controls that cannot persist or apply to the worker.
// Effective thresholds must be verified separately for each PAGASA gauge.
export default function SystemSettings() {
  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Configuration</h1>
          <span className="view-subtitle">Operational configuration — read-only until validated backend updates are available</span>
        </div>
      </div>
      <div className="stations-split-layout">
        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <ShieldAlert size={18} /> Alert Thresholds
          </h2>
          <div role="status" style={{ padding: '12px', border: '1px solid #f59e0b', borderRadius: '8px', background: '#fffbeb' }}>
            No editable global thresholds are available. PAGASA alert, alarm, and critical
            elevations must be verified <strong>for each station</strong> before they can
            be used for a flood-risk classification.
          </div>
          <p style={{ fontSize: '13px', marginTop: '12px' }}>
            Refer to Monitoring Stations for the actual recorded measurements and
            per-gauge threshold verification state. A missing threshold is not a
            Normal or safe condition.
          </p>
        </div>
        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Activity size={18} /> PAGASA Telemetry Worker
          </h2>
          <dl style={{ fontSize: '13px' }}>
            <dt>Configured polling schedule</dt>
            <dd>Every 5 minutes (worker source configuration)</dd>
            <dt>Change method</dt>
            <dd>Update the Railway worker configuration/code, redeploy, and verify its logs.</dd>
            <dt>Actual connection and latest observation</dt>
            <dd>See Monitoring Stations; this page does not claim live connectivity.</dd>
          </dl>
          <p style={{ fontSize: '12px', color: 'var(--text-muted)' }}>
            API keys and service-role secrets are managed through server-side environment
            variables; they are not viewable or editable in this browser.
          </p>
        </div>
      </div>
      <div className="stations-card" style={{ marginTop: '16px' }}>
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}><Settings size={18} /> Configuration Updates</h2>
        <p style={{ fontSize: '13px' }}>
          Saving configuration from this page is disabled until protected persistence,
          approval, worker acknowledgment, and audit logging are implemented. No changes
          can be applied from this screen in the current version.
        </p>
      </div>
    </div>
  );
}
