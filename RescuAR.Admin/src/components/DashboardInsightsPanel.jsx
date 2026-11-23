import React, { useEffect, useState } from 'react';
import { Activity, ArrowUpRight, ClipboardList, Download, MapPin, ShieldCheck, Users, Waves } from 'lucide-react';
import {
  ResponsiveContainer, CartesianGrid, XAxis, YAxis, Tooltip, LineChart, Line, BarChart, Bar,
} from 'recharts';
import { supabase } from '../supabaseClient';
import { canView } from '../rbac/access';
import { exportCsv } from '../utils/exportData';
import { formatDashboardTimestamp } from '../services/dashboardDataService';
import { fetchDashboardInsights } from '../services/dashboardInsightsService';
import './dashboardInsights.css';

const PERIOD_OPTIONS = {
  flood: [{ days: 1, label: '24 hours' }, { days: 7, label: '7 days' }, { days: 30, label: '30 days' }, { days: 180, label: '6 months' }],
  community: [{ days: 7, label: '7 days' }, { days: 30, label: '30 days' }, { days: 90, label: '90 days' }],
  users: [{ days: 30, label: '30 days' }, { days: 90, label: '90 days' }, { days: 180, label: '6 months' }],
};
const fmt = (value) => value === null || value === undefined ? '—' : (Number.isFinite(Number(value)) ? Number(value).toLocaleString('en-PH') : String(value));
const textOrNoData = (items) => Array.isArray(items) && items.length > 0;

function InsightMetric({ label, value, note }) {
  return <div className="insight-metric">
    <span className="insight-metric-label">{label}</span>
    <strong className="insight-metric-value">{fmt(value)}</strong>
    {note && <span className="insight-metric-note">{note}</span>}
  </div>;
}

function InsightPanel({ title, icon: Icon, children, note }) {
  return <section className="insight-panel">
    <h2><Icon size={19} aria-hidden="true" />{title}</h2>
    {note && <p className="insight-muted">{note}</p>}
    {children}
  </section>;
}

function SimpleBars({ data, dataKey = 'value', label = 'Records' }) {
  if (!Array.isArray(data)) return <p className="insight-empty">Data source unavailable.</p>;
  if (!textOrNoData(data)) return <p className="insight-empty">No records in this category.</p>;
  return <div className="insight-chart" role="img" aria-label={`${label} by category`}>
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={data} margin={{ top: 10, right: 15, left: -10, bottom: 12 }}>
        <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="name" interval={0} angle={-25} textAnchor="end" height={76} fontSize={11} />
        <YAxis allowDecimals={false} fontSize={11} />
        <Tooltip />
        <Bar dataKey={dataKey} name={label} fill="#4f46e5" maxBarSize={46} radius={[5, 5, 0, 0]} />
      </BarChart>
    </ResponsiveContainer>
  </div>;
}

function DailyLine({ data, label }) {
  if (!Array.isArray(data)) return <p className="insight-empty">Data source unavailable.</p>;
  if (!data.some((point) => point.count > 0)) return <p className="insight-empty">No recorded events in this period.</p>;
  return <div className="insight-chart" role="img" aria-label={label}>
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data} margin={{ top: 10, right: 18, left: -20, bottom: 0 }}>
        <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="date" tickFormatter={(value) => value.slice(5)} fontSize={11} minTickGap={16} />
        <YAxis allowDecimals={false} fontSize={11} />
        <Tooltip />
        <Line type="monotone" dataKey="count" name={label} stroke="#4f46e5" strokeWidth={2.5} dot={false} connectNulls={false} />
      </LineChart>
    </ResponsiveContainer>
  </div>;
}

function PanelLink({ enabled, onClick, children }) {
  if (!enabled) return null;
  return <button type="button" className="insight-link" onClick={onClick}>
    {children} <ArrowUpRight size={15} aria-hidden="true" />
  </button>;
}

function FloodContent({ data, stationCode, setStationCode, access, onViewChange }) {
  const station = data.stations.find((item) => item.code === data.selectedStationCode);
  const levels = data.history;
  return <>
    <div className="insight-metrics insight-metrics-3">
      <InsightMetric label="Enabled PAGASA stations" value={data.stations.length} note="From the active station catalog" />
      <InsightMetric label="Selected station level" value={station?.level === null || !station ? null : `${station.level.toFixed(2)} m EL`} note="Water-level elevation, not river depth" />
      <InsightMetric label="Observation age" value={station?.freshness?.ageText || '—'} note={station?.freshness?.label || 'No verified timestamp'} />
    </div>
    <div className="insight-two-col">
      <InsightPanel title="PAGASA level history" icon={Waves} note="Recorded observations only. Missing days remain gaps; no estimated readings are inserted.">
        <label className="insight-input-label" htmlFor="insights-gauge">Monitoring station</label>
        <select id="insights-gauge" className="insight-select" value={stationCode || data.selectedStationCode} onChange={(event) => setStationCode(event.target.value)} disabled={!data.stations.length}>
          {data.stations.length ? data.stations.map((entry) => <option key={entry.code} value={entry.code}>{entry.name}</option>) : <option value="">No active station available</option>}
        </select>
        {levels === null ? <p className="insight-empty">Historical chart unavailable. Check the `pagasa_river_chart` function and permissions.</p>
          : !levels.some((point) => point.level !== null) ? <p className="insight-empty">No saved readings for this station and period.</p>
          : <div className="insight-chart insight-chart-tall" role="img" aria-label="Recorded PAGASA water levels">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={levels} margin={{ top: 15, right: 16, left: -10, bottom: 0 }}>
                <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" vertical={false} />
                <XAxis dataKey="label" minTickGap={20} fontSize={11} />
                <YAxis fontSize={11} domain={['auto', 'auto']} unit=" m" />
                <Tooltip labelFormatter={(_, payload) => payload?.[0]?.payload?.time ? formatDashboardTimestamp(payload[0].payload.time) : 'Observation'} />
                <Line type="monotone" dataKey="level" stroke="#0284c7" name="Level (m EL)" strokeWidth={2.5} dot={false} connectNulls={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>}
      </InsightPanel>
      <InsightPanel title="Station freshness" icon={Activity} note="Observation freshness is separate from verified flood risk.">
        <div className="insight-station-list">
          {data.stations.length ? data.stations.map((entry) => <div className="insight-station" key={entry.code}>
            <div><strong>{entry.name}</strong><small>{entry.level === null ? 'Level unavailable' : `${entry.level.toFixed(2)} m EL`} · {entry.freshness.ageText}</small></div>
            <span className={`insight-state insight-state-${entry.freshness.status}`}>{entry.freshness.label}</span>
          </div>) : <p className="insight-empty">Active station catalog unavailable or empty.</p>}
        </div>
        <p className="insight-disclaimer">The chart does not infer Alert, Alarm or Critical conditions. Verified thresholds must be checked in the monitoring module.</p>
        <PanelLink enabled={canView(access, 'monitoring-river-level')} onClick={() => onViewChange('monitoring-river-level')}>Open river monitoring</PanelLink>
        <PanelLink enabled={canView(access, 'monitoring-hazard-exposure')} onClick={() => onViewChange('monitoring-hazard-exposure')}>Open PREP flood exposure analysis</PanelLink>
      </InsightPanel>
    </div>
  </>;
}

function CommunityContent({ data, access, onViewChange }) {
  return <>
    <div className="insight-metrics">
      <InsightMetric label="Reports in selected period" value={data.reportsTotal} note="Includes all moderation statuses" />
      <InsightMetric label="Pending review" value={data.pending} note="Not yet validated by administrators" />
    </div>
    <div className="insight-two-col">
      <InsightPanel title="Reports submitted over time" icon={Activity}><DailyLine data={data.trend} label="Submitted reports" /></InsightPanel>
      <InsightPanel title="Moderation status" icon={ShieldCheck}><SimpleBars data={data.statuses} label="Reports" /></InsightPanel>
    </div>
    <InsightPanel title="Report categories" icon={ClipboardList} note="Categories are based on submitted records, not verified flood hazard severity.">
      <SimpleBars data={data.categories} label="Reports" />
      <PanelLink enabled={canView(access, 'community-reports-moderation')} onClick={() => onViewChange('community-reports-moderation')}>Review community reports</PanelLink>
    </InsightPanel>
  </>;
}

function UsersContent({ data, access, onViewChange }) {
  const value = data.summary;
  if (!value) return <p className="insight-empty">User statistics are unavailable. Check access to the authorized user management RPC.</p>;
  return <>
    <div className="insight-metrics">
      <InsightMetric label="Registered resident accounts" value={value.total} note="Excludes administrator-role accounts" />
      <InsightMetric label="Active account status" value={value.activeAccounts} note="Status only; not a live session count" />
      <InsightMetric label="Suspended accounts" value={value.suspendedAccounts} />
      <InsightMetric label="New registrations" value={value.newRegistrations} note="Within selected period" />
    </div>
    <div className="insight-two-col">
      <InsightPanel title="Registration trend" icon={Users}><DailyLine data={value.registrations} label="Registrations" /></InsightPanel>
      <InsightPanel title="Account status distribution" icon={ShieldCheck}><SimpleBars data={value.statuses} label="Accounts" /></InsightPanel>
    </div>
    <p className="insight-disclaimer">Activity and barangay distributions are not shown: this version has no verified login-activity analytics or structured barangay field. Account status must not be interpreted as engagement.</p>
    <PanelLink enabled={canView(access, 'community-user-management')} onClick={() => onViewChange('community-user-management')}>Open User Management</PanelLink>
  </>;
}

function ResourcesContent({ data, access, onViewChange }) {
  const value = data.summary;
  return <>
    <div className="insight-metrics">
      <InsightMetric label="Recorded centers" value={value.totalCenters} />
      <InsightMetric label="Centers with capacity data" value={value.totalCenters === null ? null : value.measuredCenters} note="Capacity and occupancy both recorded" />
      <InsightMetric label="Recorded evacuees" value={value.occupancy} note="Only centers with complete occupancy data" />
      <InsightMetric label="Available places" value={value.available} note="Based on capacity records, not safety clearance" />
      <InsightMetric label="Active advisory records" value={value.activeAdvisories} />
    </div>
    <div className="insight-two-col">
      <InsightPanel title="Evacuation capacity" icon={MapPin} note="Shows the largest six centers with complete capacity and occupancy records.">
        {!Array.isArray(value.centers) ? <p className="insight-empty">Evacuation center data unavailable or access restricted.</p>
          : !value.centers.length ? <p className="insight-empty">No centers have both capacity and occupancy recorded.</p>
          : <div className="insight-chart insight-chart-tall" role="img" aria-label="Evacuation center occupancy">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={value.centers} margin={{ top: 15, right: 10, left: -10, bottom: 15 }}>
                <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" vertical={false} />
                <XAxis dataKey="name" fontSize={10} angle={-25} textAnchor="end" interval={0} height={75} tickFormatter={(name) => name.length > 13 ? `${name.slice(0, 12)}…` : name} />
                <YAxis fontSize={11} allowDecimals={false} />
                <Tooltip /><Bar dataKey="used" name="Current evacuees" stackId="space" fill="#4f46e5" />
                <Bar dataKey="available" name="Available places" stackId="space" fill="#94a3b8" />
              </BarChart>
            </ResponsiveContainer>
          </div>}
        <PanelLink enabled={canView(access, 'content-evacuation')} onClick={() => onViewChange('content-evacuation')}>Manage evacuation centers</PanelLink>
      </InsightPanel>
      <InsightPanel title="Advisory status" icon={ClipboardList} note="Records by publishing status; no SMS delivery claims.">
        <SimpleBars data={value.advisories} label="Advisories" />
        <PanelLink enabled={canView(access, 'content-advisories')} onClick={() => onViewChange('content-advisories')}>Open advisories</PanelLink>
      </InsightPanel>
    </div>
  </>;
}

// Protect CSV spreadsheet viewers from treating user-controlled categories as formulas.
const csvSafe = (value) => {
  const str = String(value ?? '');
  return /^[\s]*[=+@-]/.test(str) ? `'${str}` : str;
};
function exportRows(tab, data) {
  const rows = [];
  const add = (label, value, time = '', details = '') => rows.push({
    section: tab, label: csvSafe(label), value: typeof value === 'string' ? csvSafe(value) : value,
    time: csvSafe(time), details: csvSafe(details),
  });
  if (tab === 'flood') {
    for (const station of data.stations) add(station.name, station.level ?? '', station.observedAt, station.freshness.label);
    for (const item of data.history || []) if (item.level !== null) add('Historical observation (m EL)', item.level, item.time, data.selectedStationCode);
  }
  if (tab === 'community') {
    add('Reports in period', data.reportsTotal); add('Pending review', data.pending);
    for (const point of data.trend || []) add('Reports submitted', point.count, point.date);
    for (const point of data.statuses || []) add(`Status: ${point.name}`, point.value);
    for (const point of data.categories || []) add(`Category: ${point.name}`, point.value);
  }
  if (tab === 'users' && data.summary) {
    const { total, activeAccounts, suspendedAccounts, newRegistrations, registrations, statuses } = data.summary;
    add('Resident accounts', total); add('Active account status', activeAccounts); add('Suspended', suspendedAccounts); add('New registrations', newRegistrations);
    for (const point of registrations) add('Registrations', point.count, point.date);
    for (const point of statuses) add(`Status: ${point.name}`, point.value);
  }
  if (tab === 'resources') {
    const { totalCenters, measuredCenters, occupancy, available, activeAdvisories, centers, advisories } = data.summary;
    add('Centers', totalCenters); add('Centers with recorded capacity', measuredCenters);
    add('Recorded evacuees', occupancy); add('Available places', available); add('Active advisories', activeAdvisories);
    for (const item of centers || []) add(item.name, item.used, '', `Recorded capacity: ${item.capacity}; available places: ${item.available}`);
    for (const point of advisories || []) add(`Advisory status: ${point.name}`, point.value);
  }
  return rows;
}

export default function DashboardInsightsPanel({ tab, access, onViewChange, refreshVersion }) {
  const [days, setDays] = useState(tab === 'flood' ? 7 : 30);
  const [stationCode, setStationCode] = useState('');
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const allowCenters = canView(access, 'content-evacuation');
  const allowAdvisories = canView(access, 'content-advisories');

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    setError('');
    fetchDashboardInsights(supabase, tab, { days, stationCode, allowCenters, allowAdvisories })
      .then((result) => { if (mounted) setData(result); })
      .catch((reason) => { if (mounted) { setData(null); setError(reason?.message || 'Analytics request failed'); } })
      .finally(() => { if (mounted) setLoading(false); });
    return () => { mounted = false; };
  }, [tab, days, stationCode, refreshVersion, allowCenters, allowAdvisories]);

  return <div className="insight-content" id="dashboard-tab-panel" role="tabpanel" aria-label={`${tab} analytics`}>
    <div className="insight-toolbar">
      <div>
        <h2>{({ flood: 'Flood Monitoring', community: 'Community Reports', users: 'User Analytics', resources: 'Resources & Advisories' })[tab]}</h2>
        <p>Live records only · Last loaded: {data ? formatDashboardTimestamp(data.fetchedAt) : 'Not available'}</p>
      </div>
      <div className="insight-filters">
        {PERIOD_OPTIONS[tab] && <label htmlFor="dashboard-period">Period
          <select id="dashboard-period" className="insight-select" value={days} onChange={(event) => setDays(Number(event.target.value))}>
            {PERIOD_OPTIONS[tab].map((opt) => <option key={opt.days} value={opt.days}>{opt.label}</option>)}
          </select>
        </label>}
        <button className="btn-outline insight-export" type="button" disabled={loading || !data} onClick={() => exportCsv(`rescuar-${tab}-analytics`, exportRows(tab, data), [
          { label: 'Section', key: 'section' }, { label: 'Metric', key: 'label' }, { label: 'Value', key: 'value' }, { label: 'Time', key: 'time' }, { label: 'Details', key: 'details' },
        ])}><Download size={16} /> Export this tab</button>
      </div>
    </div>
    {loading && <p className="insight-loading" role="status">Loading {tab} records from Supabase…</p>}
    {error && <p className="insight-warning" role="alert">Unable to retrieve analytics: {error}</p>}
    {data?.warnings?.length > 0 && <div className="insight-warning" role="status"><strong>Data limitations:</strong>
      <ul>{data.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>
    </div>}
    {!loading && data && <div className="insight-panels">
      {tab === 'flood' && <FloodContent data={data} stationCode={stationCode} setStationCode={setStationCode} access={access} onViewChange={onViewChange} />}
      {tab === 'community' && <CommunityContent data={data} access={access} onViewChange={onViewChange} />}
      {tab === 'users' && <UsersContent data={data} access={access} onViewChange={onViewChange} />}
      {tab === 'resources' && <ResourcesContent data={data} access={access} onViewChange={onViewChange} />}
    </div>}
  </div>;
}
