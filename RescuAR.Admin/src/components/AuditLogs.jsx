import React, { useState } from 'react';
import { 
  Search, 
  History, 
  FileText, 
  CheckCircle, 
  AlertTriangle, 
  User, 
  Server, 
  ChevronLeft, 
  ChevronRight,
  ShieldCheck,
  RefreshCw
} from 'lucide-react';

const INITIAL_LOGS = [
  {
    id: 'EVT-9045',
    timestamp: '2026-07-22 08:42:01',
    operator: 'Brian Pasco (LGU)',
    category: 'Advisory',
    details: 'Published Alert Level 2 warning advisory for Tumana & Nangka sectors.',
    ip: '192.168.12.44',
    hash: '8f7a9d...2c5b'
  },
  {
    id: 'EVT-9044',
    timestamp: '2026-07-22 08:30:15',
    operator: 'System Gateway',
    category: 'Broadcast',
    details: 'SMS broadcast transmitted to 1,240 subscribers. Delivery confirmation rate: 98.4%.',
    ip: 'Cloud-Agent',
    hash: 'a1b2c3...f4e5'
  },
  {
    id: 'EVT-9043',
    timestamp: '2026-07-22 08:15:00',
    operator: 'Telemetry Daemon',
    category: 'System',
    details: 'Low battery warning triggered for Station 4 (Provident) - battery at 12%.',
    ip: '10.0.4.12',
    hash: 'e6d7c8...a9b0'
  },
  {
    id: 'EVT-9042',
    timestamp: '2026-07-22 08:12:30',
    operator: 'Brian Pasco (LGU)',
    category: 'Configuration',
    details: 'Updated Alert Level 2 threshold from 16.0m to 16.2m.',
    ip: '192.168.12.44',
    hash: 'c4d5e6...7f8a'
  },
  {
    id: 'EVT-9041',
    timestamp: '2026-07-22 07:45:00',
    operator: 'System Gateway',
    category: 'Broadcast',
    details: 'SMS broadcast test packet sent to 4 quick coordinators. Status: Delivered.',
    ip: 'Cloud-Agent',
    hash: '3f4e5d...6c7b'
  },
  {
    id: 'EVT-9040',
    timestamp: '2026-07-22 07:00:00',
    operator: 'System Daemon',
    category: 'System',
    details: 'Daily diagnostic check complete. All telemetry registers synchronized.',
    ip: 'Localhost',
    hash: '9a8b7c...6d5e'
  }
];

export default function AuditLogs() {
  const [logs, setLogs] = useState(INITIAL_LOGS);
  const [searchQuery, setSearchQuery] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [isRefreshSpinning, setIsRefreshSpinning] = useState(false);

  const handleRefresh = () => {
    setIsRefreshSpinning(true);
    setTimeout(() => {
      setIsRefreshSpinning(false);
    }, 800);
  };

  const filteredLogs = logs.filter(log => {
    const matchesSearch = log.id.toLowerCase().includes(searchQuery.toLowerCase()) || 
                          log.operator.toLowerCase().includes(searchQuery.toLowerCase()) ||
                          log.details.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesCategory = categoryFilter ? log.category === categoryFilter : true;
    return matchesSearch && matchesCategory;
  });

  const getCategoryStyle = (cat) => {
    switch (cat) {
      case 'Advisory': return { color: '#ea580c', bg: '#fff7ed' };
      case 'Broadcast': return { color: '#16a34a', bg: '#f0fdf4' };
      case 'System': return { color: '#dc2626', bg: '#fef2f2' };
      case 'Configuration': return { color: '#0284c7', bg: '#f0f9ff' };
      default: return { color: '#4b5563', bg: '#f3f4f6' };
    }
  };

  return (
    <div className="main-view">
      {/* View Header */}
      <div className="view-header">
        <div className="view-title-container">
          <h1>Audit Logs</h1>
          <span className="view-subtitle">Cryptographically signed chronological trace of operator actions and warning telemetry dispatches</span>
        </div>
        <button className="btn-refresh" onClick={handleRefresh}>
          <RefreshCw size={13} className={isRefreshSpinning ? 'spin-icon' : ''} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Filter and stats row */}
      <div className="stations-card">
        <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap', alignItems: 'center' }}>
          <div className="search-input-wrapper" style={{ flex: 1, minWidth: '250px' }}>
            <input 
              type="text" 
              placeholder="Search audit logs by ID, operator, details..." 
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <select 
            value={categoryFilter}
            onChange={(e) => setCategoryFilter(e.target.value)}
            className="time-range-select"
            style={{ padding: '10px 14px', borderRadius: '10px', width: '160px', fontSize: '13px' }}
          >
            <option value="">All Categories</option>
            <option value="Advisory">Advisory Updates</option>
            <option value="Broadcast">Broadcast Logs</option>
            <option value="System">System Traces</option>
            <option value="Configuration">Configurations</option>
          </select>
        </div>
      </div>

      {/* Main Table Card */}
      <div className="stations-card" style={{ padding: '20px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <History size={18} style={{ color: 'var(--color-brand)' }} />
            <span>Audit Trail</span>
          </h2>
          <span style={{ fontSize: '12px', color: '#16a34a', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: '600' }}>
            <ShieldCheck size={14} />
            Integrity Check: Valid
          </span>
        </div>

        {/* Audit Logs Table */}
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Log ID</th>
                <th>Timestamp</th>
                <th>Operator / Source</th>
                <th>Category</th>
                <th>Action Details</th>
                <th>Origin IP</th>
                <th>SHA-256 Checksum</th>
              </tr>
            </thead>
            <tbody>
              {filteredLogs.length === 0 ? (
                <tr>
                  <td colSpan="7" style={{ textAlign: 'center', padding: '24px', color: 'var(--text-light)' }}>
                    No audit logs match your search.
                  </td>
                </tr>
              ) : (
                filteredLogs.map(log => {
                  const catStyle = getCategoryStyle(log.category);
                  return (
                    <tr key={log.id} style={{ cursor: 'default' }}>
                      <td style={{ fontFamily: 'monospace', fontWeight: '700', color: 'var(--text-main)' }}>{log.id}</td>
                      <td className="text-muted" style={{ whiteSpace: 'nowrap' }}>{log.timestamp}</td>
                      <td style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '14px 16px', fontWeight: '500' }}>
                        {log.operator.includes('System') || log.operator.includes('Telemetry') ? (
                          <Server size={12} style={{ color: 'var(--text-light)' }} />
                        ) : (
                          <User size={12} style={{ color: 'var(--color-brand)' }} />
                        )}
                        <span>{log.operator}</span>
                      </td>
                      <td>
                        <span style={{ 
                          color: catStyle.color, 
                          backgroundColor: catStyle.bg, 
                          padding: '3px 8px', 
                          borderRadius: '12px', 
                          fontSize: '11px', 
                          fontWeight: '700' 
                        }}>
                          {log.category}
                        </span>
                      </td>
                      <td style={{ maxWidth: '300px', wordWrap: 'break-word' }}>{log.details}</td>
                      <td style={{ fontFamily: 'monospace', fontSize: '12px' }}>{log.ip}</td>
                      <td style={{ fontFamily: 'monospace', color: 'var(--text-light)', fontSize: '11px' }}>{log.hash}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination controls */}
        <div className="table-footer">
          <span>Showing {filteredLogs.length} of {logs.length} logs</span>
          <div className="pagination-controls">
            <button className="pagination-btn" disabled><ChevronLeft size={14} /></button>
            <button className="pagination-btn active">1</button>
            <button className="pagination-btn" disabled><ChevronRight size={14} /></button>
          </div>
        </div>
      </div>
    </div>
  );
}
