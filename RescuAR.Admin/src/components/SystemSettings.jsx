import React, { useState } from 'react';
import { Sliders, Settings, Save } from 'lucide-react';

export default function SystemSettings() {
  const [thresholds, setThresholds] = useState({ lvl1: 15.0, lvl2: 16.0, lvl3: 18.0 });
  const [frequency, setFrequency] = useState(10);
  const [saved, setSaved] = useState(false);

  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Configuration</h1>
          <span className="view-subtitle">Adjust core thresholds and telemetry configurations</span>
        </div>
      </div>

      <div className="stations-split-layout">
        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Sliders size={18} style={{ color: 'var(--color-brand)' }} />
            <span>Alert Threshold Parameters</span>
          </h2>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', marginTop: '8px' }}>
            <div className="form-group">
              <label className="form-label">Alert Level 1 (meters)</label>
              <input
                type="number"
                className="form-input"
                value={thresholds.lvl1}
                onChange={(e) => setThresholds({ ...thresholds, lvl1: parseFloat(e.target.value) })}
              />
            </div>

            <div className="form-group">
              <label className="form-label">Alert Level 2 (meters)</label>
              <input
                type="number"
                className="form-input"
                value={thresholds.lvl2}
                onChange={(e) => setThresholds({ ...thresholds, lvl2: parseFloat(e.target.value) })}
              />
            </div>

            <div className="form-group">
              <label className="form-label">Alert Level 3 (meters)</label>
              <input
                type="number"
                className="form-input"
                value={thresholds.lvl3}
                onChange={(e) => setThresholds({ ...thresholds, lvl3: parseFloat(e.target.value) })}
              />
            </div>

            <button className="btn-primary" onClick={handleSave} style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px', marginTop: '8px' }}>
              <Save size={16} />
              <span>{saved ? 'Settings Saved!' : 'Save Configuration'}</span>
            </button>
          </div>
        </div>

        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Settings size={18} style={{ color: 'var(--color-brand)' }} />
            <span>Telemetry Server Configurations</span>
          </h2>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', marginTop: '8px' }}>
            <div className="form-group">
              <label className="form-label">Data Query Frequency (Minutes)</label>
              <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                <input
                  type="range"
                  min="5"
                  max="60"
                  step="5"
                  value={frequency}
                  onChange={(e) => setFrequency(parseInt(e.target.value))}
                  style={{ flex: 1 }}
                />
                <span style={{ fontWeight: '700', fontSize: '14px', width: '50px' }}>{frequency}m</span>
              </div>
            </div>

            <div className="form-group">
              <label className="form-label">Telemetry Receiver Webhook URL</label>
              <input
                type="text"
                className="form-input"
                value="https://api.rescuar.net/v1/telemetry/receiver"
                readOnly
                style={{ backgroundColor: '#f1f5f9', color: '#64748b' }}
              />
            </div>

            <div className="form-group">
              <label className="form-label">SMS Gateway API Key</label>
              <input
                type="password"
                className="form-input"
                value="••••••••••••••••••••••••••••••••"
                readOnly
                style={{ backgroundColor: '#f1f5f9', color: '#64748b' }}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
