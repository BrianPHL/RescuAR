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
  if (range === '24h') return date.toLocaleTimeString('en-PH', { timeZone: 'Asia/Manila', hour: '2-digit', minute: '2-digit' });
  if (range === '7d') return date.toLocaleDateString('en-PH', { timeZone: 'Asia/Manila', weekday: 'short', hour: '2-digit' });
  return date.toLocaleDateString('en-PH', { timeZone: 'Asia/Manila', month: 'short', day: 'numeric' });
}

function finiteNumberOrNull(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function getLevelContext(status, currentLevel, thresholds) {
  const alert = finiteNumberOrNull(thresholds?.ALARM_1);
  const alarm = finiteNumberOrNull(thresholds?.ALARM_2);
  const critical = finiteNumberOrNull(thresholds?.ALARM_3);

  if (!Number.isFinite(currentLevel)) {
    return {
      tone: 'unavailable',
      title: 'Water level unavailable',
      summary: 'No verified water-level value is currently available for this station.',
      proximity: 'Threshold distance unavailable.'
    };
  }

  if (status === 'Critical') {
    const difference = Number.isFinite(critical) ? currentLevel - critical : null;
    return {
      tone: 'critical',
      title: 'Critical threshold reached',
      summary: 'Emergency response actions should be reviewed immediately using the latest verified PAGASA observation.',
      proximity: difference == null ? 'Critical threshold exceeded.' : `${difference.toFixed(2)} m above Critical level`
    };
  }

  if (status === 'Alarm') {
    const difference = Number.isFinite(alarm) ? currentLevel - alarm : null;
    return {
      tone: 'alarm',
      title: 'Alarm threshold reached',
      summary: 'Escalated flood-response actions are available for review and dispatch.',
      proximity: difference == null ? 'Alarm threshold reached.' : `${difference.toFixed(2)} m above Alarm level`
    };
  }

  if (status === 'Alert') {
    const difference = Number.isFinite(alert) ? currentLevel - alert : null;
    return {
      tone: 'alert',
      title: 'Alert threshold reached',
      summary: 'Prepare public information and review exposure conditions while monitoring the next PAGASA observations.',
      proximity: difference == null ? 'Alert threshold reached.' : `${difference.toFixed(2)} m above Alert level`
    };
  }

  const belowAlert = Number.isFinite(alert) ? alert - currentLevel : null;
  return {
    tone: 'normal',
    title: 'No flood-related dispatch currently required',
    summary: "The verified level remains below PAGASA's Alert threshold. Monitoring and historical recording continue normally.",
    proximity: belowAlert == null ? 'PAGASA Alert threshold unavailable.' : `${Math.max(0, belowAlert).toFixed(2)} m below Alert level`
  };
}

function getDispatchActions(status) {
  if (status === 'Critical') {
    return {
      advisory: { enabled: true, title: 'Issue Emergency Advisory', subtitle: 'Draft an urgent public flood warning' },
      notify: { enabled: true, title: 'Notify Residents Now', subtitle: 'Send SMS & push emergency broadcast' },
      exposure: { enabled: true, title: 'Open Exposure Analysis', subtitle: 'Review reference flood exposure immediately' }
    };
  }
  if (status === 'Alarm') {
    return {
      advisory: { enabled: true, title: 'Generate Flood Advisory', subtitle: 'Draft an escalated public flood warning' },
      notify: { enabled: true, title: 'Notify Residents', subtitle: 'Send SMS & push broadcast' },
      exposure: { enabled: true, title: 'Review Exposure Analysis', subtitle: 'Review reference flood exposure' }
    };
  }
  if (status === 'Alert') {
    return {
      advisory: { enabled: true, title: 'Generate Advisory', subtitle: 'Draft an Alert-level public notice' },
      notify: { enabled: true, title: 'Prepare Resident Notification', subtitle: 'Review SMS & push broadcast before dispatch' },
      exposure: { enabled: true, title: 'Review Exposure Analysis', subtitle: 'Review reference flood exposure' }
    };
  }
  return {
    advisory: { enabled: false, title: 'Generate Advisory', subtitle: 'Enabled when PAGASA Alert level is reached' },
    notify: { enabled: false, title: 'Notify Residents', subtitle: 'Enabled when PAGASA Alert level is reached' },
    exposure: { enabled: true, title: 'Open Exposure Analysis', subtitle: 'Review reference flood exposure' }
  };
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
  const levelContext = getLevelContext(currentAlert.status, currentLevel, thresholds);
  const dispatchActions = getDispatchActions(currentAlert.status);

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
    // RPC aggregates long ranges on the server and includes null buckets for missing days.
    // Execute database/migrations/003_pagasa_river_chart.sql before deploying this UI.
    const bucket = range === '24h' ? 'raw' : range === '7d' ? 'hour' : 'day';
    const { data, error } = await supabase.rpc('pagasa_river_chart', {
      p_station_code: stationCode,
      p_since: since,
      p_until: new Date().toISOString(),
      p_bucket: bucket
    });
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
    return history.map((row) => ({
      time: formatChartLabel(row.observed_at, timeRange),
      observed: row.level == null ? null : Number(row.level),
      observed_at: row.observed_at
    }));
  }, [history, timeRange]);

  const hasMeasurements = chartData.some((row) => Number.isFinite(row.observed));

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
              {stationsList.map((station) => <option key={station.station_code} value={station.station_code}>{station.station_name} ({Number(station.level).toFixed(2)} m EL)</option>)}
            </select>
          </div>
          <button className="refresh-btn" onClick={() => void loadData(selectedStationCode, timeRange)} disabled={isRefreshing}>
            <RefreshCw size={14} className={isRefreshing ? 'spin-icon' : ''} /><span>Refresh View</span>
          </button>
        </div>
      </div>

      {errorMessage && <div style={{ marginBottom: 14, padding: '10px 12px', borderRadius: 8, background: '#fef2f2', color: '#b91c1c' }}>{errorMessage}</div>}

      <div className="river-metrics-grid row g-3">
        <div className="col-12 col-md-6 col-xl-4">
        <div className="river-card current-level-card h-100">
          <div className="card-top-label"><Waves size={16} className="text-brand" /><span>CURRENT VERIFIED WATER-LEVEL ELEVATION</span></div>
          <div className="level-hero-group">
            <div className="hero-number-wrapper"><span className="hero-number">{Number.isFinite(currentLevel) ? currentLevel.toFixed(2) : '--'}</span><span className="hero-unit">m EL</span></div>
            <div className="trend-chip" style={{ color: trend.color }}><span>{trend.symbol}</span><span>{trend.label}</span></div>
          </div>
          <div className="level-status-pill" style={{ backgroundColor: `${currentAlert.color}15`, color: currentAlert.color, borderColor: `${currentAlert.color}40`, fontWeight: 700 }}>
            <AlertTriangle size={14} /><span>{currentAlert.label.toUpperCase()}</span>
          </div>
          <div className={`threshold-distance threshold-distance-${levelContext.tone}`}>{levelContext.proximity}</div>
          {Number.isFinite(Number(selectedStation?.delta_30m_m)) && <div className="level-change-note">30-minute change: {Number(selectedStation.delta_30m_m) >= 0 ? '+' : ''}{Number(selectedStation.delta_30m_m).toFixed(2)} m</div>}
          <div className="elevation-definition">PAGASA reports WL [EL.m] as water-level elevation at the station datum; it is not river depth.</div>
        </div>
        </div>

        <div className="col-12 col-md-6 col-xl-4">
        <div className="river-card thresholds-card h-100">
          <div className="card-top-label"><ShieldAlert size={16} className="text-brand" /><span>PAGASA SOURCE THRESHOLDS</span></div>
          <div className="thresholds-progress-stack">
            {thresholdRows.length ? thresholdRows.map(([name, value, color]) => (
              <div key={name} className="threshold-bar-item">
                <div className="threshold-info"><span className="thresh-name">{name}{currentAlert.status === name && <span className="active-tag">CURRENT</span>}</span><span className="thresh-val">{value.toFixed(2)} m EL</span></div>
                <div className="thresh-track"><div className="thresh-fill" style={{ width: `${Math.min(100, Math.max(0, ((currentLevel || 0) / value) * 100))}%`, background: color }}></div></div>
              </div>
            )) : <div style={{ color: '#64748b', fontSize: 13 }}>PAGASA did not provide warning thresholds for this station.</div>}
          </div>
          <div style={{ marginTop: 10, color: '#64748b', fontSize: 12 }}>Threshold source: {thresholds.source}</div>
        </div>
        </div>

        <div className="col-12 col-md-6 col-xl-4">
        <div className="river-card actions-card h-100">
          <div className="card-top-label"><Zap size={16} className="text-brand" /><span>DISPATCH & ACTIONS</span></div>
          <div className={`dispatch-condition dispatch-condition-${levelContext.tone}`}>
            <div className="dispatch-condition-title">{levelContext.title}</div>
            <div className="dispatch-condition-summary">{levelContext.summary}</div>
          </div>
          <div className="action-buttons-stack">
            <button
              className={`action-tile advisory ${dispatchActions.advisory.enabled ? '' : 'action-tile-disabled'}`}
              disabled={!dispatchActions.advisory.enabled}
              aria-disabled={!dispatchActions.advisory.enabled}
              onClick={() => dispatchActions.advisory.enabled && onActionClick?.('advisory', { stationName: stationDisplayName, level: currentLevel, alertStatus: currentAlert.status, alertLabel: currentAlert.label })}
            ><div className="tile-icon-box blue"><Megaphone size={16} /></div><div className="tile-text"><span className="tile-title">{dispatchActions.advisory.title}</span><span className="tile-sub">{dispatchActions.advisory.subtitle}</span></div>{dispatchActions.advisory.enabled && <ChevronRight size={16} className="tile-arrow" />}</button>
            <button
              className={`action-tile notify ${dispatchActions.notify.enabled ? '' : 'action-tile-disabled'}`}
              disabled={!dispatchActions.notify.enabled}
              aria-disabled={!dispatchActions.notify.enabled}
              onClick={() => dispatchActions.notify.enabled && onActionClick?.('notify')}
            ><div className="tile-icon-box amber"><Bell size={16} /></div><div className="tile-text"><span className="tile-title">{dispatchActions.notify.title}</span><span className="tile-sub">{dispatchActions.notify.subtitle}</span></div>{dispatchActions.notify.enabled && <ChevronRight size={16} className="tile-arrow" />}</button>
            <button className="action-tile predict" onClick={() => onActionClick?.('predict')}><div className="tile-icon-box teal"><Compass size={16} /></div><div className="tile-text"><span className="tile-title">{dispatchActions.exposure.title}</span><span className="tile-sub">{dispatchActions.exposure.subtitle}</span></div><ChevronRight size={16} className="tile-arrow" /></button>
          </div>
        </div>
        </div>
      </div>

      <div className="river-card chart-main-card">
        <div className="chart-header-row">
          <div className="chart-title-group"><Activity size={18} className="text-brand" /><div><h2 className="chart-heading">Verified PAGASA River Level History</h2><span className="chart-subheading">{stationDisplayName} • official observation timestamps • normal/below-threshold readings retained for trend monitoring</span></div></div>
          <div className="chart-controls"><div className="pill-selector">{Object.entries(RANGE_OPTIONS).map(([key, option]) => <button key={key} className={`pill-btn ${timeRange === key ? 'active' : ''}`} onClick={() => setTimeRange(key)}>{option.label}</button>)}</div></div>
        </div>
        <div className="chart-canvas-container">
          {!hasMeasurements ? <div style={{ height: 380, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#64748b' }}>No verified PAGASA observations are available for this range.</div> : (
            <ResponsiveContainer width="100%" height={380}>
              <AreaChart data={chartData} margin={{ top: 20, right: 65, left: 0, bottom: 10 }}>
                <defs><linearGradient id="gradientObserved" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#0284c7" stopOpacity={0.4} /><stop offset="95%" stopColor="#0284c7" stopOpacity={0.02} /></linearGradient></defs>
                <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="time" tickLine={false} axisLine={{ stroke: '#cbd5e1' }} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} minTickGap={20} />
                <YAxis domain={yDomain} tickFormatter={(value) => `${value} m EL`} tickLine={false} axisLine={false} tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }} />
                <Tooltip formatter={(value) => [`${Number(value).toFixed(2)} m EL`, 'Observed']} />
                {Number.isFinite(thresholds.ALARM_3) && <ReferenceArea y1={thresholds.ALARM_3} y2={yDomain[1]} fill="#ef4444" fillOpacity={0.04} />}
                {Number.isFinite(thresholds.ALARM_1) && <ReferenceLine y={thresholds.ALARM_1} stroke="#ca8a04" strokeDasharray="6 4" label={{ value: `ALERT ${thresholds.ALARM_1.toFixed(1)} m EL`, position: 'right', fill: '#ca8a04', fontSize: 10 }} />}
                {Number.isFinite(thresholds.ALARM_2) && <ReferenceLine y={thresholds.ALARM_2} stroke="#ea580c" strokeDasharray="6 4" label={{ value: `ALARM ${thresholds.ALARM_2.toFixed(1)} m EL`, position: 'right', fill: '#ea580c', fontSize: 10 }} />}
                {Number.isFinite(thresholds.ALARM_3) && <ReferenceLine y={thresholds.ALARM_3} stroke="#dc2626" strokeDasharray="6 4" label={{ value: `CRITICAL ${thresholds.ALARM_3.toFixed(1)} m EL`, position: 'right', fill: '#dc2626', fontSize: 10 }} />}
                <Area type="monotone" connectNulls={false} dataKey="observed" stroke="#0284c7" strokeWidth={3} fill="url(#gradientObserved)" dot={timeRange === '24h' ? { r: 3 } : false} />
              </AreaChart>
            </ResponsiveContainer>
          )}
        </div>
      </div>
    </div>
  );
}
