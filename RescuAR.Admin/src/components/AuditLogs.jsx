import React, { useEffect, useRef, useState } from 'react';
import { Check, ChevronLeft, ChevronRight, History, MousePointerClick, RefreshCw, X } from 'lucide-react';
import { webAuditService } from '../services/webAuditClient';
import { createAuditLogController } from '../services/auditLogController';
import { AUDIT_ACTIONS, AUDIT_MODULES, AUDIT_OUTCOMES, auditDetailLines, auditSourceLabel, readableName } from './auditPresentation';
import './AuditLogs.css';

const noOp = () => {};
const emptyFilters = { module: '', actorId: '', eventType: '' };
const shortId = (id) => `${id.slice(0, 8)}…${id.slice(-4)}`;
const displayTime = (value) => new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' });

export default function AuditLogs({ service = webAuditService, onAccessDenied = noOp }) {
  const [state, setState] = useState({ pages: [], pageIndex: 0, loading: true, error: '', selected: null });
  const [draftFilters, setDraftFilters] = useState(emptyFilters);
  const controllerRef = useRef(null);
  const initialOperationRef = useRef(null);

  useEffect(() => {
    // Reuse the ID when development StrictMode repeats effect setup.
    if (!initialOperationRef.current) initialOperationRef.current = service.newOperationId();
    const controller = createAuditLogController(service, setState, onAccessDenied);
    controllerRef.current = controller;
    void controller.loadInitial(initialOperationRef.current);
    return () => controller.dispose();
  }, [service, onAccessDenied]);

  const page = state.pages[state.pageIndex];
  const records = page?.records || [];
  const selected = state.selected;
  const detailLines = selected ? auditDetailLines(selected.details) : [];
  const selectRecord = (row) => {
    if (!state.loading) void controllerRef.current.inspect(row);
  };
  const applyFilters = (event) => {
    event.preventDefault();
    if (!state.loading) void controllerRef.current.applyFilters(draftFilters);
  };
  const clearFilters = () => {
    setDraftFilters(emptyFilters);
    void controllerRef.current.applyFilters(emptyFilters);
  };

  return (
    <div className="main-view audit-view">
      <div className="view-header audit-header">
        <div className="view-title-container">
          <h1>Audit Logs</h1>
          <p className="view-subtitle">Recorded administrative actions, newest first.</p>
        </div>
        <button className="btn-refresh" disabled={state.loading} onClick={() => void controllerRef.current.refresh()}>
          <RefreshCw size={16} /> Refresh
        </button>
      </div>

      <form className="stations-card audit-filters" aria-label="Filter audit history" onSubmit={applyFilters}>
        <label>Module
          <select value={draftFilters.module} onChange={(event) => setDraftFilters({ ...draftFilters, module: event.target.value })} disabled={state.loading}>
            <option value="">All modules</option>
            {Object.entries(AUDIT_MODULES).filter(([key]) => key !== 'content-news').map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select>
        </label>
        <label>Action
          <select value={draftFilters.eventType} onChange={(event) => setDraftFilters({ ...draftFilters, eventType: event.target.value })} disabled={state.loading}>
            <option value="">All actions</option>
            {Object.entries(AUDIT_ACTIONS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select>
        </label>
        <label>Administrator ID
          <input value={draftFilters.actorId} onChange={(event) => setDraftFilters({ ...draftFilters, actorId: event.target.value })} maxLength={36} autoComplete="off" placeholder="Any administrator" disabled={state.loading} />
        </label>
        <div className="audit-filter-actions">
          <button className="btn-refresh" type="submit" disabled={state.loading}>Apply filters</button>
          <button className="btn-refresh" type="button" disabled={state.loading} onClick={clearFilters}>Clear filters</button>
        </div>
      </form>

      {state.error && <div className="audit-error" role="alert">{state.error}{records.length > 0 && <p>The previously loaded records are still displayed.</p>}</div>}

      <div className="audit-panels">
        <section className="stations-card audit-results" aria-busy={state.loading} aria-label="Recorded actions">
          <div className="audit-panel-header">
            <h2 className="audit-heading"><History size={18} /> Recorded actions</h2>
            <p className="audit-note" id="audit-row-instructions">Select an entry to view its details. Times follow your device’s timezone.</p>
          </div>
          <div className="table-container audit-table-scroll" tabIndex={0} role="region" aria-label="Audit entries" aria-describedby="audit-row-instructions">
            <table className="data-table audit-table">
              <thead><tr><th>Recorded at</th><th>Administrator</th><th>Module / action</th><th>Outcome</th><th>Evidence</th></tr></thead>
              <tbody>
                {records.map((row) => (
                  <tr
                    key={row.id}
                    className={selected?.id === row.id ? 'audit-row is-selected' : 'audit-row'}
                    tabIndex={state.loading ? -1 : 0}
                    aria-current={selected?.id === row.id ? 'true' : undefined}
                    aria-disabled={state.loading}
                    aria-controls="audit-record-details"
                    onClick={() => selectRecord(row)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        selectRecord(row);
                      }
                    }}
                  >
                    <td><div className="audit-record-time"><span className="audit-selection-mark" aria-hidden="true">{selected?.id === row.id && <Check size={14} />}</span>{selected?.id === row.id && <span className="audit-sr-only">Selected entry. </span>}<time dateTime={row.recorded_at} title={row.recorded_at}><span>{new Date(row.recorded_at).toLocaleDateString(undefined, { dateStyle: 'medium' })}</span><span className="audit-time-of-day">{new Date(row.recorded_at).toLocaleTimeString(undefined, { timeStyle: 'medium' })}</span></time></div></td>
                    <td className="audit-id" title={row.actor_id}>{shortId(row.actor_id)}</td>
                    <td><strong>{AUDIT_ACTIONS[row.event_type] || readableName(row.event_type)}</strong><span className="audit-module">{AUDIT_MODULES[row.module] || readableName(row.module)}</span></td>
                    <td><span className={`audit-outcome ${Object.hasOwn(AUDIT_OUTCOMES, row.outcome) ? row.outcome : ''}`}>{AUDIT_OUTCOMES[row.outcome] || readableName(row.outcome)}</span></td>
                    <td>{auditSourceLabel(row.source)}</td>
                  </tr>
                ))}
                {!records.length && <tr><td className="audit-empty" colSpan={5} role="status">{state.loading ? 'Loading audit history…' : state.error ? 'Audit history is unavailable.' : 'No audit records match these filters.'}</td></tr>}
              </tbody>
            </table>
          </div>
          <div className="audit-pagination">
            <span>{records.length} records · Page {state.pageIndex + 1}{state.loading ? ' · Loading…' : ''}</span>
            <div className="audit-page-actions">
              <button className="btn-refresh" disabled={state.loading || state.pageIndex === 0} onClick={() => void controllerRef.current.newer()}><ChevronLeft size={16} /> Newer</button>
              <button className="btn-refresh" disabled={state.loading || !page?.hasMore} onClick={() => void controllerRef.current.older()}>Older <ChevronRight size={16} /></button>
            </div>
          </div>
        </section>

        <aside className="stations-card audit-record-details" id="audit-record-details" aria-label="Record details">
          <div className="audit-panel-header audit-details-header">
            <h2 className="audit-heading">Record details</h2>
            {selected && <button className="audit-clear-selection" aria-label="Clear selection" title="Clear selection" onClick={() => controllerRef.current.closeDetails()}><X size={16} /></button>}
          </div>
          <div className="audit-details-scroll" tabIndex={selected ? 0 : undefined} role="region" aria-label="Record details content" aria-live="polite">
            {selected ? <>
              <span className="audit-selected-label"><Check size={14} aria-hidden="true" /> Selected entry</span>
              <dl>
                <dt>Action</dt><dd>{AUDIT_ACTIONS[selected.event_type] || readableName(selected.event_type)}</dd>
                <dt>Module</dt><dd>{AUDIT_MODULES[selected.module] || readableName(selected.module)}</dd>
                <dt>Recorded at</dt><dd><time dateTime={selected.recorded_at} title={selected.recorded_at}>{displayTime(selected.recorded_at)}</time></dd>
                <dt>Outcome</dt><dd>{AUDIT_OUTCOMES[selected.outcome] || readableName(selected.outcome)}</dd>
                <dt>Administrator ID</dt><dd className="audit-id">{selected.actor_id}</dd>
                <dt>Record ID</dt><dd className="audit-id">{selected.id}</dd>
                <dt>Operation ID</dt><dd className="audit-id">{selected.operation_id}</dd>
                <dt>Target</dt><dd>{selected.target_type || '—'}{selected.target_id && <span className="audit-module audit-id">{selected.target_id}</span>}</dd>
                <dt>Evidence</dt><dd>{auditSourceLabel(selected.source)}<p className="audit-note audit-evidence-note">{selected.source === 'database' ? 'Confirmed by the database.' : 'Records a browser interaction.'}</p></dd>
              </dl>
              {detailLines.length > 0 && <div className="audit-metadata"><h3>Activity details</h3><ul>{detailLines.map((line) => <li key={line}>{line}</li>)}</ul></div>}
            </> : <div className="audit-details-placeholder"><MousePointerClick size={28} aria-hidden="true" /><p>Click any entry and its details will be shown here.</p></div>}
          </div>
        </aside>
      </div>
    </div>
  );
}
