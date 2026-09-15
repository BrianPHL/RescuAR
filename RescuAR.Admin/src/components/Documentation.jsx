import React from 'react';
import { BookOpen } from 'lucide-react';

export default function Documentation() {
  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Documentation</h1>
          <span className="view-subtitle">Guide to operations and early warning protocols</span>
        </div>
      </div>

      <div className="stations-card" style={{ fontSize: '13px', lineHeight: '1.6', color: 'var(--text-muted)' }}>
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--text-main)' }}>
          <BookOpen size={18} style={{ color: 'var(--color-brand)' }} />
          <span>Operational Protocols</span>
        </h2>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', marginTop: '12px' }}>
          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>1. Water Level Reading Frequencies</h3>
            <p>
              Sensor nodes are configured to publish readings every 10 minutes. If a sensor node fails to connect for 3 consecutive cycles (30 minutes), it will report as **Offline** in the monitoring panel.
            </p>
          </div>

          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>2. Alert Levels and Action Rules</h3>
            <ul style={{ paddingLeft: '20px' }}>
              <li><strong>Alert Level 1 (15.0m):</strong> Yellow Warning. Information broadcast to residents. System begins monitoring at 5-minute cycles.</li>
              <li><strong>Alert Level 2 (16.0m):</strong> Orange Warning. Residents are advised to prepare go-bags and secure electrical units. Preparedness centers initialized.</li>
              <li><strong>Alert Level 3 (18.0m):</strong> Red Warning. Mandated evacuation is declared. sirens and broadcast systems broadcast evacuation paths.</li>
            </ul>
          </div>

          <div>
            <h3 style={{ color: 'var(--text-main)', fontSize: '14px', fontWeight: '700', marginBottom: '4px' }}>3. Battery Maintenance Guidelines</h3>
            <p>
              When a station reports battery levels below 20%, immediate site dispatch is required to replace the secondary lead-acid battery cells or clean solar panel surfaces.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
