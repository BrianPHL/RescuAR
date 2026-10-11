import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus, getStationThresholds, formatTrend } from '../utils/waterLevelUtils';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import {
  RefreshCw, ChevronRight, AlertTriangle, ShieldAlert, Megaphone, Bell,
  Compass, Waves, Activity, Zap
} from 'lucide-react';
import {
  ResponsiveContainer, AreaChart, Area, XAxis, YAxis, CartesianGrid,
  Tooltip, ReferenceLine, ReferenceArea
} from 'recharts';

const SOURCE_ID = 'PAGASA_FFWWS';
const RANGE_OPTIONS = {
  '24h': { label: '24-Hour', ms: 24 * 60 * 60 * 1000 },
  '7d': { label: '7-Day', ms: 7 * 24 * 60 * 60 * 1000 },
  '30d': { label: '30-Day', ms: 30 * 24 * 60 * 60 * 1000 },
  '6mo': { label: '6-Month', ms: 183 * 24 * 60 * 60 * 1000 }
};

function sourceThresholds(row) {
  return {
    alert_threshold_m: row?.alert_threshold_m,
    alarm_threshold_m: row?.alarm_threshold_m,
    critical_threshold_m: row?.critical_threshold_m
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

function formatChartLabel(value, range) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  if (range === '24h') return date.toLocaleTimeString('en-PH', { hour: '2-digit', minute: '2-digit' });
  if (range === '7d') return date.toLocaleDateString('en-PH', { weekday: 'short', hour: '2-digit' });
  return date.toLocaleDateString('en-PH', { month: 'short', day: 'numeric' });
}

function aggregateDaily(rows) {
  const groups = new Map();
  rows.forEach((row) => {
    const date = new Date(row.observed_at);
    const level = Number(row.level);
    if (Number.isNaN(date.getTime()) || !Number.isFinite(level)) return;
    const key = date.toISOString().slice(0, 10);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(level);
  });
  return [...groups.entries()].map(([day, values]) => ({
    observed_at: `${day}T12:00:00+08:00`,
    level: values.reduce((sum, value) => sum + value, 0) / values.length
  }));
}

export default function RiverLevel({ onActionClick }) {
  const [timeRange, setTimeRange] = useState('24h');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [stationsList, setStationsList] = useState([]);
  const [selectedStationCode, setSelectedStationCode] = useState('11104204');
  const [selectedStation, setSelectedStation] = useState(null);
  const [history, setHistory] = useState([]);
  const [errorMessage, setErrorMessage] = useState('');
  const [nowMs, setNowMs] = useState(Date.now());

  const stationDisplayName = selectedStation?.station_name || 'Sto. Niño Station';
  const currentLevel = selectedStation?.level == null ? null : Number(selectedStation.level);
  const thresholds = getStationThresholds(stationDisplayName, sourceThresholds(selectedStation));
  const currentAlert = calculateAlertStatus(currentLevel, stationDisplayName, sourceThresholds(selectedStation));
  const freshness = getTelemetryFreshness(selectedStation?.observed_at || selectedStation?.updated_at, nowMs);
  const trend = formatTrend(selectedStation?.trend);

  const fetchStations = useCallback(async () => {
    const { data, error } = await supabase
      .from('monitoring_stations')
      .select('*')
      .eq('source', SOURCE_ID)
      .order('station_name', { ascending: true });
    if (error) throw error;
    return data || [];
  }, []);

  const fetchHistory = useCallback(async (stationCode, range) => {
    if (!stationCode) return [];
    const since = new Date(Date.now() - RANGE_OPTIONS[range].ms).toISOString();
    const { data, error } = await supabase
      .from('river_level_history')
      .select('station_code, station_name, level, status, source, observed_at')
      .eq('station_code', stationCode)
      .eq('source', SOURCE_ID)
      .gte('observed_at', since)
      .order('observed_at', { ascending: true });
    if (error) throw error;
    return data || [];
  }, []);

  const loadData = useCallback(async (stationCode = selectedStationCode, range = timeRange) => {
    setIsRefreshing(true);
    setErrorMessage('');
    try {
      const list = await fetchStations();
      setStationsList(list);
      const target = list.find((row) => String(row.station_code) === String(stationCode)) || list.find((row) => row.station_name?.includes('Sto. Niño')) || list[0] || null;
      setSelectedStation(target);
      if (!target) {
        setHistory([]);
        return;
      }
      setSelectedStationCode(target.station_code);
      setHistory(await fetchHistory(target.station_code, range));
      setNowMs(Date.now());
    } catch (error) {
      console.error('River telemetry load failed:', error);
      setErrorMessage('Verified PAGASA river telemetry could not be loaded. Confirm the Supabase migration and telemetry worker are active.');
    } finally {
      setIsRefreshing(false);
    }
  }, [fetchHistory, fetchStations, selectedStationCode, timeRange]);

  useEffect(() => {
    void loadData(selectedStationCode, timeRange);
    const timer = setInterval(() => setNowMs(Date.now()), 60 * 1000);
    const channel = supabase
      .channel('pagasa-river-level-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => void loadData(selectedStationCode, timeRange))
      .on('postgres_changes', { event: 'INSERT', schema: 'public', table: 'river_level_history' }, () => void loadData(selectedStationCode, timeRange))
      .subscribe();
    return () => {
      clearInterval(timer);
      supabase.removeChannel(channel);
    };
  }, [selectedStationCode, timeRange]); // eslint-disable-line react-hooks/exhaustive-deps

  const chartData = useMemo(() => {
    const rows = ['30d', '6mo'].includes(timeRange) ? aggregateDaily(history) : history;
    return rows.map((row) => ({
      time: formatChartLabel(row.observed_at, timeRange),
      observed: Number(row.level),
      observed_at: row.observed_at
    }));
  }, [history, timeRange]);

  const yDomain = useMemo(() => {
    const values = chartData.map((row) => row.observed).filter(Number.isFinite);
    const thresholdValues = [thresholds.ALARM_1, thresholds.ALARM_2, thresholds.ALARM_3].filter(Number.isFinite);
    const all = [...values, ...thresholdValues];
    if (!all.length) return ['auto', 'auto'];
    return [Math.max(0, Math.floor(Math.min(...all) - 2)), Math.ceil(Math.max(...all) + 2)];
  }, [chartData, thresholds]);

  const thresholdRows = [
    ['Alert', thresholds.ALARM_1, '#ca8a04'],
    ['Alarm', thresholds.ALARM_2, '#ea580c'],
    ['Critical', thresholds.ALARM_3, '#dc2626']
  ].filter(([, value]) => Number.isFinite(value));

  return (
    <div className="river-level-view">
      <div className="river-header">
        <div className="title-group">
          <div className="title-row" style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <h1 className="river-title">{stationDisplayName} Level</h1>
            <span className="live-telemetry-badge" style={{ color: freshness.color, borderColor: freshness.borderColor, backgroundColor: freshness.backgroundColor }}>
              <span className="pulse-dot-green" style={{ backgroundColor: freshness.dotColor }}></span>{freshness.label} Telemetry
            </span>
          </div>
          <p className="river-subtitle">Last verified PAGASA observation: {formatDateTime(selectedStation?.observed_at || selectedStation?.updated_at)} • {freshness.ageText}</p>
        </div>
        <div className="header-actions" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <div className="station-selector-wrapper" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <label htmlFor="station-select" style={{ fontSize: '0.85rem', fontWeight: 600, color: '#64748b' }}>Station:</label>
            <select id="station-select" value={selectedStationCode || ''} onChange={(e) => setSelectedStationCode(e.target.value)} style={{ padding: '6px 12px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#fff', fontWeight: 600 }}>
              {stationsList.map((station) => <option key={station.station_code} value={station.station_code}>{station.station_name} ({Number(station.level).toFixed(2)}m)</option>)}
            </select>
          </div>
          <button className="refresh-btn" onClick={() => void loadData(selectedStationCode, timeRange)} disabled={isRefreshing}>
            <RefreshCw size={14} className={isRefreshing ? 'spin-icon' : ''} /><span>Refresh View</span>
          </button>
        </div>
      </div>

      {errorMessage && <div style={{ marginBottom: 14, padding: '10px 12px', borderRadius: 8, background: '#fef2f2', color: '#b91c1c' }}>{errorMessage}</div>}

      <div className="river-metrics-grid">
        <div className="river-card current-level-card">
          <div className="card-top-label"><Waves size={16} className="text-brand" /><span>CURRENT VERIFIED LEVEL</span></div>
          <div className="level-hero-group">
            <div className="hero-number-wrapper"><span className="hero-number">{Number.isFinite(currentLevel) ? currentLevel.toFixed(2) : '--'}</span><span className="hero-unit">meters</span></div>
            <div className="trend-chip" style={{ color: trend.color }}><span>{trend.symbol}</span><span>{trend.label}</span></div>
          </div>
          <div className="level-status-pill" style={{ backgroundColor: `${currentAlert.color}15`, color: currentAlert.color, borderColor: `${currentAlert.color}40`, fontWeight: 700 }}>
            <AlertTriangle size={14} /><span>{currentAlert.label.toUpperCase()}</span>
          </div>
          {Number.isFinite(Number(selectedStation?.delta_30m_m)) && <div style={{ marginTop: 8, fontSize: 12, color: '#64748b' }}>30-minute change: {Number(selectedStation.delta_30m_m) >= 0 ? '+' : ''}{Number(selectedStation.delta_30m_m).toFixed(2)} m</div>}
        </div>

        <div className="river-card thresholds-card">
          <div className="card-top-label"><ShieldAlert size={16} className="text-brand" /><span>PAGASA SOURCE THRESHOLDS</span></div>
          <div className="thresholds-progress-stack">
            {thresholdRows.length ? thresholdRows.map(([name, value, color]) => (
              <div key={name} className="threshold-bar-item">
                <div className="threshold-info"><span className="thresh-name">{name}{currentAlert.status === name && <span className="active-tag">CURRENT</span>}</span><span className="thresh-val">{value.toFixed(2)} meters</span></div>
                <div className="thresh-track"><div className="thresh-fill" style={{ width: `${Math.min(100, Math.max(0, ((currentLevel || 0) / value) * 100))}%`, background: color }}></div></div>
              </div>
            )) : <div style={{ color: '#64748b', fontSize: 13 }}>PAGASA did not provide warning thresholds for this station.</div>}
          </div>
          <div style={{ marginTop: 10, color: '#64748b', fontSize: 12 }}>Threshold source: {thresholds.source}</div>
        </div>

        <div className="river-card actions-card">
          <div className="card-top-label"><Zap size={16} className="text-brand" /><span>DISPATCH & ACTIONS</span></div>
          <div className="action-buttons-stack">
            <button className="action-tile advisory" onClick={() => onActionClick?.('advisory', { stationName: stationDisplayName, level: currentLevel, alertStatus: currentAlert.status, alertLabel: currentAlert.label })}><div className="tile-icon-box blue"><Megaphone size={16} /></div><div className="tile-text"><span className="tile-title">Generate Advisory</span><span className="tile-sub">Draft public flood warning</span></div><ChevronRight size={16} className="tile-arrow" /></button>
            <button className="action-tile notify" onClick={() => onActionClick?.('notify')}><div className="tile-icon-box amber"><Bell size={16} /></div><div className="tile-text"><span className="tile-title">Notify Residents</span><span className="tile-sub">Send SMS & push broadcast</span></div><ChevronRight size={16} className="tile-arrow" /></button>
            <button className="action-tile predict" onClick={() => onActionClick?.('predict')}><div className="tile-icon-box teal"><Compass size={16} /></div><div className="tile-text"><span className="tile-title">Open Exposure Analysis</span><span className="tile-sub">Review reference flood exposure</span></div><ChevronRight size={16} className="tile-arrow" /></button>
          </div>
        </div>
      </div>

      <div className="river-card chart-main-card">
        <div className="chart-header-row">
          <div className="chart-title-group"><Activity size={18} className="text-brand" /><div><h2 className="chart-heading">Verified PAGASA River Level History</h2><span className="chart-subheading">{stationDisplayName} • official observation timestamps</span></div></div>
          <div className="chart-controls"><div className="pill-selector">{Object.entries(RANGE_OPTIONS).map(([key, option]) => <button key={key} className={`pill-btn ${timeRange === key ? 'active' : ''}`} onClick={() => setTimeRange(key)}>{option.label}</button>)}</div></div>
        </div>
        <div className="chart-canvas-container">
          {!chartData.length ? <div style={{ height: 380, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#64748b' }}>No verified PAGASA observations are available for this range.</div> : (
            <ResponsiveContainer width="100%" height={380}>
              <AreaChart data={chartData} margin={{ top: 20, right: 65, left: 0, bottom: 10 }}>
                <defs><linearGradient id="gradientObserved" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#0284c7" stopOpacity={0.4} /><stop offset="95%" stopColor="#0284c7" stopOpacity={0.02} /></linearGradient></defs>
                <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="time" tickLine={false} axisLine={{ stroke: '#cbd5e1' }} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} minTickGap={20} />
                <YAxis domain={yDomain} tickFormatter={(value) => `${value}m`} tickLine={false} axisLine={false} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} />
                <Tooltip formatter={(value) => [`${Number(value).toFixed(2)} m`, 'Observed']} />
                {Number.isFinite(thresholds.ALARM_3) && <ReferenceArea y1={thresholds.ALARM_3} y2={yDomain[1]} fill="#ef4444" fillOpacity={0.04} />}
                {Number.isFinite(thresholds.ALARM_1) && <ReferenceLine y={thresholds.ALARM_1} stroke="#ca8a04" strokeDasharray="6 4" label={{ value: `ALERT ${thresholds.ALARM_1.toFixed(1)}m`, position: 'right', fill: '#ca8a04', fontSize: 10 }} />}
                {Number.isFinite(thresholds.ALARM_2) && <ReferenceLine y={thresholds.ALARM_2} stroke="#ea580c" strokeDasharray="6 4" label={{ value: `ALARM ${thresholds.ALARM_2.toFixed(1)}m`, position: 'right', fill: '#ea580c', fontSize: 10 }} />}
                {Number.isFinite(thresholds.ALARM_3) && <ReferenceLine y={thresholds.ALARM_3} stroke="#dc2626" strokeDasharray="6 4" label={{ value: `CRITICAL ${thresholds.ALARM_3.toFixed(1)}m`, position: 'right', fill: '#dc2626', fontSize: 10 }} />}
                <Area type="monotone" dataKey="observed" stroke="#0284c7" strokeWidth={3} fill="url(#gradientObserved)" dot={timeRange === '24h' ? { r: 3 } : false} connectNulls />
              </AreaChart>
            </ResponsiveContainer>
          )}
        </div>
      </div>
    </div>
  );
}
