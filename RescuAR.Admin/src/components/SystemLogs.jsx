import React, { useCallback, useEffect, useState } from 'react';
import { History, RefreshCw, ShieldCheck } from 'lucide-react';
import { supabase } from '../supabaseClient';
import { createSystemAuditService, SYSTEM_AUDIT_PAGE_SIZE } from '../services/systemAuditService.js';
import ExportActions from './ExportActions';

const auditService = createSystemAuditService(supabase);
const moduleOptions = [
  ['', 'All covered modules'],
  ['content-advisories', 'Advisories'],
  ['rbac-admins', 'Admin roles']
];

export default function SystemLogs() {
  const [module, setModule] = useState('');
  const [eventType, setEventType] = useState('');
  const [actorId, setActorId] = useState('');
  const [filters, setFilters] = useState({ module: '', eventType: '', actorId: '' });
  const [pages, setPages] = useState([null]);
  const [pageIndex, setPageIndex] = useState(0);
  const [rows, setRows] = useState([]);
  const [hasMore, setHasMore] = useState(false);
  const [nextId, setNextId] = useState(null);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState('');
  const [refreshToken, setRefreshToken] = useState(0);

  const refresh = useCallback(() => { setPages([null]); setPageIndex(0); setRefreshToken((x) => x + 1); }, []);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setErrorText('');
    auditService.list({ ...filters, beforeId: pages[pageIndex] })
      .then(({ rows: nextRows, hasMore: more, nextId: cursor }) => {
        if (!active) return;
        setRows(nextRows);
        setHasMore(more);
        setNextId(cursor);
      })
      .catch((error) => {
        if (!active) return;
        setRows([]);
        setHasMore(false);
        setErrorText(error.message || 'Audit history unavailable.');
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [filters, pages, pageIndex, refreshToken]);

  function applyFilters(e) {
    e.preventDefault();
    setPages([null]);
    setPageIndex(0);
    setFilters({ module, eventType, actorId });
    setRefreshToken((n) => n + 1);
  }

  const exportRows = rows.map((row) => ({
    id: row.id, time: row.recorded_at, actor: row.actor_id || 'Unknown',
    module: row.module, event: row.event_type, outcome: row.outcome,
    target: row.target_id || '—', source: row.source,
  }));

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>System Audit Logs</h1>
          <span className="view-subtitle">Recorded administrator changes — not a live telemetry diagnostic or SMS delivery log</span>
        </div>
        <button className="btn-refresh" type="button" onClick={refresh} disabled={loading}>
          <RefreshCw size={14} /> Refresh
        </button>
      </div>
      <div className="stations-card" style={{ marginBottom: '16px' }}>
        <p role="status" style={{ fontSize: '13px', marginBottom: '12px' }}>
          <ShieldCheck size={15} style={{ marginRight: '6px', verticalAlign: 'middle' }} />
          Initial audit coverage: committed Advisory changes and RBAC role actions from the database.
          Prior historical actions are not backfilled; unsuccessful attempts and notification delivery are not yet captured.
        </p>
        <form onSubmit={applyFilters} style={{ display: 'flex', alignItems: 'end', gap: '10px', flexWrap: 'wrap' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '12px' }}>
            Module
            <select className="form-input" value={module} onChange={(e) => setModule(e.target.value)}>
              {moduleOptions.map(([value, name]) => <option value={value} key={value}>{name}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '12px' }}>
            Action type
            <input className="form-input" value={eventType} onChange={(e) => setEventType(e.target.value)} placeholder="e.g. advisory.created" />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '12px', flex: 1, minWidth: '190px' }}>
            Administrator UUID (optional)
            <input className="form-input" value={actorId} onChange={(e) => setActorId(e.target.value)} placeholder="Authenticated account ID" />
          </label>
          <button type="submit" className="btn-primary" disabled={loading}>Apply Filters</button>
          <ExportActions title="rescuar_audit_current_page" rows={exportRows} columns={[
            { label: 'ID', key: 'id' }, { label: 'Timestamp', key: 'time' },
            { label: 'Actor UUID', key: 'actor' }, { label: 'Module', key: 'module' },
            { label: 'Action', key: 'event' }, { label: 'Outcome', key: 'outcome' },
            { label: 'Target ID', key: 'target' }, { label: 'Source', key: 'source' }
          ]} disabled={loading || !rows.length} />
        </form>
      </div>
      <div className="stations-card">
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <History size={18} /> Database Audit Events
        </h2>
        {errorText && <p role="alert" style={{ color: '#b91c1c', padding: '12px' }}>{errorText}</p>}
        <div className="table-container" style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr><th>Time (PH)</th><th>Actor UUID</th><th>Module</th><th>Action</th><th>Outcome</th><th>Target</th><th>Source</th></tr>
            </thead>
            <tbody>
              {loading ? <tr><td colSpan="7">Loading actual audit records...</td></tr>
                : !rows.length ? <tr><td colSpan="7">{errorText ? 'Records could not be loaded.' : 'No audit events recorded for these filters.'}</td></tr>
                  : rows.map((row) => <tr key={row.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>{new Date(row.recorded_at).toLocaleString('en-PH', { timeZone: 'Asia/Manila' })}</td>
                    <td style={{ fontSize: '12px' }}>{row.actor_id || 'Not attributable'}</td>
                    <td>{row.module}</td><td>{row.event_type}</td><td>{row.outcome}</td>
                    <td>{row.target_id || '—'}</td><td>{row.source}</td>
                  </tr>)}
            </tbody>
          </table>
        </div>
        <div className="table-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
          <span>Page {pageIndex + 1} · Up to {SYSTEM_AUDIT_PAGE_SIZE} records per page · Export includes only this page</span>
          <div className="pagination-controls">
            <button className="pagination-btn" type="button" disabled={loading || pageIndex === 0} onClick={() => setPageIndex((n) => n - 1)}>Previous</button>
            <button className="pagination-btn" type="button" disabled={loading || !hasMore} onClick={() => {
              setPages((previous) => [...previous.slice(0, pageIndex + 1), nextId]);
              setPageIndex((n) => n + 1);
            }}>Next</button>
          </div>
        </div>
      </div>
    </div>
  );
}
