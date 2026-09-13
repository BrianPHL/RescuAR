import React from 'react';
import { 
  ResponsiveContainer, 
  LineChart, 
  Line, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  Legend, 
  BarChart, 
  Bar, 
  PieChart, 
  Pie, 
  Cell 
} from 'recharts';
import { 
  TrendingUp, 
  Users, 
  Send, 
  ShieldAlert, 
  ArrowUpRight, 
  Clock, 
  MapPin, 
  Smartphone 
} from 'lucide-react';

// Mock Data
const HYDROLOGY_DATA = [
  { time: '10:00 PM', level: 13.5 },
  { time: '11:00 PM', level: 13.8 },
  { time: '12:00 AM', level: 14.1 },
  { time: '01:00 AM', level: 14.2 },
  { time: '02:00 AM', level: 14.5 },
  { time: '03:00 AM', level: 14.8 },
  { time: '04:00 AM', level: 15.1 },
  { time: '05:00 AM', level: 15.3 },
  { time: '06:00 AM', level: 15.6 },
  { time: '07:00 AM', level: 15.8 },
  { time: '08:00 AM', level: 16.0 },
  { time: '08:42 AM', level: 16.2 }
];

const OCCUPANCY_DATA = [
  { name: 'Malanday ES', Occupancy: 320, Capacity: 500 },
  { name: 'Nangka HS', Occupancy: 215, Capacity: 300 },
  { name: 'Sports Center', Occupancy: 1000, Capacity: 1000 }
];

const BROADCAST_DATA = [
  { name: 'Delivered', value: 1240, color: '#16a34a' },
  { name: 'Pending', value: 15, color: '#eab308' },
  { name: 'Failed', value: 5, color: '#dc2626' }
];

export default function Analytics() {
  const totalCapacity = OCCUPANCY_DATA.reduce((sum, item) => sum + item.Capacity, 0);
  const totalOccupancy = OCCUPANCY_DATA.reduce((sum, item) => sum + item.Occupancy, 0);
  const overallOccupancyPercent = Math.round((totalOccupancy / totalCapacity) * 100);

  return (
    <div className="main-view">
      {/* View Header */}
      <div className="view-header">
        <div className="view-title-container">
          <h1>Analytics Dashboard</h1>
          <span className="view-subtitle">Detailed hydrology telemetry, evacuation distribution, and communication reach analysis</span>
        </div>
      </div>

      {/* Top Cards Row */}
      <div className="dashboard-grid">
        {/* Metric 1 */}
        <div className="dashboard-card">
          <div className="dashboard-card-icon" style={{ backgroundColor: 'rgba(234, 88, 12, 0.1)', color: '#ea580c' }}>
            <TrendingUp size={24} />
          </div>
          <div className="dashboard-card-info">
            <span className="dashboard-card-num">16.2m</span>
            <span className="dashboard-card-lbl" style={{ fontWeight: '600', color: '#1f2937', marginBottom: '2px' }}>Current Hydrology Level</span>
            <span className="dashboard-card-lbl" style={{ color: '#ea580c', fontWeight: '700' }}>Alert Level 2 (Rising)</span>
          </div>
        </div>

        {/* Metric 2 */}
        <div className="dashboard-card">
          <div className="dashboard-card-icon" style={{ backgroundColor: 'rgba(2, 132, 199, 0.1)', color: '#0284c7' }}>
            <Users size={24} />
          </div>
          <div className="dashboard-card-info">
            <span className="dashboard-card-num">{overallOccupancyPercent}%</span>
            <span className="dashboard-card-lbl" style={{ fontWeight: '600', color: '#1f2937', marginBottom: '2px' }}>Evacuation Occupancy</span>
            <span className="dashboard-card-lbl">{totalOccupancy} of {totalCapacity} beds filled</span>
          </div>
        </div>

        {/* Metric 3 */}
        <div className="dashboard-card">
          <div className="dashboard-card-icon" style={{ backgroundColor: 'rgba(22, 163, 74, 0.1)', color: '#16a34a' }}>
            <Send size={24} />
          </div>
          <div className="dashboard-card-info">
            <span className="dashboard-card-num">98.4%</span>
            <span className="dashboard-card-lbl" style={{ fontWeight: '600', color: '#1f2937', marginBottom: '2px' }}>SMS Transmission Rate</span>
            <span className="dashboard-card-lbl">1,240 delivered broadcasts</span>
          </div>
        </div>
      </div>

      {/* Grid: Charts Section */}
      <div className="stations-split-layout" style={{ gridTemplateColumns: '1.8fr 1fr' }}>
        
        {/* Left Side: Hydrology Trend Line Chart */}
        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Clock size={18} style={{ color: 'var(--color-brand)' }} />
            <span>Marikina River Level Trend (Last 12 Hours)</span>
          </h2>
          <div className="chart-container-wrapper" style={{ height: '320px', width: '100%' }}>
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={HYDROLOGY_DATA} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                <XAxis dataKey="time" stroke="#94a3b8" fontSize={11} />
                <YAxis domain={[12, 20]} stroke="#94a3b8" fontSize={11} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#1e293b', border: 'none', borderRadius: '8px', color: 'white' }}
                  labelStyle={{ fontWeight: '700' }}
                />
                <Legend verticalAlign="top" height={36} />
                <Line 
                  name="Water level (m)" 
                  type="monotone" 
                  dataKey="level" 
                  stroke="#ea580c" 
                  strokeWidth={3} 
                  activeDot={{ r: 6 }} 
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <div style={{ display: 'flex', gap: '16px', justifyContent: 'center', fontSize: '11px', flexWrap: 'wrap', borderTop: '1px solid var(--color-border-light)', paddingTop: '12px' }}>
            <span style={{ color: '#ca8a04', fontWeight: '700' }}>⚠️ Level 1 threshold: 15.0m</span>
            <span style={{ color: '#ea580c', fontWeight: '700' }}>⚠️ Level 2 threshold: 16.0m</span>
            <span style={{ color: '#dc2626', fontWeight: '700' }}>🚨 Level 3 evacuation: 18.0m</span>
          </div>
        </div>

        {/* Right Side: SMS Broadcast Status Pie Chart */}
        <div className="stations-card">
          <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Smartphone size={18} style={{ color: 'var(--color-brand)' }} />
            <span>SMS Transmission Summary</span>
          </h2>
          <div style={{ display: 'flex', justifyContent: 'center', height: '220px', alignItems: 'center' }}>
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={BROADCAST_DATA}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={80}
                  paddingAngle={5}
                  dataKey="value"
                >
                  {BROADCAST_DATA.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', borderTop: '1px solid var(--color-border-light)', paddingTop: '12px' }}>
            {BROADCAST_DATA.map((item, idx) => (
              <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <div style={{ width: '10px', height: '10px', borderRadius: '50%', backgroundColor: item.color }}></div>
                  <span style={{ color: 'var(--text-muted)' }}>{item.name}</span>
                </div>
                <span style={{ fontWeight: '700', color: 'var(--text-main)' }}>{item.value} SMS</span>
              </div>
            ))}
          </div>
        </div>

      </div>

      {/* Row 2: Evacuation Center Occupancy Chart */}
      <div className="stations-card">
        <h2 className="stations-card-title" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <MapPin size={18} style={{ color: 'var(--color-brand)' }} />
          <span>Evacuation Center Capacity Allocation</span>
        </h2>
        <div className="chart-container-wrapper" style={{ height: '300px' }}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={OCCUPANCY_DATA} margin={{ top: 20, right: 10, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
              <XAxis dataKey="name" stroke="#94a3b8" fontSize={11} />
              <YAxis stroke="#94a3b8" fontSize={11} />
              <Tooltip />
              <Legend verticalAlign="top" height={36} />
              <Bar dataKey="Occupancy" fill="#3b82f6" radius={[4, 4, 0, 0]} />
              <Bar dataKey="Capacity" fill="#e2e8f0" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
}
