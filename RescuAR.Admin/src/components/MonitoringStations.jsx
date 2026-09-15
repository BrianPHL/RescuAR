import React, { useState, useEffect } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus } from '../utils/waterLevelUtils';
import {  
  RefreshCw, 
  Droplet, 
  Waves, 
  AlertTriangle, 
  Info, 
  BarChart2, 
  ArrowUpDown, 
  ArrowUp, 
  ArrowDown
} from 'lucide-react';
import { 
  ResponsiveContainer, 
  BarChart, 
  Bar, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip 
} from 'recharts';

// Default initial station data matching P.R.E.P Marikina telemetry node format
const INITIAL_STATIONS = [
  { id: 1, name: 'Rodriguez Station', level: 28.2, status: 'Normal' },
  { id: 2, name: 'San Jose Station', level: 22.3, status: 'Normal' },
  { id: 3, name: 'Batasan Station', level: 14.6, status: 'Normal' },
  { id: 4, name: 'Nangka Station', level: 22.2, status: '3rd Alarm' },
  { id: 5, name: 'Tumana Station', level: 12.0, status: 'Normal' },
  { id: 6, name: 'Sto. Niño Station', level: 12.1, status: 'Normal' }
];

export default function MonitoringStations() {
  const [stations, setStations] = useState(INITIAL_STATIONS);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState('Loading live data...');
  const [showSummary, setShowSummary] = useState(true);
  const [sortConfig, setSortConfig] = useState({ key: null, direction: null });

  // Fetch stations from Supabase table 'monitoring_stations'
  const fetchStationsFromSupabase = async () => {
    setIsRefreshing(true);
    try {
      const { data, error } = await supabase
        .from('monitoring_stations')
        .select('*')
        .order('level', { ascending: false });

      if (error) {
        console.error('Error fetching monitoring stations from Supabase:', error.message);
      } else if (data && data.length > 0) {
        // Calculate status dynamically using calculateAlertStatus
        const formattedStations = data.map((item, idx) => {
          const level = Number(item.level);
          const alertInfo = calculateAlertStatus(level, item.station_name);
          return {
            id: item.id || idx + 1,
            name: item.station_name,
            level: level,
            status: alertInfo.status, // Calculated dynamically!
            alertInfo: alertInfo
          };
        });
        setStations(formattedStations);

        // Find the maximum updated_at timestamp from all stations
        const latestTimestamp = data.reduce((latest, item) => {
          if (!item.updated_at) return latest;
          const itemTime = new Date(item.updated_at).getTime();
          return itemTime > latest ? itemTime : latest;
        }, 0);

        const dateObj = latestTimestamp > 0 ? new Date(latestTimestamp) : new Date();
        const dateStr = dateObj.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });
        const timeStr = dateObj.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
        setLastUpdated(`${dateStr} • ${timeStr}`);
      }
    } catch (err) {
      console.error('Unexpected error fetching stations:', err);
    } finally {
      setIsRefreshing(false);
    }
  };

  useEffect(() => {
    fetchStationsFromSupabase();

    // Subscribe to real-time changes on the 'monitoring_stations' table
    const channel = supabase
      .channel('monitoring-stations-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => {
        fetchStationsFromSupabase();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await fetchStationsFromSupabase();
    } finally {
      // Ensure minimum spinning animation duration so user gets immediate visual feedback
      setTimeout(() => setIsRefreshing(false), 500);
    }
  };

  // Sorting Handler
  const handleSort = (key) => {
    let direction = 'ascending';
    if (sortConfig.key === key && sortConfig.direction === 'ascending') {
      direction = 'descending';
    } else if (sortConfig.key === key && sortConfig.direction === 'descending') {
      direction = null; // Reset to default order
    }
    setSortConfig({ key, direction });
  };

  // Get Sorted Stations
  const getSortedStations = () => {
    if (!sortConfig.key || !sortConfig.direction) {
      return [...stations];
    }
    return [...stations].sort((a, b) => {
      let valA = a[sortConfig.key];
      let valB = b[sortConfig.key];

      if (typeof valA === 'string') {
        valA = valA.toLowerCase();
        valB = valB.toLowerCase();
      }

      if (valA < valB) {
        return sortConfig.direction === 'ascending' ? -1 : 1;
      }
      if (valA > valB) {
        return sortConfig.direction === 'ascending' ? 1 : -1;
      }
      return 0;
    });
  };

  const getSortIcon = (key) => {
    if (sortConfig.key !== key) {
      return <ArrowUpDown size={12} className="sort-icon-inactive" />;
    }
    if (sortConfig.direction === 'ascending') {
      return <ArrowUp size={12} className="sort-icon-active" />;
    }
    return <ArrowDown size={12} className="sort-icon-active" />;
  };

  // Sort bar chart data by level descending as shown in the screenshot
  const barChartData = [...stations].sort((a, b) => b.level - a.level);
  const sortedStations = getSortedStations();

  return (
    <div className="main-view">
      {/* View Header */}
      <div className="view-header">
        <div className="view-title-container">
          <h1>Monitoring Stations</h1>
          <span className="view-subtitle">Last updated: {lastUpdated}</span>
        </div>
        <button 
          className="btn-refresh" 
          onClick={handleRefresh}
          disabled={isRefreshing}
        >
          <RefreshCw size={14} className={isRefreshing ? 'animate-spin' : ''} style={{ animation: isRefreshing ? 'spin 1s linear infinite' : 'none' }} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Point Cards Grid */}
      {(() => {
        const getStationData = (namePart) => {
          const found = stations.find(s => s.name.toLowerCase().includes(namePart.toLowerCase()));
          const stationObj = found || { level: 0, status: 'Normal', name: namePart };
          const alertInfo = calculateAlertStatus(stationObj.level, stationObj.name);
          return { ...stationObj, alertInfo };
        };

        const renderPointCard = (title, stationData, defaultIcon) => {
          const { level, alertInfo } = stationData;
          const is3rdAlarm = alertInfo.status === '3rd Alarm' || level >= 18;
          const is2ndAlarm = alertInfo.status === '2nd Alarm';
          const is1stAlarm = alertInfo.status === '1st Alarm';

          // Card distinction styles
          let cardStyle = {};
          let iconBg = '#e8f7ed';
          let iconColor = '#10b981';
          let badgeStyle = { backgroundColor: '#dcfce7', color: '#15803d' };
          let IconComponent = defaultIcon;

          if (is3rdAlarm) {
            cardStyle = {
              backgroundColor: '#fef2f2',
              boxShadow: '0 4px 12px rgba(239, 68, 68, 0.12)',
              position: 'relative'
            };
            badgeStyle = {
              backgroundColor: '#dc2626',
              color: '#ffffff',
              fontWeight: '800',
              boxShadow: '0 2px 4px rgba(220, 38, 38, 0.3)'
            };
            iconBg = '#fee2e2';
            iconColor = '#dc2626';
            IconComponent = AlertTriangle;
          } else if (is2ndAlarm) {
            cardStyle = { backgroundColor: '#fff7ed' };
            badgeStyle = { backgroundColor: '#ea580c', color: '#ffffff', fontWeight: '700' };
            iconBg = '#ffedd5';
            iconColor = '#ea580c';
            IconComponent = AlertTriangle;
          } else if (is1stAlarm) {
            cardStyle = { backgroundColor: '#fefce8' };
            badgeStyle = { backgroundColor: '#ca8a04', color: '#ffffff', fontWeight: '700' };
            iconBg = '#fef9c3';
            iconColor = '#ca8a04';
          }

          return (
            <div className="point-card" style={cardStyle}>
              <div className="point-card-left">
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <span className="point-card-title">{title}</span>
                </div>
                <span className="point-card-value" style={{ color: is3rdAlarm ? '#b91c1c' : 'var(--text-main)' }}>
                  {level.toFixed(2)} m
                </span>
                <div className="point-card-badge-row">
                  <span className="status-badge-pill" style={badgeStyle}>
                    {is3rdAlarm ? '3rd Alarm' : alertInfo.label}
                  </span>
                  <span className="live-feed-text">
                    <span className="live-feed-dot" style={{ backgroundColor: is3rdAlarm ? '#dc2626' : '#10b981' }}></span>
                    Live Feed
                  </span>
                </div>
              </div>
              <div className="point-card-right">
                <div 
                  className="point-card-icon-wrapper" 
                  style={{ backgroundColor: iconBg, color: iconColor }}
                >
                  <IconComponent size={22} style={{ animation: is3rdAlarm ? 'pulse 1.5s infinite' : 'none' }} />
                </div>
              </div>
            </div>
          );
        };

        const tumana = getStationData('Tumana');
        const nangka = getStationData('Nangka');
        const stoNino = getStationData('Sto. Niño');

        return (
          <div className="station-points-grid">
            {renderPointCard('TUMANA MONITORING POINT', tumana, Droplet)}
            {renderPointCard('NANGKA MONITORING POINT', nangka, Waves)}
            {renderPointCard('STO. NIÑO (MAIN NODE)', stoNino, AlertTriangle)}
          </div>
        );
      })()}

      {/* Main Split Layout */}
      <div className="stations-split-layout">
        {/* Left Side: Levels Bar Chart */}
        <div className="stations-card">
          <h2 className="stations-card-title">Station Levels vs warning Thresholds</h2>
          
          <div className="chart-container-wrapper" style={{ height: '300px', marginTop: '16px' }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={barChartData}
                margin={{ top: 10, right: 10, left: -20, bottom: 5 }}
              >
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f1f5f9" />
                <XAxis 
                  dataKey="name" 
                  tickLine={false} 
                  axisLine={false} 
                  tick={{ fill: 'var(--text-muted)', fontSize: 10, fontWeight: '500' }}
                />
                <YAxis 
                  domain={[0, 30]}
                  ticks={[0, 10, 20]}
                  tickLine={false}
                  axisLine={false}
                  tick={{ fill: 'var(--text-light)', fontSize: 11 }}
                  label={{ 
                    value: 'Water Gauge Level (meters)', 
                    angle: -90, 
                    position: 'insideLeft', 
                    style: { textAnchor: 'middle', fill: 'var(--text-muted)', fontSize: 11, fontWeight: '500' },
                    offset: 0
                  }}
                />
                <Tooltip 
                  cursor={{ fill: 'rgba(241, 245, 249, 0.5)' }}
                  content={({ active, payload }) => {
                    if (active && payload && payload.length) {
                      return (
                        <div style={{
                          backgroundColor: '#ffffff',
                          border: '1px solid var(--color-border)',
                          padding: '8px 12px',
                          borderRadius: 'var(--radius-md)',
                          boxShadow: 'var(--shadow-lg)',
                          fontSize: '12px'
                        }}>
                          <p style={{ fontWeight: '700', color: 'var(--text-main)' }}>{payload[0].payload.name}</p>
                          <p style={{ color: '#3b82f6', fontWeight: '600', marginTop: '4px' }}>
                            Level: {Number(payload[0].value).toFixed(2)} m
                          </p>
                        </div>
                      );
                    }
                    return null;
                  }}
                />
                <Bar 
                  dataKey="level" 
                  fill="#3b82f6" 
                  barSize={36}
                  radius={[4, 4, 0, 0]}
                />
              </BarChart>
            </ResponsiveContainer>
          </div>

          <div className="x-axis-title-centered">
            Gaging Network Station Node Location
          </div>

          <div className="graph-type-label">
            <BarChart2 size={14} />
            <span>Graph Type: Bar Chart</span>
          </div>

          <div className="checkbox-wrapper">
            <input 
              type="checkbox" 
              id="show-summary" 
              checked={showSummary} 
              onChange={(e) => setShowSummary(e.target.checked)} 
            />
            <label htmlFor="show-summary">Show Hydrological Summary</label>
          </div>

          {showSummary && (
            <div className="hydrological-summary-box">
              <Info size={16} className="summary-icon" />
              <div>
                <div className="summary-title">Hydrological Summary</div>
                <div className="summary-text">
                  The river channels are within safe operational bounds. Water flow is running normally and there is no active threat of overflow.
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Right Side: Telemetry Node Database */}
        <div className="stations-card">
          <h2 className="stations-card-title">Telemetry Node Database</h2>
          
          <div className="table-container" style={{ marginTop: '16px' }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th onClick={() => handleSort('name')} className="sortable-header">
                    <div className="header-cell-content">
                      <span>RIVER</span>
                      {getSortIcon('name')}
                    </div>
                  </th>
                  <th onClick={() => handleSort('level')} className="sortable-header">
                    <div className="header-cell-content">
                      <span>LEVEL</span>
                      {getSortIcon('level')}
                    </div>
                  </th>
                  <th onClick={() => handleSort('status')} className="sortable-header">
                    <div className="header-cell-content">
                      <span>STATUS</span>
                      {getSortIcon('status')}
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody>
                {sortedStations.map((station) => {
                  const alertInfo = station.alertInfo || calculateAlertStatus(station.level, station.name);
                  const is3rdAlarm = alertInfo.status === '3rd Alarm';
                  return (
                    <tr 
                      key={station.id} 
                      className="table-row-hover"
                      style={is3rdAlarm ? { backgroundColor: '#fef2f2' } : {}}
                    >
                      <td style={{ fontWeight: '600', color: is3rdAlarm ? '#b91c1c' : 'var(--text-main)' }}>{station.name}</td>
                      <td style={{ fontWeight: '600', color: is3rdAlarm ? '#dc2626' : 'inherit' }}>{Number(station.level).toFixed(2)} m</td>
                      <td>
                        <span 
                          className="status-badge-pill"
                          style={{
                            backgroundColor: `${alertInfo.color}20`,
                            color: alertInfo.color,
                            border: `1px solid ${alertInfo.color}40`,
                            fontWeight: '700'
                          }}
                        >
                          {alertInfo.status}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
