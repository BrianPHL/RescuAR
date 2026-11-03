import React from 'react';
import { BookOpen } from 'lucide-react';

export default function Documentation() {
  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Documentation</h1>
          <span className="view-subtitle">Current monitoring, exposure and messaging behavior</span>
        </div>
      </div>

      <div className="stations-card" style={{ fontSize: '13px', lineHeight: '1.6', color: 'var(--text-muted)' }}>
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--text-main)' }}>
          <BookOpen size={18} style={{ color: 'var(--color-brand)' }} />
          <span>System Operating Guide</span>
        </h2>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', marginTop: '12px' }}>
          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>1. Water Level Updates and Freshness</h3>
            <p>
              The telemetry worker checks PAGASA every five minutes. Reading age is measured from the source observation time, not the latest check. A reading is Fresh up to 15 minutes old, Delayed over 15 through 30 minutes, Stale over 30 through 60 minutes, and Unavailable over 60 minutes or without a valid observation time. Only Fresh readings are treated as live. Worker health and reading freshness are shown separately.
            </p>
          </div>

          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>2. Alert Levels and Hazard Exposure</h3>
            <p>
              Monitoring classifies readings as Normal, Alert, Alarm or Critical using station-specific PAGASA thresholds when available, with stored fallback thresholds otherwise. Read the selected station&apos;s thresholds and freshness together. A status change does not automatically issue an evacuation order or activate a siren. Follow the responsible authority&apos;s confirmed instructions.
            </p>
            <p>
              Flood Hazard Exposure estimates population and facilities within selected reference hazard maps. It is not a live inundation forecast or an observed count of affected residents. Automatic mappings use provisional rules unless an approved configuration is loaded; check the page&apos;s configuration and data warnings before interpreting an estimate.
            </p>
          </div>

          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>3. SMS Queue and Broadcast Simulation</h3>
            <p>
              Where your account has access, SMS Parser opens the Outbound SMS Gateway. It queues messages for a separately configured SIM handset. Queued means waiting for processing; sent means the handset reported submission to the network, not confirmed recipient delivery. The separate Notify Residents broadcast window is a simulation: its SMS, push and siren choices do not send alerts.
            </p>
          </div>

          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>4. Settings and Logs</h3>
            <p>
              System Settings currently keeps changes on screen without saving them to the server or changing telemetry collection. System Logs displays sample entries rather than live operational or audit history. Use confirmed service and gateway records to establish what actually occurred.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
