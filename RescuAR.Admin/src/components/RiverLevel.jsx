import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus, getStationThresholds } from '../utils/waterLevelUtils';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import {
  RefreshCw,
  ArrowUpRight,
  ChevronRight,
  AlertTriangle,
  ShieldAlert,
  Megaphone,
  Bell,
  Compass,
  Waves,
  Clock,
  Activity,
  Zap,
  Info
} from 'lucide-react';
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ReferenceArea
} from 'recharts';

const RANGE_OPTIONS = {
  '24h': { label: '24-Hour', ms: 24 * 60 * 60 * 1000 },
  '7d': { label: '7-Day', ms: 7 * 24 * 60 * 60 * 1000 },
  '30d': { label: '30-Day', ms: 30 * 24 * 60 * 60 * 1000 },
  '6mo': { label: '6-Month', ms: 183 * 24 * 60 * 60 * 1000 }
};

function formatLastUpdated(value) {
  if (!value) return 'No verified timestamp';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Invalid timestamp';
  return date.toLocaleString('en-PH', {
    year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit'
  });
}

function formatChartLabel(value, range) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  if (range === '24h') {
    return date.toLocaleTimeString('en-PH', { hour: '2-digit', minute: '2-digit' });
  }
  if (range === '7d') {
    return date.toLocaleDateString('en-PH', { weekday: 'short', hour: '2-digit' });
  }
  if (range === '30d') {
    return date.toLocaleDateString('en-PH', { month: 'short', day: 'numeric' });
  }
  return date.toLocaleDateString('en-PH', { month: 'short', day: 'numeric' });
}

function aggregateDaily(rows) {
  const groups = new Map();
  rows.forEach((row) => {
    const date = new Date(row.observed_at);
    if (Number.isNaN(date.getTime())) return;
    const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    const level = Number(row.level);
    if (!Number.isFinite(level)) return;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(level);
  });
  return [...groups.entries()].map(([day, values]) => ({
    observed_at: `${day}T12:00:00`,
    level: values.reduce((a, b) => a + b, 0) / values.length,
    sampleCount: values.length
  }));
}

export default function RiverLevel({ onActionClick }) {
  const [timeRange, setTimeRange] = useState('6mo');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [stationsList, setStationsList] = useState([]);
  const [selectedStationId, setSelectedStationId] = useState(null);
  const [selectedStation, setSelectedStation] = useState(null);
  const [history, setHistory] = useState([]);
  const [historyError, setHistoryError] = useState('');
  const [nowMs, setNowMs] = useState(Date.now());

  const stationDisplayName = selectedStation?.station_name || 'Sto. Niño Station';
  const currentLevel = Number(selectedStation?.level) || 0;
  const currentAlert = calculateAlertStatus(currentLevel, stationDisplayName);
  const currentStationThresholds = getStationThresholds(stationDisplayName);
  const freshness = getTelemetryFreshness(selectedStation?.updated_at, nowMs);

  const fetchAllStations = useCallback(async () => {
    const { data, error } = await supabase
      .from('monitoring_stations')
      .select('*')
      .order('station_name', { ascending: true });

    if (error) throw error;
    return data || [];
  }, []);

  const fetchHistory = useCallback(async (stationName, range) => {
    if (!stationName) return [];
    const since = new Date(Date.now() - RANGE_OPTIONS[range].ms).toISOString();
    const { data, error } = await supabase
      .from('river_level_history')
      .select('station_name, level, status, source, observed_at')
      .eq('station_name', stationName)
      .eq('source', 'PAGASA')
      .gte('observed_at', since)
      .order('observed_at', { ascending: true });

    if (error) throw error;
    return data || [];
  }, []);

  const loadData = useCallback(async (targetId = null, range = timeRange) => {
    setIsRefreshing(true);
    setHistoryError('');
    try {
      const list = await fetchAllStations();
      setStationsList(list);

      let target = null;
      const desiredId = targetId ?? selectedStationId;
      if (desiredId !== null) {
        target = list.find((s) => String(s.id) === String(desiredId));
      }
      if (!target) {
        target = list.find((s) => s.station_name?.toLowerCase().includes('sto')) || list[0] || null;
      }

      if (!target) {
        setSelectedStation(null);
        setHistory([]);
        return;
      }

      setSelectedStationId(target.id);
      setSelectedStation(target);

      try {
        const historyRows = await fetchHistory(target.station_name, range);
        setHistory(historyRows);
      } catch (error) {
        console.error('Historical telemetry query failed:', error);
        setHistory([]);
        setHistoryError('Historical telemetry could not be loaded. Verify river_level_history and the scraper worker.');
      }
    } catch (error) {
      console.error('River telemetry load failed:', error);
      setHistoryError('Live telemetry could not be loaded.');
    } finally {
      setIsRefreshing(false);
    }
  }, [fetchAllStations, fetchHistory, selectedStationId, timeRange]);

  useEffect(() => {
    loadData(null, timeRange);
    const freshnessTimer = setInterval(() => setNowMs(Date.now()), 60 * 1000);

    const channel = supabase
      .channel('river-level-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => loadData(null, timeRange))
      .on('postgres_changes', { event: 'INSERT', schema: 'public', table: 'river_level_history' }, () => loadData(null, timeRange))
      .subscribe();

    return () => {
      clearInterval(freshnessTimer);
      supabase.removeChannel(channel);
    };
  }, [timeRange]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleStationChange = async (e) => {
    const newId = e.target.value;
    setSelectedStationId(newId);
    const target = stationsList.find((s) => String(s.id) === String(newId));
    if (target) {
      setSelectedStation(target);
      setHistoryError('');
      try {
        setHistory(await fetchHistory(target.station_name, timeRange));
      } catch (error) {
        console.error(error);
        setHistory([]);
        setHistoryError('Historical telemetry could not be loaded.');
      }
    }
  };

  const handleRangeChange = async (range) => {
    setTimeRange(range);
    if (!selectedStation?.station_name) return;
    setIsRefreshing(true);
    setHistoryError('');
    try {
      setHistory(await fetchHistory(selectedStation.station_name, range));
    } catch (error) {
      console.error(error);
      setHistory([]);
      setHistoryError('Historical telemetry could not be loaded.');
    } finally {
      setIsRefreshing(false);
    }
  };

  const chartData = useMemo(() => {
    const sourceRows = timeRange === '30d' || timeRange === '6mo' ? aggregateDaily(history) : history;
    return sourceRows.map((row) => ({
      time: formatChartLabel(row.observed_at, timeRange),
      observedAt: row.observed_at,
      observed: Number(Number(row.level).toFixed(2)),
      source: 'PAGASA',
      sampleCount: row.sampleCount || 1
    }));
  }, [history, timeRange]);

  const CustomTooltip = ({ active, payload }) => {
    if (!active || !payload?.length) return null;
    const point = payload[0]?.payload;
    const value = Number(point?.observed);
    const alert = calculateAlertStatus(value, stationDisplayName);
    return (
      <div className="river-chart-glass-tooltip">
        <div className="tooltip-header"><Clock size={12} /><span>{formatLastUpdated(point?.observedAt)}</span></div>
        <div className="tooltip-metrics">
          <div className="tooltip-row observed">
            <span className="tooltip-dot blue"></span>
            <span className="tooltip-label">Observed Level:</span>
            <span className="tooltip-value">{Number.isFinite(value) ? value.toFixed(2) : '--'} m</span>
          </div>
          <div className="tooltip-row observed">
            <span className="tooltip-label">Source:</span>
            <span className="tooltip-value">PAGASA</span>
          </div>
          {point?.sampleCount > 1 && (
            <div className="tooltip-row observed">
              <span className="tooltip-label">Daily samples:</span>
              <span className="tooltip-value">{point.sampleCount}</span>
            </div>
          )}
        </div>
        <div className="tooltip-footer-badge" style={{ color: alert.color, backgroundColor: `${alert.color}15`, borderColor: `${alert.color}30` }}>
          {alert.label}
        </div>
      </div>
    );
  };

  const yDomain = useMemo(() => {
    const values = chartData.map((d) => d.observed).filter(Number.isFinite);
    const thresholds = [currentStationThresholds.ALARM_1, currentStationThresholds.ALARM_2, currentStationThresholds.ALARM_3];
    const all = [...values, ...thresholds].filter(Number.isFinite);
    if (!all.length) return ['auto', 'auto'];
    const min = Math.max(0, Math.floor(Math.min(...all) - 2));
    const max = Math.ceil(Math.max(...all) + 2);
    return [min, max];
  }, [chartData, currentStationThresholds]);

  return (
    <div className="river-level-view">
      <div className="river-header">
        <div className="title-group">
          <div className="title-row" style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
            <h1 className="river-title">{stationDisplayName} Level</h1>
            <span className="live-telemetry-badge" style={{ color: freshness.color, borderColor: freshness.borderColor, backgroundColor: freshness.backgroundColor }}>
              <span className="pulse-dot-green" style={{ backgroundColor: freshness.dotColor }}></span>
              {freshness.label} Telemetry
            </span>
          </div>
          <p className="river-subtitle">Last verified: {formatLastUpdated(selectedStation?.updated_at)} • {freshness.ageText}</p>
        </div>

        <div className="header-actions" style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div className="station-selector-wrapper" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <label htmlFor="station-select" style={{ fontSize: '0.85rem', fontWeight: '600', color: 'var(--text-muted, #64748b)' }}>Station:</label>
            <select id="station-select" value={selectedStationId || ''} onChange={handleStationChange} style={{ padding: '6px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', backgroundColor: '#ffffff', fontSize: '0.875rem', fontWeight: '600', color: '#1e293b', cursor: 'pointer', outline: 'none', boxShadow: '0 1px 2px rgba(0, 0, 0, 0.05)' }}>
              {stationsList.length === 0 ? <option value="">Sto. Niño Station</option> : stationsList.map((st) => <option key={st.id} value={st.id}>{st.station_name} ({st.level}m)</option>)}
            </select>
          </div>
          <button className="refresh-btn" onClick={() => loadData(selectedStationId, timeRange)} disabled={isRefreshing}>
            <RefreshCw size={14} className={isRefreshing ? 'spin-icon' : ''} /><span>Refresh Telemetry</span>
          </button>
        </div>
      </div>

      <div className="river-metrics-grid">
        <div className="river-card current-level-card">
          <div className="card-top-label"><Waves size={16} className="text-brand" /><span>CURRENT WATER LEVEL</span></div>
          <div className="level-hero-group">
            <div className="hero-number-wrapper"><span className="hero-number">{currentLevel ? currentLevel.toFixed(2) : '--'}</span><span className="hero-unit">meters</span></div>
            <div className="trend-chip rising"><ArrowUpRight size={16} /><span>{stationDisplayName}</span></div>
          </div>
          <div className="level-status-pill" style={{ backgroundColor: `${currentAlert.color}15`, color: currentAlert.color, borderColor: `${currentAlert.color}40`, fontWeight: '700' }}>
            <AlertTriangle size={14} /><span>{currentAlert.label.toUpperCase()}</span>
          </div>
        </div>

        <div className="river-card thresholds-card">
          <div className="card-top-label"><ShieldAlert size={16} className="text-brand" /><span>{stationDisplayName.toUpperCase()} ALERT THRESHOLDS</span></div>
          <div className="thresholds-progress-stack">
            {[['1st Alarm', 'Alert Level 1 (Alarm)', currentStationThresholds.ALARM_1, 'level-1', 'fill-level-1'], ['2nd Alarm', 'Alert Level 2 (Prepare)', currentStationThresholds.ALARM_2, 'level-2', 'fill-level-2'], ['3rd Alarm', 'Alert Level 3 (Evacuate)', currentStationThresholds.ALARM_3, 'level-3', 'fill-level-3']].map(([status, name, threshold, levelClass, fillClass], idx, arr) => {
              const prev = idx === 0 ? 0 : arr[idx - 1][2];
              const width = currentLevel >= threshold ? 100 : Math.max(0, ((currentLevel - prev) / Math.max(0.01, threshold - prev)) * 100);
              return (
                <div key={status} className={`threshold-bar-item ${levelClass} ${currentAlert.status === status ? 'active' : ''}`}>
                  <div className="threshold-info"><span className="thresh-name">{name}{currentAlert.status === status && <span className="active-tag">CURRENT</span>}</span><span className="thresh-val">{threshold.toFixed(2)} meters</span></div>
                  <div className="thresh-track"><div className={`thresh-fill ${fillClass}`} style={{ width: `${Math.min(100, width)}%` }}></div></div>
                </div>
              );
            })}
          </div>
        </div>

        <div className="river-card actions-card">
          <div className="card-top-label"><Zap size={16} className="text-brand" /><span>DISPATCH & ACTIONS</span></div>
          <div className="action-buttons-stack">
            <button className="action-tile advisory" onClick={() => onActionClick?.('advisory', { stationName: stationDisplayName, level: currentLevel, alertStatus: currentAlert.status, alertLabel: currentAlert.label })}><div className="tile-icon-box blue"><Megaphone size={16} /></div><div className="tile-text"><span className="tile-title">Generate Advisory</span><span className="tile-sub">Draft public flood warning</span></div><ChevronRight size={16} className="tile-arrow" /></button>
            <button className="action-tile notify" onClick={() => onActionClick?.('notify')}><div className="tile-icon-box amber"><Bell size={16} /></div><div className="tile-text"><span className="tile-title">Notify Residents</span><span className="tile-sub">Send SMS & push broadcast</span></div><ChevronRight size={16} className="tile-arrow" /></button>
            <button className="action-tile predict" onClick={() => onActionClick?.('predict')}><div className="tile-icon-box teal"><Compass size={16} /></div><div className="tile-text"><span className="tile-title">Run Inundation Sim</span><span className="tile-sub">Model affected barangays</span></div><ChevronRight size={16} className="tile-arrow" /></button>
          </div>
        </div>
      </div>

      <div className="river-card chart-main-card">
        <div className="chart-header-row">
          <div className="chart-title-group"><Activity size={18} className="text-brand" /><div><h2 className="chart-heading">Verified River Level History</h2><span className="chart-subheading">{stationDisplayName} • Timestamped measurements only</span></div></div>
          <div className="chart-controls"><div className="pill-selector">
            {Object.entries(RANGE_OPTIONS).map(([key, option]) => <button key={key} className={`pill-btn ${timeRange === key ? 'active' : ''}`} onClick={() => handleRangeChange(key)}>{option.label}</button>)}
          </div></div>
        </div>

        {historyError && <div style={{ margin: '12px 20px 0', padding: '10px 12px', borderRadius: '8px', background: '#fef2f2', color: '#b91c1c', fontSize: '0.85rem' }}>{historyError}</div>}

        <div className="chart-canvas-container">
          {chartData.length === 0 ? (
            <div style={{ height: 380, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#64748b', textAlign: 'center', padding: 24 }}>
              No verified historical observations are available for this station in the selected {RANGE_OPTIONS[timeRange].label.toLowerCase()} range yet.
            </div>
          ) : (
            <ResponsiveContainer width="100%" height={380}>
              <AreaChart data={chartData} margin={{ top: 20, right: 65, left: 0, bottom: 10 }}>
                <defs><linearGradient id="gradientObserved" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#0284c7" stopOpacity={0.4} /><stop offset="95%" stopColor="#0284c7" stopOpacity={0.02} /></linearGradient></defs>
                <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="time" tickLine={false} axisLine={{ stroke: '#cbd5e1' }} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} dy={8} minTickGap={20} />
                <YAxis domain={yDomain} tickFormatter={(val) => `${val}m`} tickLine={false} axisLine={false} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} dx={-6} />
                <Tooltip content={<CustomTooltip />} />
                <ReferenceArea y1={currentStationThresholds.ALARM_3} y2={yDomain[1]} fill="#ef4444" fillOpacity={0.04} />
                <ReferenceLine y={currentStationThresholds.ALARM_1} stroke="#ca8a04" strokeDasharray="6 4" strokeWidth={1.5} label={{ value: `ALERT 1 (${currentStationThresholds.ALARM_1.toFixed(1)}m)`, position: 'right', fill: '#ca8a04', fontSize: 10, fontWeight: '800' }} />
                <ReferenceLine y={currentStationThresholds.ALARM_2} stroke="#ea580c" strokeDasharray="6 4" strokeWidth={2} label={{ value: `ALARM 2 (${currentStationThresholds.ALARM_2.toFixed(1)}m)`, position: 'right', fill: '#ea580c', fontSize: 10, fontWeight: '800' }} />
                <ReferenceLine y={currentStationThresholds.ALARM_3} stroke="#dc2626" strokeDasharray="6 4" strokeWidth={2} label={{ value: `CRITICAL (${currentStationThresholds.ALARM_3.toFixed(1)}m)`, position: 'right', fill: '#dc2626', fontSize: 10, fontWeight: '800' }} />
                <Area type="monotone" dataKey="observed" stroke="#0284c7" strokeWidth={3} fillOpacity={1} fill="url(#gradientObserved)" dot={timeRange === '24h' || timeRange === '7d' ? { r: 4, fill: '#0284c7', stroke: '#ffffff', strokeWidth: 2 } : false} activeDot={{ r: 6, fill: '#0284c7', stroke: '#ffffff', strokeWidth: 3 }} connectNulls />
              </AreaChart>
            </ResponsiveContainer>
          )}
        </div>

        <div className="chart-footer-bar">
          <div className="chart-legend-items"><div className="legend-chip"><span className="chip-indicator solid-blue"></span><span className="chip-text">Verified Observed Water Level</span></div></div>
          <div className="chart-info-note"><Info size={13} /><span>{timeRange === '6mo' ? 'Six-month view uses daily averages of stored PAGASA measurements; no synthetic values are generated.' : 'Every plotted point is a verified PAGASA observation from river_level_history.'}</span></div>
        </div>
      </div>
    </div>
  );
}
