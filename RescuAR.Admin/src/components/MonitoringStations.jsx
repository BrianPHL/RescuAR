import React, { useEffect, useMemo, useState } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus, formatTrend } from '../utils/waterLevelUtils';
import { getTelemetryFreshness, formatTelemetryAge } from '../utils/telemetryFreshness';
import {
  RefreshCw, Droplet, Waves, AlertTriangle, Info, BarChart2,
  ArrowUpDown, ArrowUp, ArrowDown, CheckCircle2, WifiOff
} from 'lucide-react';
import {
  ResponsiveContainer, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip
} from 'recharts';

const SOURCE_ID = 'PAGASA_FFWWS';
const unavailableAlert = {
  status: 'Unavailable', label: 'Unverified thresholds', color: '#b91c1c'
};

function hasVerifiedThresholds(row) {
  return ['alert_threshold_m', 'alarm_threshold_m', 'critical_threshold_m'].every((key) =>
    row[key] !== null && row[key] !== undefined && row[key] !== '' && Number.isFinite(Number(row[key]))
  );
}

function thresholdPayload(row) {
  return {
    alert_threshold_m: row.alert_threshold_m,
    alarm_threshold_m: row.alarm_threshold_m,
    critical_threshold_m: row.critical_threshold_m
  };
}

function formatDateTime(value) {
  if (!value) return 'No verified timestamp';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Invalid timestamp';
  return date.toLocaleString('en-PH', {
    year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit'
  });
}

export default function MonitoringStations() {
  const [stations, setStations] = useState([]);
  const [sourceStatus, setSourceStatus] = useState(null);
  const [catalogWarning, setCatalogWarning] = useState(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [showSummary, setShowSummary] = useState(true);
  const [sortConfig, setSortConfig] = useState({ key: null, direction: null });
  const [nowMs, setNowMs] = useState(Date.now());

  const loadData = async () => {
    setIsRefreshing(true);
    try {
      const [
        { data: stationRows, error: stationError },
        { data: sourceRows, error: sourceError },
        { data: catalogRows, error: catalogError }
      ] = await Promise.all([
        supabase
          .from('monitoring_stations')
          .select('*')
          .eq('source', SOURCE_ID)
          .order('level', { ascending: false }),
        supabase
          .from('telemetry_source_status')
          .select('*')
          .eq('source', SOURCE_ID)
          .limit(1),
        supabase
          .from('station_catalog')
          .select('station_code, enabled, is_primary')
          .eq('source', SOURCE_ID)
      ]);

      if (stationError) throw stationError;
      if (sourceError) console.warn('Unable to load PAGASA source health:', sourceError.message);

      // Do not keep an inactive old Sto. Niño gauge on the live monitoring page
      // when the catalog has switched to its separately verified alternate.
      const catalogLoaded = !catalogError && Array.isArray(catalogRows) && catalogRows.length > 0;
      const activeCodes = new Set((catalogRows || [])
        .filter((item) => item.enabled === true).map((item) => String(item.station_code).trim()));
      const visibleRows = catalogLoaded
        ? (stationRows || []).filter((row) => activeCodes.has(String(row.station_code).trim()))
        : (stationRows || []);
      setCatalogWarning(catalogLoaded ? null : 'Active PAGASA station catalog could not be verified; displayed records may include inactive stations.');

      const formatted = visibleRows.map((row, index) => {
        const level = Number(row.level);
        const validLevel = row.level !== null && row.level !== undefined && row.level !== '' && Number.isFinite(level);
        const observedAt = row.observed_at || row.updated_at;
        const alertInfo = hasVerifiedThresholds(row) && getTelemetryFreshness(observedAt).isUsableAsLive
          ? calculateAlertStatus(validLevel ? level : null, row.station_name, thresholdPayload(row))
          : unavailableAlert;
        return {
          ...row,
          id: row.id || row.station_code || index + 1,
          name: row.station_name,
          level: validLevel ? level : null,
          alertInfo,
          observedAt,
          sourceCheckedAt: row.source_checked_at,
          isPrimary: catalogRows?.find((item) => String(item.station_code).trim() === String(row.station_code).trim())?.is_primary === true
        };
      });

      setStations(formatted);
      setSourceStatus(sourceRows?.[0] || null);
      setNowMs(Date.now());
    } catch (error) {
      console.error('Monitoring telemetry load failed:', error);
    } finally {
      setIsRefreshing(false);
    }
  };

  useEffect(() => {
    void loadData();
    // Realtime notifications may not be enabled for these Supabase tables.
    const timer = setInterval(() => {
      setNowMs(Date.now());
      void loadData();
    }, 60 * 1000);
    const channel = supabase
      .channel('pagasa-monitoring-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => void loadData())
      .on('postgres_changes', { event: '*', schema: 'public', table: 'telemetry_source_status' }, () => void loadData())
      .subscribe();

    return () => {
      clearInterval(timer);
      supabase.removeChannel(channel);
    };
  }, []);

  const latestObservation = useMemo(() => {
    const times = stations.map((s) => new Date(s.observedAt).getTime()).filter(Number.isFinite);
    return times.length ? new Date(Math.max(...times)).toISOString() : null;
  }, [stations]);

  const sourceCheckAge = sourceStatus?.last_check_at
    ? formatTelemetryAge(Math.max(0, nowMs - new Date(sourceStatus.last_check_at).getTime()))
    : 'No source check recorded';

  const handleSort = (key) => {
    setSortConfig((previous) => {
      if (previous.key !== key) return { key, direction: 'ascending' };
      if (previous.direction === 'ascending') return { key, direction: 'descending' };
      return { key: null, direction: null };
    });
  };

  const sortedStations = useMemo(() => {
    if (!sortConfig.key || !sortConfig.direction) return [...stations];
    return [...stations].sort((a, b) => {
      const av = a[sortConfig.key] ?? '';
      const bv = b[sortConfig.key] ?? '';
      const result = typeof av === 'string'
        ? av.localeCompare(String(bv))
        : Number(av) - Number(bv);
      return sortConfig.direction === 'ascending' ? result : -result;
    });
  }, [stations, sortConfig]);

  const getSortIcon = (key) => {
    if (sortConfig.key !== key) return <ArrowUpDown size={12} className="sort-icon-inactive" />;
    return sortConfig.direction === 'ascending'
      ? <ArrowUp size={12} className="sort-icon-active" />
      : <ArrowDown size={12} className="sort-icon-active" />;
  };

  const freshnessRows = stations.map((station) => getTelemetryFreshness(station.observedAt, nowMs));
  const unavailableCount = freshnessRows.filter((item) => item.status === 'unavailable').length;
  const delayedCount = freshnessRows.filter((item) => ['delayed', 'stale'].includes(item.status)).length;
  const warningCount = stations.filter((station) => !['Normal', 'Unavailable'].includes(station.alertInfo.status)).length;

  const sourceHealthy = sourceStatus?.worker_status === 'healthy' && sourceStatus?.source_status === 'reachable';

  const hydrologicalSummary = () => {
    if (!stations.length) return 'No verified PAGASA station rows are available yet.';
    if (!sourceHealthy) return `The telemetry worker is not reporting a healthy PAGASA source connection${sourceStatus?.last_error_message ? `: ${sourceStatus.last_error_message}` : '.'}`;
    if (unavailableCount === stations.length) return 'PAGASA is reachable, but the latest verified measurements are too old to treat as live.';
    if (delayedCount || unavailableCount) return `${delayedCount} delayed/stale and ${unavailableCount} unavailable station reading(s). Values remain the last verified PAGASA observations.`;
    if (warningCount) return `${warningCount} station(s) currently meet a source-provided Alert, Alarm, or Critical threshold.`;
    return 'Verified PAGASA telemetry is current across the configured stations and all source-provided thresholds remain in the normal range.';
  };

  const getStation = (fragment) => [...stations]
    .filter((station) => station.name.toLowerCase().includes(fragment.toLowerCase()))
    .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary))[0];

  const renderPointCard = (title, station, Icon) => {
    const fallback = { name: title, level: null, alertInfo: calculateAlertStatus(null), observedAt: null, trend: 'Unknown' };
    const item = station || fallback;
    const freshness = getTelemetryFreshness(item.observedAt, nowMs);
    const trend = formatTrend(freshness.isUsableAsLive ? item.trend : 'Unknown');
    const isCritical = item.alertInfo.status === 'Critical';

    return (
      <div className="point-card" style={freshness.status === 'unavailable' ? { backgroundColor: '#f8fafc' } : {}}>
        <div className="point-card-left">
          <span className="point-card-title">{title}</span>
          <span className="point-card-value" style={{ color: isCritical ? '#b91c1c' : 'var(--text-main)' }}>
            {Number.isFinite(item.level) ? `${item.level.toFixed(2)} m` : '--'}
          </span>
          <div className="point-card-badge-row">
            <span className="status-badge-pill" style={{ backgroundColor: `${item.alertInfo.color}20`, color: item.alertInfo.color, border: `1px solid ${item.alertInfo.color}40` }}>
              {item.alertInfo.label}
            </span>
            <span className={`telemetry-state-badge compact ${freshness.status}`} title={freshness.description}>
              <span className={`telemetry-state-dot ${freshness.status}`}></span>
              {freshness.label} • {freshness.ageText}
            </span>
          </div>
          <div style={{ marginTop: 6, fontSize: 12, color: trend.color, fontWeight: 700 }}>
            {trend.symbol} {trend.label}{item.delta_30m_m !== null && item.delta_30m_m !== undefined && Number.isFinite(Number(item.delta_30m_m)) ? ` (${Number(item.delta_30m_m) >= 0 ? '+' : ''}${Number(item.delta_30m_m).toFixed(2)} m / 30 min)` : ''}
          </div>
        </div>
        <div className="point-card-right">
          <div className="point-card-icon-wrapper" style={{ backgroundColor: '#eff6ff', color: isCritical ? '#dc2626' : '#0284c7' }}>
            <Icon size={22} />
          </div>
        </div>
      </div>
    );
  };

  const chartData = [...stations]
    .filter((s) => Number.isFinite(s.level) && getTelemetryFreshness(s.observedAt, nowMs).isUsableAsLive)
    .sort((a, b) => b.level - a.level);
  const chartMax = chartData.length ? Math.ceil(Math.max(...chartData.map((s) => Math.max(s.level, Number(s.critical_threshold_m) || 0))) + 2) : 30;

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Monitoring Stations</h1>
          <span className="view-subtitle">Latest verified observation: {formatDateTime(latestObservation)}</span>
          <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', marginTop: 6, fontSize: 12, color: '#64748b' }}>
            <span><strong>PAGASA source:</strong> {sourceStatus?.source_status || 'unknown'}</span>
            <span><strong>Worker:</strong> {sourceStatus?.worker_status || 'unknown'}</span>
            <span><strong>Last source check:</strong> {sourceCheckAge}</span>
          </div>
        </div>
        <button className="btn-refresh" onClick={() => void loadData()} disabled={isRefreshing}>
          <RefreshCw size={14} className={isRefreshing ? 'animate-spin' : ''} />
          <span>Refresh View</span>
        </button>
      </div>

      {(!sourceHealthy || sourceStatus?.last_error_code || catalogWarning) && (
        <div style={{ marginBottom: 16, padding: '12px 14px', borderRadius: 10, background: '#fff7ed', border: '1px solid #fed7aa', color: '#9a3412', display: 'flex', gap: 10, alignItems: 'flex-start' }}>
          <WifiOff size={18} style={{ marginTop: 1 }} />
          <div><strong>PAGASA monitoring requires attention.</strong><div style={{ marginTop: 2, fontSize: 13 }}>{[sourceStatus?.last_error_message, catalogWarning].filter(Boolean).join(' ') || 'No recent healthy PAGASA observation cycle is available.'}</div></div>
        </div>
      )}

      <div className="station-points-grid row g-3">
        <div className="col-12 col-md-6 col-xxl-4">{renderPointCard('TUMANA MONITORING POINT', getStation('Tumana'), Droplet)}</div>
        <div className="col-12 col-md-6 col-xxl-4">{renderPointCard('NANGKA MONITORING POINT', getStation('Nangka'), Waves)}</div>
        <div className="col-12 col-md-6 col-xxl-4">{renderPointCard('STO. NIÑO (MAIN NODE)', getStation('Sto. Niño'), AlertTriangle)}</div>
      </div>

      <div className="stations-split-layout monitoring-stations-layout row g-3">
        <div className="col-12 col-xl-7">
        <div className="stations-card h-100">
          <h2 className="stations-card-title">Current Verified Station Water Levels</h2>
          {!chartData.length && <div style={{ fontSize: 12, color: '#9a3412', marginTop: 8 }}>No fresh, verified readings are available for the chart. Historical values remain in the table with their observation age.</div>}
          <div className="chart-container-wrapper" style={{ height: 300, marginTop: 16 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chartData} margin={{ top: 10, right: 10, left: -20, bottom: 5 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f1f5f9" />
                <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fill: 'var(--text-muted)', fontSize: 10, fontWeight: 500 }} />
                <YAxis domain={[0, chartMax]} tickLine={false} axisLine={false} tick={{ fill: 'var(--text-light)', fontSize: 11 }} />
                <Tooltip formatter={(value) => [`${Number(value).toFixed(2)} m`, 'Verified level']} />
                <Bar dataKey="level" fill="#3b82f6" barSize={36} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <div className="x-axis-title-centered">PAGASA FFWWS Configured Station</div>
          <div className="graph-type-label"><BarChart2 size={14} /><span>Graph Type: Bar Chart</span></div>
          <div className="checkbox-wrapper">
            <input type="checkbox" id="show-summary" checked={showSummary} onChange={(e) => setShowSummary(e.target.checked)} />
            <label htmlFor="show-summary">Show Hydrological Summary</label>
          </div>
          {showSummary && (
            <div className="hydrological-summary-box">
              <Info size={16} className="summary-icon" />
              <div><div className="summary-title">Hydrological Summary</div><div className="summary-text">{hydrologicalSummary()}</div></div>
            </div>
          )}
        </div>
        </div>

        <div className="col-12 col-xl-5">
        <div className="stations-card h-100">
          <h2 className="stations-card-title">PAGASA Telemetry Database</h2>
          <div className="table-container" style={{ marginTop: 16 }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th onClick={() => handleSort('name')} className="sortable-header"><div className="header-cell-content"><span>STATION</span>{getSortIcon('name')}</div></th>
                  <th onClick={() => handleSort('level')} className="sortable-header"><div className="header-cell-content"><span>LEVEL</span>{getSortIcon('level')}</div></th>
                  <th>TREND</th>
                  <th>OBSERVATION</th>
                  <th onClick={() => handleSort('status')} className="sortable-header"><div className="header-cell-content"><span>STATUS</span>{getSortIcon('status')}</div></th>
                </tr>
              </thead>
              <tbody>
                {sortedStations.map((station) => {
                  const freshness = getTelemetryFreshness(station.observedAt, nowMs);
                  const trend = formatTrend(freshness.isUsableAsLive ? station.trend : 'Unknown');
                  return (
                    <tr key={station.id} className="table-row-hover">
                      <td style={{ fontWeight: 600 }}>{station.name}<div style={{ fontSize: 11, color: '#94a3b8', marginTop: 2 }}>{station.station_code}</div></td>
                      <td style={{ fontWeight: 700 }}>{Number.isFinite(station.level) ? `${station.level.toFixed(2)} m` : '--'}</td>
                      <td style={{ color: trend.color, fontWeight: 700 }}>{trend.symbol} {trend.label}</td>
                      <td><span className={`telemetry-state-badge compact ${freshness.status}`} title={freshness.description}><span className={`telemetry-state-dot ${freshness.status}`}></span>{freshness.label} • {freshness.ageText}</span></td>
                      <td><span className="status-badge-pill" style={{ backgroundColor: `${station.alertInfo.color}20`, color: station.alertInfo.color, border: `1px solid ${station.alertInfo.color}40`, fontWeight: 700 }}>{station.alertInfo.status}</span></td>
                    </tr>
                  );
                })}
                {!sortedStations.length && <tr><td colSpan="5" style={{ textAlign: 'center', padding: 30, color: '#64748b' }}>No verified PAGASA telemetry has been stored yet.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
        </div>
      </div>
    </div>
  );
}
