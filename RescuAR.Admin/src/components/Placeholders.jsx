import React, { useState } from 'react';
import {
  Users,
  Send,
  Settings,
  History,
  FileText,
  BookOpen,
  Check,
  Save,
  Sliders,
  Search,
  Phone
} from 'lucide-react';

/* --- RESIDENTS DIRECTORY COMPONENT --- */
export function ResidentsDirectory() {
  const [searchTerm, setSearchTerm] = useState('');
  const residents = [
    { name: 'Kagawad Juan Dela Cruz', barangay: 'Tumana', role: 'Barangay Coordinator', phone: '+63 917 123 4567' },
    { name: 'Maria Santos', barangay: 'Nangka', role: 'Zone Captain', phone: '+63 918 234 5678' },
    { name: 'Antonio Luna', barangay: 'Provident', role: 'LGU Responder', phone: '+63 920 345 6789' },
    { name: 'Teresa Rizal', barangay: 'Malanday', role: 'Local Coordinator', phone: '+63 915 456 7890' },
    { name: 'Emilio Jacinto', barangay: 'Tumana', role: 'Zone Leader', phone: '+63 909 567 8901' }
  ];

  const filtered = residents.filter(r =>
    r.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    r.barangay.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Residents Directory</h1>
          <span className="view-subtitle">Primary emergency response contacts in high-risk zones</span>
        </div>
      </div>

      <div className="stations-card">
        <div style={{ display: 'flex', justifyBetween: 'space-between', alignItems: 'center' }}>
          <h2 className="stations-card-title">Emergency Coordinators List</h2>
        </div>

        <div className="search-input-wrapper">
          <input
            type="text"
            placeholder="Search by name or barangay..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
        </div>

        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Coordinator Name</th>
                <th>Barangay Sector</th>
                <th>Designated Role</th>
                <th>Contact Info</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((r, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: '600' }}>{r.name}</td>
                  <td>{r.barangay}</td>
                  <td><span style={{ backgroundColor: '#e0f2fe', color: '#0369a1', padding: '3px 8px', borderRadius: '10px', fontSize: '11px', fontWeight: '700' }}>{r.role}</span></td>
                  <td style={{ fontFamily: 'monospace' }}>{r.phone}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

/* --- BROADCAST LOGS COMPONENT --- */
export function BroadcastLogs() {
  const logs = [
    { date: 'June 18, 2026 - 08:30 AM', target: 'Tumana, Nangka', message: 'Alert Level 2 warning. Please begin packing essential belongings.', status: 'Delivered (1,240 SMS)', type: 'warning' },
    { date: 'June 15, 2026 - 02:15 PM', target: 'All Sectors', message: 'Routine test: early alert system is operational.', status: 'Delivered (1,420 SMS)', type: 'info' },
    { date: 'June 02, 2026 - 09:00 AM', target: 'Provident', message: 'Water level warning: River level reached 14.8m.', status: 'Delivered (350 SMS)', type: 'warning' }
  ];

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Broadcast Logs</h1>
          <span className="view-subtitle">History of emergency text/alert broadcasts sent to the community</span>
        </div>
      </div>

      <div className="stations-card">
        <h2 className="stations-card-title">Recent Broadcast History</h2>
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Date & Time</th>
                <th>Recipients</th>
                <th>Broadcast Message</th>
                <th>Delivery Status</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((log, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: '600', whiteSpace: 'nowrap' }}>{log.date}</td>
                  <td>{log.target}</td>
                  <td>{log.message}</td>
                  <td>
                    <span style={{
                      color: 'var(--color-online)',
                      backgroundColor: 'var(--color-online-bg)',
                      padding: '3px 8px',
                      borderRadius: '12px',
                      fontSize: '11px',
                      fontWeight: '700',
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '4px'
                    }}>
                      <Check size={12} />
                      {log.status}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

/* --- SYSTEM CONFIGURATION COMPONENT --- */
export function SystemSettings() {
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

/* --- SYSTEM LOGS COMPONENT --- */
export function SystemLogs() {
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

/* --- ADVISORIES COMPONENT --- */
export function Advisories() {
  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Advisories Template</h1>
          <span className="view-subtitle">Manage communication templates for public distribution</span>
        </div>
      </div>

      <div className="stations-card">
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <FileText size={18} style={{ color: 'var(--color-brand)' }} />
          <span>Active Alert Level 2 Template</span>
        </h2>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '8px' }}>
          <div className="form-group">
            <label className="form-label">Advisory Subject Header</label>
            <input type="text" className="form-input" value="PUBLIC WARNING: Marikina River Alert Level 2 (PREPARE)" readOnly />
          </div>
          <div className="form-group">
            <label className="form-label">Advisory Message Content</label>
            <textarea className="form-textarea" readOnly value={`RESIDENTS IN BARANGAYS TUMANA & NANGKA:

Please be advised that the Marikina River has reached Alert Level 2 (16.2 meters). Water levels are expected to continue rising. 

Action required:
1. Secure all electrical equipment.
2. Prepare emergency go-bags with medicines, flashlights, water, and food.
3. Be ready for evacuation if Alert Level 3 (18.0m) is declared.

LGU Responders are on standby. Stay tuned for further announcements.

- Marikina Disaster Risk Reduction & Management Office (MDRRMO)`}></textarea>
          </div>
        </div>
      </div>
    </div>
  );
}

/* --- DOCUMENTATION COMPONENT --- */
export function Documentation() {
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

/* --- EMERGENCY HOTLINES COMPONENT --- */
export function EmergencyHotlines() {
  const hotlines = [
    { name: 'Marikina Rescue 161', category: 'General Emergency / Rescue', number: '161 / 0917-809-5141', active: true },
    { name: 'Marikina Police Station (PNP)', category: 'Law Enforcement', number: '8646-1631', active: true },
    { name: 'Marikina Fire Station (BFP)', category: 'Fire & Rescue', number: '8646-0427', active: true },
    { name: 'NDRRMC / MDRRMO', category: 'Disaster Risk Management', number: '8646-2423', active: true },
    { name: 'Philippine Red Cross - Marikina', category: 'Medical Emergency', number: '8475-1011', active: true },
    { name: 'MERALCO (Marikina Branch)', category: 'Utility Services', number: '16211', active: true }
  ];

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Emergency Hotlines</h1>
          <span className="view-subtitle">Official emergency contact directory for Marikina City</span>
        </div>
      </div>

      <div className="stations-card">
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <Phone size={18} style={{ color: 'var(--color-brand)' }} />
          <span>Marikina City Critical Contacts</span>
        </h2>

        <div className="table-container" style={{ marginTop: '16px' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>Agency / Department</th>
                <th>Category</th>
                <th>Hotline Number</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {hotlines.map((h, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: '600', color: 'var(--text-main)' }}>{h.name}</td>
                  <td>{h.category}</td>
                  <td style={{ fontFamily: 'monospace', fontWeight: '600', fontSize: '14px', color: 'var(--primary)' }}>{h.number}</td>
                  <td>
                    {h.active ? (
                      <span style={{ backgroundColor: '#ecfdf5', color: '#059669', padding: '4px 10px', borderRadius: '12px', fontSize: '11px', fontWeight: '700' }}>Active</span>
                    ) : (
                      <span style={{ backgroundColor: '#fef2f2', color: '#dc2626', padding: '4px 10px', borderRadius: '12px', fontSize: '11px', fontWeight: '700' }}>Inactive</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
