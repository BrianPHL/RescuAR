import React, { useState, useEffect, useRef } from 'react';
import {
  AlertTriangle,
  ClipboardList,
  Video,
  Users,
  Shield,
  ArrowUpRight,
  RefreshCw,
  ChevronRight,
  Activity,
  Bell,
  Map as MapIcon,
  Download
} from 'lucide-react';
import { MapContainer, TileLayer, CircleMarker, Popup, LayersControl } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import { supabase } from '../supabaseClient';
import { canView } from '../rbac/access';
import { exportCsv } from '../utils/exportData';
import { fetchDashboardData, formatDashboardTimestamp } from '../services/dashboardDataService';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip as RechartsTooltip, ResponsiveContainer,
  BarChart, Bar
} from 'recharts';

const injectedStyles = `
  /* Global Layout - Fix for white space on zoom */
  .dashboard-container {
    --primary: #4f46e5;
    --primary-hover: #4338ca;
    --bg-main: #f8fafc;
    --surface: #ffffff;
    --text-main: #0f172a;
    --text-muted: #64748b;
    --border: #e2e8f0;
    --shadow-sm: 0 1px 2px 0 rgb(0 0 0 / 0.05);
    --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1);
    --shadow-lg: 0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1);
  }

  .dashboard-container {
    padding: 2rem;
    background-color: var(--bg-main);
    min-height: 100vh; /* Fills viewport vertically */
    width: 100%;       /* Ensures width spans fully */
    font-family: 'Inter', system-ui, -apple-system, sans-serif;
    color: var(--text-main);
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    overflow-x: hidden;
  }
  
  /* Do not reset every element in the app: the advisory dialog is a sibling. */
  .dashboard-container,
  .dashboard-container * { box-sizing: border-box; }

  /* Header Section */
  .header-section {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 2.5rem;
    padding-bottom: 1.5rem;
    border-bottom: 1px solid var(--border);
  }
  .header-left {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }
  .title-row {
    display: flex;
    align-items: center;
    gap: 1.25rem;
  }
  .title-row h1 {
    font-size: 2rem;
    font-weight: 800;
    color: var(--text-main);
    letter-spacing: -0.03em;
    background: linear-gradient(90deg, #0f172a, #334155);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
  }
  .status-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.35rem 1rem;
    background-color: #ecfdf5;
    color: #059669;
    font-size: 0.85rem;
    font-weight: 700;
    border-radius: 9999px;
    border: 1px solid #a7f3d0;
    box-shadow: var(--shadow-sm);
  }
  .status-dot {
    width: 0.5rem;
    height: 0.5rem;
    background-color: #10b981;
    border-radius: 50%;
    animation: pulse 2s infinite;
  }
  @keyframes pulse {
    0% { transform: scale(0.95); box-shadow: 0 0 0 0 rgba(16, 185, 129, 0.7); }
    70% { transform: scale(1); box-shadow: 0 0 0 6px rgba(16, 185, 129, 0); }
    100% { transform: scale(0.95); box-shadow: 0 0 0 0 rgba(16, 185, 129, 0); }
  }
  .last-updated {
    font-size: 0.9rem;
    color: var(--text-muted);
    font-weight: 500;
  }
  .header-actions {
    display: flex;
    gap: 1rem;
  }
  .dashboard-container .btn-outline {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.75rem 1.5rem;
    background-color: var(--surface);
    border: 1px solid var(--border);
    border-radius: 0.75rem;
    cursor: pointer;
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-main);
    box-shadow: var(--shadow-sm);
    transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  }
  .dashboard-container .btn-outline:hover {
    background-color: #f1f5f9; 
    border-color: #cbd5e1; 
    transform: translateY(-1px);
    box-shadow: var(--shadow-md);
  }
  .dashboard-container .btn-primary {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.75rem 1.5rem;
    background-color: var(--primary);
    border: none;
    border-radius: 0.75rem;
    cursor: pointer;
    font-size: 0.9rem;
    font-weight: 600;
    color: white;
    box-shadow: var(--shadow-md);
    transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  }
  .dashboard-container .btn-primary:hover {
    background-color: var(--primary-hover);
    transform: translateY(-1px);
    box-shadow: var(--shadow-lg);
  }

  /* Stats Grid */
  .stats-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 1.5rem;
    width: 100%;
    margin-bottom: 2.5rem;
  }
  .stats-grid > * { min-width: 0; }
  .stat-card {
    position: relative;
    background: var(--surface);
    padding: 1.25rem;
    border-radius: 1.25rem;
    border: 1px solid var(--border);
    box-shadow: var(--shadow-sm);
    cursor: pointer;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    min-height: 8.5rem;
    overflow: hidden;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  }
  .stat-card::before {
    content: '';
    position: absolute;
    top: 0; left: 0; right: 0;
    height: 4px;
    background: linear-gradient(90deg, var(--primary), #818cf8);
    opacity: 0;
    transition: opacity 0.3s ease;
  }
  .stat-card:hover { 
    transform: translateY(-4px);
    box-shadow: var(--shadow-lg); 
    border-color: #cbd5e1;
  }
  .stat-card:hover::before { opacity: 1; }
  
  .stat-icon-wrapper {
    width: 48px;
    height: 48px;
    border-radius: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: 1rem;
  }
  .icon-blue { background: #eff6ff; color: #3b82f6; }
  .icon-orange { background: #fff7ed; color: #f97316; }
  .icon-green { background: #f0fdf4; color: #22c55e; }
  
  .stat-value {
    font-size: 1.75rem;
    font-weight: 800;
    color: var(--text-main);
    z-index: 1;
    letter-spacing: -0.02em;
  }
  .stat-footer {
    display: flex;
    justify-content: space-between;
    align-items: flex-end;
    z-index: 1;
    margin-top: 0.5rem;
  }
  .stat-label {
    font-size: 1rem;
    font-weight: 600;
    color: var(--text-muted);
  }
  .stat-arrow {
    color: var(--primary);
    opacity: 0;
    transform: translate(-10px, 10px);
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  }
  .stat-card:hover .stat-arrow { opacity: 1; transform: translate(0, 0); }

  /* Main Layout Grid */
  .main-layout {
    display: grid;
    grid-template-columns: 2fr 1fr;
    gap: 1.5rem;
    align-items: stretch;
    margin-bottom: 2.5rem;
  }

  /* Panels */
  .content-panel {
    background: var(--surface);
    padding: 1.25rem;
    border-radius: 1.25rem;
    border: 1px solid var(--border);
    box-shadow: var(--shadow-sm);
    display: flex;
    flex-direction: column;
    transition: box-shadow 0.3s ease;
  }
  .content-panel:hover {
    box-shadow: var(--shadow-md);
  }
  
  .panel-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 1.5rem;
  }
  .section-title {
    font-size: 1.25rem;
    font-weight: 700;
    color: var(--text-main);
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  /* Analytics Specifics */
  .chart-container {
    width: 100%;
    height: 300px;
    margin-top: 1rem;
  }
  
  .chart-tooltip {
    background: rgba(255, 255, 255, 0.95);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 1rem;
    box-shadow: var(--shadow-lg);
    backdrop-filter: blur(4px);
  }

  /* Map Specifics */
  .map-legend {
    display: flex;
    flex-wrap: wrap;
    gap: 1.25rem;
    margin-bottom: 1.25rem;
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--text-muted);
    background: #f8fafc;
    padding: 1rem;
    border-radius: 0.75rem;
    border: 1px solid var(--border);
  }
  .legend-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .legend-dot {
    width: 0.75rem;
    height: 0.75rem;
    border-radius: 50%;
    box-shadow: inset 0 0 0 2px rgba(255,255,255,0.5);
  }
  .map-container-wrapper {
    width: 100%;
    height: 400px;
    border-radius: 1rem;
    overflow: hidden;
    border: 1px solid var(--border);
    flex-grow: 1;
    z-index: 0;
  }

  /* Activity Specifics */
  .activity-list {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    flex-grow: 1;
  }
  .activity-item {
    padding: 1rem 1.25rem;
    border-radius: 0.75rem;
    border: 1px solid #f1f5f9;
    background: #fafafa;
    display: flex;
    justify-content: space-between;
    align-items: center;
    transition: all 0.2s;
    cursor: pointer;
  }
  .activity-item:hover { 
    background: var(--surface); 
    border-color: #cbd5e1;
    transform: translateX(4px);
    box-shadow: var(--shadow-sm);
  }
  .activity-info {
    display: flex;
    align-items: center;
    gap: 1rem;
  }
  .activity-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }
  .activity-text {
    font-size: 0.9rem;
    font-weight: 600;
    color: #334155;
  }
  .activity-time {
    font-size: 0.8rem;
    font-weight: 600;
    color: #94a3b8;
    background: #f1f5f9;
    padding: 0.25rem 0.5rem;
    border-radius: 0.375rem;
  }
  
  /* Quick Actions */
  .actions-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1rem;
  }
  .action-btn {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    padding: 1rem;
    background: #f8fafc;
    border: 1px solid var(--border);
    border-radius: 1rem;
    cursor: pointer;
    transition: all 0.2s;
    color: var(--text-main);
    font-weight: 600;
    font-size: 0.85rem;
  }
  .action-btn:hover {
    background: var(--surface);
    border-color: var(--primary);
    color: var(--primary);
    box-shadow: var(--shadow-md);
    transform: translateY(-2px);
  }

  .view-all-btn {
    margin-top: 1.5rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 0.75rem;
    font-size: 0.9rem;
    font-weight: 700;
    color: var(--primary);
    background: #eef2ff;
    border: none;
    border-radius: 0.75rem;
    cursor: pointer;
    transition: all 0.2s;
    width: 100%;
  }
  .view-all-btn:hover { 
    background: #e0e7ff; 
    color: var(--primary-hover);
  }
  
  .loading-skeleton {
    animation: pulse-bg 1.5s infinite;
    background: #e2e8f0;
    border-radius: 0.375rem;
    height: 1.25rem;
  }
  @keyframes pulse-bg {
    0% { opacity: 0.6; }
    50% { opacity: 1; }
    100% { opacity: 0.6; }
  }

  /* Leaflet Popups Reset */
  .leaflet-popup-content-wrapper {
    border-radius: 8px;
    box-shadow: var(--shadow-md);
  }

  /* Responsiveness */
  @media (max-width: 1200px) {
    .main-layout { grid-template-columns: 1fr; }
  }
  @media (max-width: 1100px) {
    .stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  }
  @media (max-width: 768px) {
    .header-section { flex-direction: column; align-items: flex-start; gap: 1.5rem; }
    .header-actions { width: 100%; justify-content: space-between; }
    .stats-grid { grid-template-columns: 1fr; }
    .dashboard-container { padding: 1rem; }
  }
`;

const CustomTooltip = ({ active, payload, label }) => {
  if (active && payload && payload.length) {
    return (
      <div className="chart-tooltip">
        <p style={{ fontWeight: 'bold', marginBottom: '0.5rem', color: '#0f172a' }}>{label}</p>
        {payload.map((entry, index) => (
          <p key={index} style={{ color: entry.color || entry.fill, fontSize: '0.9rem', fontWeight: 600 }}>
            {entry.name}: {entry.value}
          </p>
        ))}
      </div>
    );
  }
  return null;
};

export default function Dashboard({ onViewChange = () => { }, onActionClick = () => { }, access }) {
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [isRefreshing, setIsRefreshing] = useState(true);
  const [dashboard, setDashboard] = useState(null);
  const lastRealtimeRefresh = useRef(0);

  useEffect(() => {
    let cancelled = false;
    setIsRefreshing(true);
    void fetchDashboardData(supabase).then((data) => {
      if (!cancelled) setDashboard(data);
    }).catch((error) => {
      if (!cancelled) {
        setDashboard({
          successfulQueries: 0, totalQueries: 1, warnings: [error.message],
          metrics: {}, incidents: null, mapMarkers: null, resourceData: null, activities: null
        });
      }
    }).finally(() => { if (!cancelled) setIsRefreshing(false); });
    return () => { cancelled = true; };
  }, [refreshVersion]);

  useEffect(() => {
    const refresh = () => setRefreshVersion((version) => version + 1);
    const poll = setInterval(refresh, 60_000);
    // Polling is the fallback when Realtime publication is not enabled for a table.
    const channel = supabase.channel('dashboard-live-changes');
    const onChange = () => {
      if (Date.now() - lastRealtimeRefresh.current < 15_000) return;
      lastRealtimeRefresh.current = Date.now();
      refresh();
    };
    for (const table of ['community_reports', 'advisories', 'evacuation_centers', 'monitoring_stations', 'station_catalog']) {
      channel.on('postgres_changes', { event: '*', schema: 'public', table }, onChange);
    }
    channel.subscribe();
    return () => { clearInterval(poll); void supabase.removeChannel(channel); };
  }, []);

  const handleRefresh = () => setRefreshVersion((version) => version + 1);
  const metrics = dashboard?.metrics || {};
  const reportData = dashboard?.incidents;
  const resourceData = dashboard?.resourceData;
  const hazardMarkers = dashboard?.mapMarkers;
  const activities = dashboard?.activities;
  const hasData = (dashboard?.successfulQueries || 0) > 0;
  const isPartial = hasData && dashboard.successfulQueries < dashboard.totalQueries;
  const statusLabel = !dashboard ? 'Loading Data' : !hasData ? 'Data Unavailable' : isPartial ? 'Partial Data' : 'Data Connected';
  const analytics = [
    {
      value: metrics.activeAdvisories ?? '—', label: 'Active Advisory Records', icon: <AlertTriangle size={24} />,
      target: 'content-advisories', colorClass: 'icon-orange'
    },
    {
      value: metrics.pendingReports ?? '—', label: 'Pending Reports', icon: <ClipboardList size={24} />,
      target: 'community-reports-moderation', colorClass: 'icon-blue'
    },
    {
      value: metrics.reportingStations ? `${metrics.reportingStations.healthy}/${metrics.reportingStations.total}` : '—',
      label: 'Fresh PAGASA Stations', icon: <Activity size={24} />, target: 'monitoring-stations', colorClass: 'icon-green'
    },
  ];

  const handleExport = () => {
    if (!dashboard || !hasData) return;
    // Use the existing safe quoted CSV writer and neutralize spreadsheet formulas from source titles.
    const safe = (value) => {
      const text = String(value ?? '');
      return /^[\s]*[=+@-]/.test(text) ? `'${text}` : text;
    };
    const rows = [];
    for (const metric of analytics) rows.push({
      section: 'Metric', time: dashboard.fetchedAt,
      label: metric.label, value: metric.value, details: ''
    });
    for (const bucket of reportData || []) rows.push({
      section: 'Incident trend (4-hour bucket)',
      time: bucket.time, label: 'Community reports submitted', value: bucket.reports, details: ''
    });
    for (const resource of resourceData || []) rows.push({
      section: 'Evacuation capacity', time: '',
      label: safe(resource.name), value: resource.used, details: `Capacity: ${resource.capacity}; available: ${resource.available}`
    });
    for (const event of activities || []) rows.push({
      section: 'Recent activity', time: event.time,
      label: safe(event.text), value: '', details: safe(event.source)
    });
    for (const marker of hazardMarkers || []) rows.push({
      section: 'Map location', time: marker.observedAt,
      label: safe(marker.label), value: marker.type,
      details: safe(`${marker.detail}; latitude: ${marker.position[0]}; longitude: ${marker.position[1]}`)
    });
    exportCsv('rescuar-dashboard-live-data', rows,
      [{ label: 'Section', key: 'section' }, { label: 'Time', key: 'time' },
      { label: 'Label', key: 'label' }, { label: 'Value', key: 'value' }, { label: 'Details', key: 'details' }]);
  };

  const getMarkerColor = (type) => {
    switch (type) {
      case 'flood': return '#3b82f6';
      case 'report': return '#f97316';
      case 'pending': return '#94a3b8';
      case 'evac': return '#10b981';
      default: return '#94a3b8';
    }
  };

  const getActivityColor = (type) => {
    switch (type) {
      case 'high': return '#ef4444';
      case 'medium': return '#f97316';
      case 'info': return '#3b82f6';
      case 'success': return '#10b981';
      default: return '#94a3b8';
    }
  };

  return (
    <div className="dashboard-container">
      <style>{injectedStyles}</style>

      {/* Header Section */}
      <div className="header-section">
        <div className="header-left">
          <div className="title-row">
            <h1>RescuAR Command Center</h1>
            <div className="status-badge" aria-live="polite" style={(!hasData && dashboard) || isPartial ? { background: '#fffbeb', color: '#92400e', borderColor: '#fde68a' } : {}}>
              <div className="status-dot" style={hasData && !isPartial ? {} : { background: '#d97706', animation: 'none' }}></div>
              {statusLabel}
            </div>
          </div>
          <p className="last-updated">Last successful fetch: {hasData ? formatDashboardTimestamp(dashboard.fetchedAt) : 'Not yet available'} · Auto-refresh every 60 seconds</p>
        </div>

        <div className="header-actions">
          <button onClick={handleExport} className="btn-outline" disabled={!hasData || isRefreshing} title="Export currently loaded database records">
            <Download size={18} />
            Export Report
          </button>
          <button onClick={handleRefresh} className="btn-primary" disabled={isRefreshing}>
            <RefreshCw size={18} className={isRefreshing ? 'animate-spin' : ''} />
            {isRefreshing ? 'Syncing...' : 'Sync Data'}
          </button>
        </div>
      </div>

      {Boolean(dashboard?.warnings?.length) && (
        <div role="alert" style={{ marginBottom: '1.25rem', padding: '0.85rem 1rem', background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '0.75rem', color: '#92400e', fontSize: '0.88rem' }}>
          <strong>Data notice:</strong> Some sources may be unavailable or incomplete.
          <ul style={{ margin: '0.35rem 0 0 1.25rem', padding: 0 }}>
            {dashboard.warnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        </div>
      )}

      {/* Top Stats Grid */}
      <div className="stats-grid" aria-label="Dashboard overview">
        {analytics.map((stat, idx) => (
          <div key={idx} className="stat-card-cell">
            <button type="button" disabled={!canView(access, stat.target)} onClick={() => onViewChange(stat.target)} className="stat-card h-100 w-100" style={{ textAlign: 'left', cursor: canView(access, stat.target) ? 'pointer' : 'default' }}>
              <div className={`stat-icon-wrapper ${stat.colorClass}`}>
                {stat.icon}
              </div>
              <div>
                <div className="stat-value">{stat.value}</div>
                <div className="stat-footer">
                  <span className="stat-label">{stat.label}</span>
                  <ArrowUpRight size={24} className="stat-arrow" />
                </div>
              </div>
            </button>
          </div>
        ))}
      </div>

      {/* Main Content Layout */}
      <div className="main-layout">

        {/* Left Column: Analytics & Map */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>

          {/* Analytics Panel */}
          <div className="content-panel">
            <div className="panel-header">
              <h2 className="section-title"><Activity size={20} color="#4f46e5" /> Incident Reports (Last 24 Hours)</h2>
            </div>
            <div className="chart-container">
              {reportData === null || reportData === undefined ? <p role="status" style={{ color: '#64748b' }}>Incident data unavailable.</p> : <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={reportData} margin={{ top: 10, right: 30, left: -20, bottom: 0 }}>
                  <defs>
                    <linearGradient id="colorReports" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#4f46e5" stopOpacity={0.3} />
                      <stop offset="95%" stopColor="#4f46e5" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <XAxis dataKey="time" stroke="#94a3b8" fontSize={12} tickLine={false} axisLine={false} />
                  <YAxis stroke="#94a3b8" fontSize={12} tickLine={false} axisLine={false} />
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                  <RechartsTooltip content={<CustomTooltip />} />
                  <Area type="monotone" dataKey="reports" stroke="#4f46e5" strokeWidth={3} fillOpacity={1} fill="url(#colorReports)" name="Reports Submitted" />
                </AreaChart>
              </ResponsiveContainer>}
            </div>
            {dashboard?.incidentTruncated && <p style={{ fontSize: '0.8rem', color: '#92400e' }}>Showing only the latest 1,000 reports. The chart is incomplete for high-volume periods.</p>}
          </div>

          {/* Map Panel */}
          <div className="content-panel">
            <div className="panel-header">
              <h2 className="section-title"><MapIcon size={20} color="#4f46e5" /> Reported Incidents and Evacuation Centers</h2>
            </div>

            <div className="map-legend">
              <span style={{ fontWeight: 700, color: '#475569' }}>Legend:</span>
              <div className="legend-item"><div className="legend-dot" style={{ backgroundColor: '#3b82f6' }}></div> Approved flood report</div>
              <div className="legend-item"><div className="legend-dot" style={{ backgroundColor: '#f97316' }}></div> Other approved report</div>
              <div className="legend-item"><div className="legend-dot" style={{ backgroundColor: '#94a3b8' }}></div> Pending/unverified report</div>
              <div className="legend-item"><div className="legend-dot" style={{ backgroundColor: '#10b981' }}></div> Evacuation center (check status)</div>
            </div>

            <p style={{ fontSize: '0.78rem', color: '#64748b', marginBottom: '0.75rem' }}>Pins reflect geolocated records from Supabase, not verified flood hazard polygons. Reports are limited to the past 30 days. Center status is shown in each popup.</p>
            {hazardMarkers?.length === 0 && <p role="status" style={{ color: '#64748b', marginBottom: '0.5rem' }}>No geolocated records were returned by the available sources.</p>}
            {!hazardMarkers && <p role="status" style={{ color: '#b45309', marginBottom: '0.5rem' }}>Map records unavailable.</p>}
            <div className="map-container-wrapper">
              <MapContainer
                center={[14.6416, 121.0950]}
                zoom={14}
                scrollWheelZoom={true}
                style={{ height: '100%', width: '100%', zIndex: 0 }}
              >
                <LayersControl position="topright">
                  <LayersControl.BaseLayer checked name="Carto Voyager">
                    <TileLayer
                      url="https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png"
                      attribution='&copy; <a href="https://carto.com/attributions">CARTO</a>'
                    />
                  </LayersControl.BaseLayer>
                  <LayersControl.BaseLayer name="Standard OSM">
                    <TileLayer
                      url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                      attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                    />
                  </LayersControl.BaseLayer>
                </LayersControl>

                {(hazardMarkers || []).map(marker => (
                  <CircleMarker
                    key={marker.id}
                    center={marker.position}
                    radius={8}
                    fillColor={getMarkerColor(marker.type)}
                    fillOpacity={0.8}
                    color="white"
                    weight={2}
                  >
                    <Popup className="premium-popup">
                      <strong style={{ color: '#0f172a' }}>{marker.label}</strong>
                      <div style={{ color: '#475569', marginTop: '4px' }}>{marker.detail}</div>
                      {marker.observedAt && <div style={{ fontSize: '0.75rem', marginTop: '4px' }}>{formatDashboardTimestamp(marker.observedAt)}</div>}
                    </Popup>
                  </CircleMarker>
                ))}
              </MapContainer>
            </div>
          </div>
        </div>

        {/* Right Column: Activity & Actions */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>

          {/* Quick Actions Panel */}
          <div className="content-panel">
            <div className="panel-header">
              <h2 className="section-title">Quick Actions</h2>
            </div>
            <div className="actions-grid">
              {canView(access, 'content-advisories') && <button type="button" className="action-btn" onClick={() => onActionClick('advisory')}>
                <Bell size={24} color="#4f46e5" /> New Advisory
              </button>}
              {canView(access, 'community-reports-moderation') && <button type="button" className="action-btn" onClick={() => onViewChange('community-reports-moderation')}>
                <Shield size={24} color="#10b981" /> Review Reports
              </button>}
              {canView(access, 'community-user-management') && <button type="button" className="action-btn" onClick={() => onViewChange('community-user-management')}>
                <Users size={24} color="#f97316" /> Manage Users
              </button>}
              {canView(access, 'monitoring-stations') && <button type="button" className="action-btn" onClick={() => onViewChange('monitoring-stations')}>
                <Video size={24} color="#3b82f6" /> Monitoring Stations
              </button>}
            </div>
          </div>

          {/* Resources Bar Chart Panel */}
          <div className="content-panel">
            <div className="panel-header">
              <h2 className="section-title">Evacuation Center Capacity</h2>
            </div>
            <div style={{ height: '220px', width: '100%' }}>
              {!resourceData ? <p role="status" style={{ color: '#64748b' }}>Evacuation capacity data unavailable.</p> : resourceData.length === 0 ? <p role="status" style={{ color: '#64748b' }}>No centers have recorded capacity and occupancy.</p> : <ResponsiveContainer width="100%" height="100%">
                <BarChart data={resourceData} margin={{ top: 10, right: 0, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
                  <XAxis dataKey="name" stroke="#94a3b8" fontSize={11} tickLine={false} axisLine={false} tickFormatter={(name) => name.length > 14 ? `${name.slice(0, 13)}…` : name} />
                  <YAxis stroke="#94a3b8" fontSize={12} tickLine={false} axisLine={false} />
                  <RechartsTooltip cursor={{ fill: '#f1f5f9' }} content={<CustomTooltip />} />
                  <Bar dataKey="available" name="Available capacity" fill="#94a3b8" radius={[4, 4, 0, 0]} barSize={20} />
                  <Bar dataKey="used" name="Current evacuees" fill="#4f46e5" radius={[4, 4, 0, 0]} barSize={20} />
                </BarChart>
              </ResponsiveContainer>}
            </div>
          </div>

          {/* Connected Recent Activity Panel */}
          <div className="content-panel" style={{ flexGrow: 1 }}>
            <div className="panel-header">
              <h2 className="section-title">Recent Activity</h2>
            </div>

            <div className="activity-list">
              {isRefreshing && !dashboard ? (
                <>
                  <div className="loading-skeleton" style={{ width: '100%', marginBottom: '8px' }}></div>
                  <div className="loading-skeleton" style={{ width: '80%', marginBottom: '8px' }}></div>
                  <div className="loading-skeleton" style={{ width: '90%' }}></div>
                </>
              ) : activities === null || activities === undefined ? (
                <p role="status" style={{ color: '#64748b' }}>Recent activity unavailable.</p>
              ) : activities.length === 0 ? (
                <p role="status" style={{ color: '#64748b' }}>No recent community reports or advisories.</p>
              ) : (
                activities.map((activity) => (
                  <div key={activity.id} className="activity-item">
                    <div className="activity-info">
                      <div className="activity-dot" style={{ backgroundColor: getActivityColor(activity.type) }}></div>
                      <span className="activity-text">{activity.text}</span>
                    </div>
                    <span className="activity-time" title={activity.time}>{formatDashboardTimestamp(activity.time)}</span>
                  </div>
                ))
              )}
            </div>

            <p style={{ marginTop: '1rem', fontSize: '0.77rem', color: '#64748b' }}>
              This feed shows actual creation timestamps, not a full administrator audit trail.
            </p>
            {canView(access, 'community-reports-moderation') && <button className="view-all-btn" onClick={() => onViewChange('community-reports-moderation')}>
              View Community Reports
              <ChevronRight size={18} style={{ marginLeft: '4px' }} />
            </button>}
          </div>

        </div>

      </div>
    </div>
  );
}
