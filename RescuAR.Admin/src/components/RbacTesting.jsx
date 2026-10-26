import React, { useEffect, useState } from 'react';
import {
  Activity,
  AlertCircle,
  BellRing,
  CheckCircle2,
  Clock3,
  FlaskConical,
  History,
  RefreshCw,
  Save,
  ShieldCheck,
  ShieldAlert,
  Waves,
} from 'lucide-react';
import { supabase } from '../supabaseClient';
import './RbacPages.css';

// Preview-only legacy PREP threshold mapping. NOT verified real-world flood extent.
const affectedAreas = (level) => level >= 18
  ? ['Tumana', 'Malanday', 'Nangka', 'Concepcion', 'Barangka', 'Tañong', 'Calumpang', 'Santo Niño', 'San Roque']
  : level >= 17 ? ['Tumana', 'Malanday', 'Nangka', 'Concepcion', 'Barangka', 'Tañong']
    : level >= 16 ? ['Tumana', 'Malanday', 'Nangka', 'Concepcion']
      : level >= 15 ? ['Tumana', 'Malanday'] : [];

const previewLevel = (level) => level >= 18 ? 'Level 3' : level >= 16 ? 'Level 2' : level >= 15 ? 'Level 1' : 'Below Level 1';

const formatDate = (value) => {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Unknown date' : parsed.toLocaleString('en-PH', {
    month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
};

export default function RbacTesting() {
  const [level, setLevel] = useState('16');
  const [subject, setSubject] = useState('TEST: Flood advisory');
  const [message, setMessage] = useState('Simulation only. No action required.');
  const [logs, setLogs] = useState([]);
  const [notice, setNotice] = useState(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const numeric = Number(level);
  const validLevel = level.trim() !== '' && Number.isFinite(numeric) && numeric >= 0 && numeric <= 30;
  const affected = validLevel ? affectedAreas(numeric) : [];
  const alarm = validLevel ? previewLevel(numeric) : 'Invalid input';
  const severityClass = !validLevel ? '' : numeric >= 18 ? 'rbac-level--critical' : numeric >= 16 ? 'rbac-level--warning' : numeric >= 15 ? 'rbac-level--alert' : 'rbac-level--normal';

  const refresh = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase.rpc('rbac_list_tests');
      if (error) throw error;
      setLogs(Array.isArray(data) ? data : []);
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to retrieve test history.' });
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => { void refresh(); }, []);

  const save = async (kind, payload) => {
    if (busy) return;
    setBusy(true);
    setNotice(null);
    try {
      const { error } = await supabase.rpc('rbac_log_test', { p_kind: kind, p_payload: payload });
      if (error) throw error;
      await refresh();
      setNotice({ type: 'success', text: 'Test recorded. No live telemetry was modified and no public notification was sent.' });
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to save the test record.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="main-view rbac-page" aria-label="Testing and Simulation">
      <div className="view-header rbac-page-heading">
        <div className="view-title-container">
          <div className="rbac-eyebrow"><FlaskConical size={15} /> Monitoring / Flood Hazard Exposure</div>
          <h1>Testing &amp; Simulation</h1>
          <p className="view-subtitle">Preview river-level thresholds and advisory content without touching production information.</p>
        </div>
        <span className="rbac-top-badge"><ShieldCheck size={15} /> Superadmin access</span>
      </div>

      <div className="rbac-test-banner" role="note"><ShieldAlert size={20} /><div><strong>TEST ENVIRONMENT — NOT LIVE MONITORING</strong><span>All values on this page are simulations. These previews do not represent confirmed flooding or generate public emergency notifications.</span></div></div>

      {notice && <div className={`rbac-notice rbac-notice--${notice.type}`} role={notice.type === 'error' ? 'alert' : 'status'}>{notice.type === 'error' ? <AlertCircle size={18} /> : <CheckCircle2 size={18} />}<span>{notice.text}</span><button type="button" aria-label="Dismiss message" onClick={() => setNotice(null)}>×</button></div>}

      <div className="rbac-test-grid">
        <section className="rbac-panel" aria-labelledby="rbac-river-test-title">
          <div className="rbac-panel-heading"><div className="rbac-section-icon"><Waves size={19} /></div><div><h2 id="rbac-river-test-title">River-Level Testing</h2><p>Manually preview PREP's legacy threshold rules.</p></div></div>
          <div className="rbac-simulation-control">
            <div className="rbac-simulation-label"><label htmlFor="rbac-sim-level">Simulated river level</label><strong>{validLevel ? `${numeric.toFixed(1)} m` : '—'}</strong></div>
            <input id="rbac-sim-level" className="rbac-range" type="range" min="0" max="30" step="0.1" value={validLevel ? numeric : 0} onChange={(event) => setLevel(event.target.value)} />
            <div className="rbac-simulation-scale"><span>0 m</span><span>15 m</span><span>30 m</span></div>
            <div className="form-group rbac-meter-field"><label className="form-label" htmlFor="rbac-meter-input">Manual reading (m)</label><input id="rbac-meter-input" className="form-input" type="number" min="0" max="30" step="0.1" value={level} onChange={(event) => setLevel(event.target.value)} /></div>
          </div>
          <div className="rbac-test-result" aria-live="polite"><span className="rbac-result-label"><Activity size={15} /> Threshold preview</span><strong className={`rbac-level-pill ${severityClass}`}>{alarm}</strong><p>Only predefined PREP threshold rules are evaluated. These are not verified flood observations.</p></div>
          <div className="rbac-affected-block"><h3>Potentially affected barangays <span>Unvalidated legacy rules</span></h3>{affected.length > 0 ? <div className="rbac-area-list">{affected.map((name) => <span key={name}>{name}</span>)}</div> : <p className="rbac-muted">{validLevel ? 'No threshold activated. This does not prove flooding is impossible.' : 'Enter a value between 0 and 30 meters.'}</p>}</div>
          <button type="button" className="btn-primary rbac-action-btn rbac-full-button" disabled={busy || !validLevel} onClick={() => void save('river_level', { meters: numeric, alarm, affected, mode: 'simulation', warning: 'legacy-unvalidated' })}><Save size={16} /> {busy ? 'Saving…' : 'Save River-Level Test'}</button>
        </section>

        <section className="rbac-panel" aria-labelledby="rbac-advisory-title">
          <div className="rbac-panel-heading"><div className="rbac-section-icon"><BellRing size={19} /></div><div><h2 id="rbac-advisory-title">Advisory Testing</h2><p>Prepare a test-only advisory and preview its content.</p></div></div>
          <div className="rbac-advisory-fields"><div className="form-group"><label className="form-label" htmlFor="rbac-test-subject">Advisory title</label><input id="rbac-test-subject" className="form-input" value={subject} maxLength={250} onChange={(event) => setSubject(event.target.value)} /></div><div className="form-group"><label className="form-label" htmlFor="rbac-test-message">Advisory message</label><textarea id="rbac-test-message" className="form-textarea" value={message} maxLength={2000} onChange={(event) => setMessage(event.target.value)} /></div></div>
          <div className="rbac-advisory-preview"><div className="rbac-preview-tag"><FlaskConical size={14} /> TEST PREVIEW</div><h3>{subject.trim() || 'Untitled test advisory'}</h3><p>{message.trim() || 'Enter a message to preview it here.'}</p></div>
          <div className="rbac-panel-help"><ShieldCheck size={15} /><span>Saving only records this preview in testing history. SMS, push alerts, and public advisories are not sent.</span></div>
          <button type="button" className="btn-primary rbac-action-btn rbac-full-button" disabled={busy || !subject.trim() || !message.trim()} onClick={() => void save('advisory_preview', { subject: subject.slice(0, 250), message: message.slice(0, 2000), mode: 'test_only' })}><Save size={16} /> {busy ? 'Saving…' : 'Save Advisory Preview'}</button>
        </section>
      </div>

      <section className="rbac-panel rbac-panel--flush" aria-labelledby="rbac-history-title"><div className="rbac-panel-toolbar"><div className="rbac-panel-heading"><div className="rbac-section-icon"><History size={19} /></div><div><h2 id="rbac-history-title">Recent Test History</h2><p>Saved simulation events and advisory previews.</p></div></div><button type="button" className="btn-secondary rbac-action-btn" onClick={() => void refresh()} disabled={busy || loading}><RefreshCw size={15} className={loading ? 'rbac-spin' : ''} /> Refresh</button></div>
        <div className="rbac-table-scroll"><table className="rbac-table"><thead><tr><th scope="col">Timestamp</th><th scope="col">Test type</th><th scope="col">Details</th></tr></thead><tbody>{loading ? <tr><td colSpan={3} className="rbac-empty-state">Loading test history…</td></tr> : logs.length === 0 ? <tr><td colSpan={3} className="rbac-empty-state"><Clock3 size={16} /> No saved simulations yet.</td></tr> : logs.slice(0, 25).map((item) => <tr key={item.id}><td className="rbac-muted-cell">{formatDate(item.created_at)}</td><td><span className="rbac-role-tag">{item.kind === 'river_level' ? 'River-level test' : item.kind === 'advisory_preview' ? 'Advisory preview' : item.kind}</span></td><td className="rbac-history-detail">{item.kind === 'river_level' ? `${item.payload?.meters ?? '—'} m · ${item.payload?.alarm ?? 'Unknown level'}` : item.kind === 'advisory_preview' ? (item.payload?.subject || 'Untitled advisory') : JSON.stringify(item.payload || {}).slice(0, 160)}</td></tr>)}</tbody></table></div>
      </section>
      <p className="rbac-footer-note"><ShieldCheck size={16} /> Test records are separate from verified river telemetry and production advisories.</p>
    </section>
  );
}
