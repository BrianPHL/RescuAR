import React from 'react';
import { History } from 'lucide-react';

export default function SystemLogs() {
  const logs = [
    '[08:42:01] INFO [TelemetryProcessor] Received reading from Station 1: 16.20m',
    '[08:42:01] WARNING [AlertEngine] Level 16.20m exceeds threshold Level 2 (16.00m)',
    '[08:41:30] INFO [TelemetryProcessor] Received reading from Station 2: 15.40m',
    '[08:40:02] INFO [DBSync] Database sync successful (12ms write delay)',
    '[08:37:15] INFO [TelemetryProcessor] Received reading from Station 3: 14.90m',
    '[08:30:00] INFO [Scheduler] Broadcast job queue checked - 0 active, 1 completed',
    '[08:15:05] ERROR [TelemetryReceiver] Connection timeout on Station 4. Device offline.'
  ];

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Logs</h1>
          <span className="view-subtitle">Raw diagnostics console of early warning processes</span>
        </div>
      </div>

      <div className="stations-card">
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <History size={18} style={{ color: 'var(--color-brand)' }} />
          <span>Receiver Diagnostics Console</span>
        </h2>
        <div
          style={{
            backgroundColor: '#0f172a',
            color: '#38bdf8',
            fontFamily: 'monospace',
            fontSize: '12px',
            padding: '20px',
            borderRadius: 'var(--radius-md)',
            maxHeight: '400px',
            overflowY: 'auto',
            textAlign: 'left',
            lineHeight: '1.6'
          }}
        >
          {logs.map((log, i) => (
            <div key={i} style={{ borderBottom: '1px solid #1e293b', paddingBottom: '4px', marginBottom: '4px' }}>
              {log}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
