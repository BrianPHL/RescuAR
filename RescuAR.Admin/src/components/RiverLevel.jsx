import React, { useState, useEffect } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus, getStationThresholds } from '../utils/waterLevelUtils';
import {
  RefreshCw,
  ArrowUpRight,
  ChevronRight,
  TrendingUp,
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
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ReferenceArea
} from 'recharts';

// 24 Hour Mock Telemetry Data
const MOCK_DATA_24H = [
  { time: '00:00', observed: 10.5, predicted: 10.2 },
  { time: '02:00', observed: 11.0, predicted: 10.7 },
  { time: '04:00', observed: 11.8, predicted: 11.3 },
  { time: '06:00', observed: 12.9, predicted: 12.3 },
  { time: '08:00', observed: 14.2, predicted: 13.5 },
  { time: '10:00', observed: 15.3, predicted: 14.6 },
  { time: '12:00', observed: 16.2, predicted: 15.4 }, // Current Peak
  { time: '14:00', observed: null, predicted: 16.1 },
  { time: '16:00', observed: null, predicted: 16.6 },
  { time: '18:00', observed: null, predicted: 17.1 },
  { time: '20:00', observed: null, predicted: 17.4 },
  { time: '22:00', observed: null, predicted: 17.6 }
];

// 7 Day Historical Data
const MOCK_DATA_7D = [
  { time: 'Mon', observed: 12.1, predicted: 12.3 },
  { time: 'Tue', observed: 13.5, predicted: 13.2 },
  { time: 'Wed', observed: 14.8, predicted: 14.9 },
  { time: 'Thu', observed: 16.2, predicted: 16.0 },
  { time: 'Fri', observed: 15.1, predicted: 15.3 },
  { time: 'Sat', observed: 14.0, predicted: 14.2 },
  { time: 'Sun', observed: 13.2, predicted: 13.5 }
];

export default function RiverLevel({ onActionClick }) {
  const [timeRange, setTimeRange] = useState('24h');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState('Loading live data...');
  const [stationsList, setStationsList] = useState([]);
  const [selectedStationId, setSelectedStationId] = useState(null);
  const [selectedStation, setSelectedStation] = useState(null);
  const [currentLevel, setCurrentLevel] = useState(12.1);
  const [currentAlert, setCurrentAlert] = useState(calculateAlertStatus(12.1));

  // Fetch all available scraped monitoring stations
  const fetchAllStations = async () => {
    try {
      const { data, error } = await supabase
        .from('monitoring_stations')
        .select('*')
        .order('station_name', { ascending: true });

      if (error) {
        console.error('Error fetching monitoring stations:', error);
        return [];
      }

      if (data && data.length > 0) {
        setStationsList(data);
        return data;
      }
    } catch (err) {
      console.error('Unexpected error fetching stations:', err);
    }
    return [];
  };

  // Load telemetry data for selected station (or default Sto. Niño)
  const updateStationViewData = (station) => {
    if (!station) return;
    const level = Number(station.level) || 0;
    setSelectedStation(station);
    setCurrentLevel(level);
    setCurrentAlert(calculateAlertStatus(level, station.station_name));

    const latestTimestamp = station.updated_at ? new Date(station.updated_at) : new Date();
    const dateStr = latestTimestamp.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });
    const timeStr = latestTimestamp.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
    setLastUpdated(`${dateStr} • ${timeStr}`);
  };

  const loadData = async (targetId = null) => {
    const list = await fetchAllStations();
    if (list.length > 0) {
      let target = null;
      const currentId = targetId !== null ? targetId : selectedStationId;
      if (currentId !== null) {
        target = list.find(s => String(s.id) === String(currentId));
      }
      // If no station matched or none selected yet, default to Sto. Niño or first station
      if (!target) {
        target = list.find(s => s.station_name.toLowerCase().includes('sto')) || list[0];
      }
      setSelectedStationId(target.id);
      updateStationViewData(target);
    }
  };

  useEffect(() => {
    loadData();

    const channel = supabase
      .channel('river-level-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => {
        loadData();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const handleStationChange = (e) => {
    const newId = e.target.value;
    setSelectedStationId(newId);
    const target = stationsList.find(s => String(s.id) === String(newId));
    if (target) {
      updateStationViewData(target);
    }
  };

  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await loadData();
    } finally {
      setTimeout(() => {
        setIsRefreshing(false);
      }, 500);
    }
  };

  // Glassmorphic Custom Tooltip for Recharts
  const CustomTooltip = ({ active, payload, label }) => {
    if (active && payload && payload.length) {
      const obsVal = payload.find(p => p.dataKey === 'observed')?.value;
      const predVal = payload.find(p => p.dataKey === 'predicted')?.value;
      const currentVal = obsVal ?? predVal ?? 0;

      let statusText = 'Normal Level';
      let statusColor = '#16a34a';
      if (currentVal >= 18) {
        statusText = 'Alarm Level 3 (Evacuation)';
        statusColor = '#dc2626';
      } else if (currentVal >= 16) {
        statusText = 'Alarm Level 2 (Preparation)';
        statusColor = '#ea580c';
      } else if (currentVal >= 15) {
        statusText = 'Alarm Level 1 (Monitoring)';
        statusColor = '#ca8a04';
      }

      return (
        <div className="river-chart-glass-tooltip">
          <div className="tooltip-header">
            <Clock size={12} />
            <span>Time Stamp: {label}</span>
          </div>

          <div className="tooltip-metrics">
            {obsVal !== undefined && obsVal !== null && (
              <div className="tooltip-row observed">
                <span className="tooltip-dot blue"></span>
                <span className="tooltip-label">Observed Level:</span>
                <span className="tooltip-value">{obsVal.toFixed(1)} m</span>
              </div>
            )}

            {predVal !== undefined && predVal !== null && (
              <div className="tooltip-row predicted">
                <span className="tooltip-dot orange"></span>
                <span className="tooltip-label">Predicted Forecast:</span>
                <span className="tooltip-value">{predVal.toFixed(1)} m</span>
              </div>
            )}
          </div>

          <div
            className="tooltip-footer-badge"
            style={{ color: statusColor, backgroundColor: `${statusColor}15`, borderColor: `${statusColor}30` }}
          >
            {statusText}
          </div>
        </div>
      );
    }
    return null;
  };

  const stationDisplayName = selectedStation ? selectedStation.station_name : 'River';
  const currentStationThresholds = getStationThresholds(stationDisplayName);

  // Generate dynamic chart data proportional to the active station's level & threshold baseline
  const getDynamicChartData = () => {
    const baseVal = currentLevel > 0 ? currentLevel : currentStationThresholds.ALARM_1 - 2.5;
    
    if (timeRange === '24h') {
      return [
        { time: '00:00', observed: Number((baseVal - 3.5).toFixed(1)), predicted: Number((baseVal - 3.8).toFixed(1)) },
        { time: '02:00', observed: Number((baseVal - 3.0).toFixed(1)), predicted: Number((baseVal - 3.3).toFixed(1)) },
        { time: '04:00', observed: Number((baseVal - 2.2).toFixed(1)), predicted: Number((baseVal - 2.7).toFixed(1)) },
        { time: '06:00', observed: Number((baseVal - 1.3).toFixed(1)), predicted: Number((baseVal - 1.7).toFixed(1)) },
        { time: '08:00', observed: Number((baseVal - 0.5).toFixed(1)), predicted: Number((baseVal - 0.9).toFixed(1)) },
        { time: '10:00', observed: Number((baseVal - 0.1).toFixed(1)), predicted: Number((baseVal - 0.3).toFixed(1)) },
        { time: '12:00', observed: Number(baseVal.toFixed(1)), predicted: Number((baseVal - 0.1).toFixed(1)) },
        { time: '14:00', observed: null, predicted: Number((baseVal + 0.3).toFixed(1)) },
        { time: '16:00', observed: null, predicted: Number((baseVal + 0.7).toFixed(1)) },
        { time: '18:00', observed: null, predicted: Number((baseVal + 1.1).toFixed(1)) },
        { time: '20:00', observed: null, predicted: Number((baseVal + 1.4).toFixed(1)) },
        { time: '22:00', observed: null, predicted: Number((baseVal + 1.6).toFixed(1)) }
      ];
    }

    return [
      { time: 'Mon', observed: Number((baseVal - 2.5).toFixed(1)), predicted: Number((baseVal - 2.3).toFixed(1)) },
      { time: 'Tue', observed: Number((baseVal - 1.8).toFixed(1)), predicted: Number((baseVal - 2.0).toFixed(1)) },
      { time: 'Wed', observed: Number((baseVal - 1.0).toFixed(1)), predicted: Number((baseVal - 0.8).toFixed(1)) },
      { time: 'Thu', observed: Number(baseVal.toFixed(1)), predicted: Number((baseVal - 0.2).toFixed(1)) },
      { time: 'Fri', observed: Number((baseVal - 0.6).toFixed(1)), predicted: Number((baseVal - 0.4).toFixed(1)) },
      { time: 'Sat', observed: Number((baseVal - 1.2).toFixed(1)), predicted: Number((baseVal - 1.0).toFixed(1)) },
      { time: 'Sun', observed: Number((baseVal - 1.9).toFixed(1)), predicted: Number((baseVal - 1.6).toFixed(1)) }
    ];
  };

  const chartData = getDynamicChartData();

  return (
    <div className="river-level-view">
      {/* Page Header */}
      <div className="river-header">
        <div className="title-group">
          <div className="title-row" style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
            <h1 className="river-title">{stationDisplayName} Level</h1>
            <span className="live-telemetry-badge">
              <span className="pulse-dot-green"></span>
              {stationDisplayName} Telemetry Active
            </span>
          </div>
          <p className="river-subtitle">Last updated: {lastUpdated}</p>
        </div>

        <div className="header-actions" style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {/* Interchangeable Station Dropdown Selector */}
          <div className="station-selector-wrapper" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <label htmlFor="station-select" style={{ fontSize: '0.85rem', fontWeight: '600', color: 'var(--text-muted, #64748b)' }}>
              Station:
            </label>
            <select
              id="station-select"
              value={selectedStationId || ''}
              onChange={handleStationChange}
              style={{
                padding: '6px 12px',
                borderRadius: '8px',
                border: '1px solid #cbd5e1',
                backgroundColor: '#ffffff',
                fontSize: '0.875rem',
                fontWeight: '600',
                color: '#1e293b',
                cursor: 'pointer',
                outline: 'none',
                boxShadow: '0 1px 2px rgba(0, 0, 0, 0.05)'
              }}
            >
              {stationsList.length === 0 ? (
                <option value="">Sto. Niño Station</option>
              ) : (
                stationsList.map((st) => (
                  <option key={st.id} value={st.id}>
                    {st.station_name} ({st.level}m)
                  </option>
                ))
              )}
            </select>
          </div>

          <button
            className="refresh-btn"
            onClick={handleRefresh}
            disabled={isRefreshing}
          >
            <RefreshCw size={14} className={isRefreshing ? 'spin-icon' : ''} />
            <span>Refresh Telemetry</span>
          </button>
        </div>
      </div>

      {/* Top 3 Cards Grid */}
      <div className="river-metrics-grid">

        {/* Card 1: Current Level */}
        <div className="river-card current-level-card">
          <div className="card-top-label">
            <Waves size={16} className="text-brand" />
            <span>CURRENT WATER LEVEL</span>
          </div>

          <div className="level-hero-group">
            <div className="hero-number-wrapper">
              <span className="hero-number">{Number(currentLevel).toFixed(2)}</span>
              <span className="hero-unit">meters</span>
            </div>

            <div className="trend-chip rising">
              <ArrowUpRight size={16} />
              <span>{stationDisplayName}</span>
            </div>
          </div>

          <div 
            className="level-status-pill"
            style={{ backgroundColor: `${currentAlert.color}15`, color: currentAlert.color, borderColor: `${currentAlert.color}40`, fontWeight: '700' }}
          >
            <AlertTriangle size={14} />
            <span>{currentAlert.label.toUpperCase()}</span>
          </div>
        </div>

        {/* Card 2: Threshold Gauge Breakdown */}
        <div className="river-card thresholds-card">
          <div className="card-top-label">
            <ShieldAlert size={16} className="text-brand" />
            <span>{stationDisplayName.toUpperCase()} ALERT THRESHOLDS</span>
          </div>

          <div className="thresholds-progress-stack">
            {/* Level 1 */}
            <div className={`threshold-bar-item level-1 ${currentAlert.status === '1st Alarm' ? 'active' : ''}`}>
              <div className="threshold-info">
                <span className="thresh-name">
                  Alert Level 1 (Alarm)
                  {currentAlert.status === '1st Alarm' && <span className="active-tag">CURRENT</span>}
                </span>
                <span className="thresh-val">{currentStationThresholds.ALARM_1.toFixed(2)} meters</span>
              </div>
              <div className="thresh-track">
                <div className="thresh-fill fill-level-1" style={{ width: currentLevel >= currentStationThresholds.ALARM_1 ? '100%' : `${Math.max(0, (currentLevel / currentStationThresholds.ALARM_1) * 100)}%` }}></div>
              </div>
            </div>

            {/* Level 2 */}
            <div className={`threshold-bar-item level-2 ${currentAlert.status === '2nd Alarm' ? 'active' : ''}`}>
              <div className="threshold-info">
                <span className="thresh-name">
                  Alert Level 2 (Prepare)
                  {currentAlert.status === '2nd Alarm' && <span className="active-tag">CURRENT</span>}
                </span>
                <span className="thresh-val">{currentStationThresholds.ALARM_2.toFixed(2)} meters</span>
              </div>
              <div className="thresh-track">
                <div className="thresh-fill fill-level-2" style={{ width: currentLevel >= currentStationThresholds.ALARM_2 ? '100%' : `${Math.max(0, ((currentLevel - currentStationThresholds.ALARM_1) / (currentStationThresholds.ALARM_2 - currentStationThresholds.ALARM_1)) * 100)}%` }}></div>
              </div>
            </div>

            {/* Level 3 */}
            <div className={`threshold-bar-item level-3 ${currentAlert.status === '3rd Alarm' ? 'active' : ''}`}>
              <div className="threshold-info">
                <span className="thresh-name">
                  Alert Level 3 (Evacuate)
                  {currentAlert.status === '3rd Alarm' && <span className="active-tag">CURRENT</span>}
                </span>
                <span className="thresh-val">{currentStationThresholds.ALARM_3.toFixed(2)} meters</span>
              </div>
              <div className="thresh-track">
                <div className="thresh-fill fill-level-3" style={{ width: currentLevel >= currentStationThresholds.ALARM_3 ? '100%' : `${Math.max(0, ((currentLevel - currentStationThresholds.ALARM_2) / (currentStationThresholds.ALARM_3 - currentStationThresholds.ALARM_2)) * 100)}%` }}></div>
              </div>
            </div>
          </div>
        </div>

        {/* Card 3: Action Center */}
        <div className="river-card actions-card">
          <div className="card-top-label">
            <Zap size={16} className="text-brand" />
            <span>DISPATCH & ACTIONS</span>
          </div>

          <div className="action-buttons-stack">
            <button className="action-tile advisory" onClick={() => onActionClick('advisory', { stationName: stationDisplayName, level: currentLevel, alertStatus: currentAlert.status, alertLabel: currentAlert.label })}>
              <div className="tile-icon-box blue">
                <Megaphone size={16} />
              </div>
              <div className="tile-text">
                <span className="tile-title">Generate Advisory</span>
                <span className="tile-sub">Draft public flood warning</span>
              </div>
              <ChevronRight size={16} className="tile-arrow" />
            </button>

            <button className="action-tile notify" onClick={() => onActionClick('notify')}>
              <div className="tile-icon-box amber">
                <Bell size={16} />
              </div>
              <div className="tile-text">
                <span className="tile-title">Notify Residents</span>
                <span className="tile-sub">Send SMS & push broadcast</span>
              </div>
              <ChevronRight size={16} className="tile-arrow" />
            </button>

            <button className="action-tile predict" onClick={() => onActionClick('predict')}>
              <div className="tile-icon-box teal">
                <Compass size={16} />
              </div>
              <div className="tile-text">
                <span className="tile-title">Run Inundation Sim</span>
                <span className="tile-sub">Model affected barangays</span>
              </div>
              <ChevronRight size={16} className="tile-arrow" />
            </button>
          </div>
        </div>

      </div>

      {/* Main Hydrodynamic Graph Card */}
      <div className="river-card chart-main-card">
        <div className="chart-header-row">
          <div className="chart-title-group">
            <Activity size={18} className="text-brand" />
            <div>
              <h2 className="chart-heading">Hydrodynamic Telemetry & 12-Hour Forecast</h2>
              <span className="chart-subheading">{stationDisplayName} • Real-time stream gauge data</span>
            </div>
          </div>

          <div className="chart-controls">
            <div className="pill-selector">
              <button
                className={`pill-btn ${timeRange === '24h' ? 'active' : ''}`}
                onClick={() => setTimeRange('24h')}
              >
                24-Hour View
              </button>
              <button
                className={`pill-btn ${timeRange === '7d' ? 'active' : ''}`}
                onClick={() => setTimeRange('7d')}
              >
                7-Day Trend
              </button>
            </div>
          </div>
        </div>

        {/* Chart Canvas */}
        <div className="chart-canvas-container">
          <ResponsiveContainer width="100%" height={380}>
            <AreaChart
              data={chartData}
              margin={{ top: 20, right: 65, left: 0, bottom: 10 }}
            >
              <defs>
                {/* Observed Water Gradient */}
                <linearGradient id="gradientObserved" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#0284c7" stopOpacity={0.4} />
                  <stop offset="95%" stopColor="#0284c7" stopOpacity={0.02} />
                </linearGradient>

                {/* Predicted Water Gradient */}
                <linearGradient id="gradientPredicted" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#f97316" stopOpacity={0.25} />
                  <stop offset="95%" stopColor="#f97316" stopOpacity={0.01} />
                </linearGradient>
              </defs>

              <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#e2e8f0" />

              <XAxis
                dataKey="time"
                tickLine={false}
                axisLine={{ stroke: '#cbd5e1' }}
                tick={{ fill: '#64748b', fontSize: 12, fontWeight: 600 }}
                dy={8}
              />

              <YAxis
                domain={[
                  stationDisplayName.toLowerCase().includes('rodriguez')
                    ? 25
                    : stationDisplayName.toLowerCase().includes('nangka')
                    ? 14
                    : (stationDisplayName.toLowerCase().includes('san jose') || stationDisplayName.toLowerCase().includes('montalban'))
                    ? 18
                    : 10,
                  stationDisplayName.toLowerCase().includes('rodriguez')
                    ? 35
                    : (stationDisplayName.toLowerCase().includes('san jose') || stationDisplayName.toLowerCase().includes('montalban'))
                    ? 28
                    : 24
                ]}
                ticks={
                  stationDisplayName.toLowerCase().includes('rodriguez')
                    ? [25, 27, 28.8, 29.8, 30.7, 33, 35]
                    : stationDisplayName.toLowerCase().includes('nangka')
                    ? [14, 15, 16.5, 17.1, 17.7, 20, 22, 24]
                    : (stationDisplayName.toLowerCase().includes('san jose') || stationDisplayName.toLowerCase().includes('montalban'))
                    ? [18, 20, 22.4, 23.0, 23.6, 26, 28]
                    : [10, 12, 14, 15, 16, 18, 20, 22, 24]
                }
                tickFormatter={(val) => `${val}m`}
                tickLine={false}
                axisLine={false}
                tick={{ fill: '#64748b', fontSize: 11, fontWeight: 600 }}
                dx={-6}
              />

              <Tooltip content={<CustomTooltip />} />

              {/* Critical Danger Reference Areas */}
              <ReferenceArea
                y1={currentStationThresholds.ALARM_3}
                y2={
                  stationDisplayName.toLowerCase().includes('rodriguez')
                    ? 35
                    : (stationDisplayName.toLowerCase().includes('san jose') || stationDisplayName.toLowerCase().includes('montalban'))
                    ? 28
                    : 24
                }
                fill="#ef4444"
                fillOpacity={0.04}
              />

              {/* Threshold Lines */}
              <ReferenceLine
                y={currentStationThresholds.ALARM_1}
                stroke="#ca8a04"
                strokeDasharray="6 4"
                strokeWidth={1.5}
                label={{ value: `ALERT 1 (${currentStationThresholds.ALARM_1.toFixed(1)}m)`, position: 'right', fill: '#ca8a04', fontSize: 10, fontWeight: '800' }}
              />
              <ReferenceLine
                y={currentStationThresholds.ALARM_2}
                stroke="#ea580c"
                strokeDasharray="6 4"
                strokeWidth={2}
                label={{ value: `ALARM 2 (${currentStationThresholds.ALARM_2.toFixed(1)}m)`, position: 'right', fill: '#ea580c', fontSize: 10, fontWeight: '800' }}
              />
              <ReferenceLine
                y={currentStationThresholds.ALARM_3}
                stroke="#dc2626"
                strokeDasharray="6 4"
                strokeWidth={2}
                label={{ value: `CRITICAL (${currentStationThresholds.ALARM_3.toFixed(1)}m)`, position: 'right', fill: '#dc2626', fontSize: 10, fontWeight: '800' }}
              />

              {/* Observed Fill & Line */}
              <Area
                type="monotone"
                dataKey="observed"
                stroke="#0284c7"
                strokeWidth={3}
                fillOpacity={1}
                fill="url(#gradientObserved)"
                dot={{ r: 5, fill: '#0284c7', stroke: '#ffffff', strokeWidth: 2 }}
                activeDot={{ r: 7, fill: '#0284c7', stroke: '#ffffff', strokeWidth: 3 }}
                connectNulls={false}
              />

              {/* Predicted Forecast Fill & Line */}
              <Area
                type="monotone"
                dataKey="predicted"
                stroke="#f97316"
                strokeWidth={2.5}
                strokeDasharray="6 6"
                fillOpacity={1}
                fill="url(#gradientPredicted)"
                dot={{ r: 4, fill: '#f97316', stroke: '#ffffff', strokeWidth: 2 }}
                activeDot={{ r: 7, fill: '#f97316', stroke: '#ffffff', strokeWidth: 3 }}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        {/* Modern Interactive Chart Footer Legend */}
        <div className="chart-footer-bar">
          <div className="chart-legend-items">
            <div className="legend-chip">
              <span className="chip-indicator solid-blue"></span>
              <span className="chip-text">Observed Water Level (Gauge)</span>
            </div>
            <div className="legend-chip">
              <span className="chip-indicator dashed-orange"></span>
              <span className="chip-text">AI Hydrodynamic Forecast</span>
            </div>
          </div>

          <div className="chart-info-note">
            <Info size={13} />
            <span>Data synchronized with Marikina City Disaster Risk Reduction & Management Office (MCDRRMO)</span>
          </div>
        </div>

      </div>
    </div>
  );
}
