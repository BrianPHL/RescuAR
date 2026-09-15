import React, { useState, useEffect, useRef } from 'react';
import { 
  RefreshCw, 
  ShieldAlert, 
  CheckCircle2, 
  AlertTriangle, 
  Droplets, 
  Home, 
  Users, 
  Building2, 
  Layers, 
  Compass, 
  Activity,
  ArrowUpRight
} from 'lucide-react';
import 'leaflet/dist/leaflet.css';
import L from 'leaflet';

// Fix Leaflet default marker icons
delete L.Icon.Default.prototype._getIconUrl;
L.Icon.Default.mergeOptions({
  iconRetinaUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon-2x.png',
  iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon.png',
  shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-shadow.png',
});

const TILE_SERVERS = {
  osm: {
    url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  },
  voyager: {
    url: 'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/">CARTO</a>'
  }
};

export default function InundationPrediction() {
  const [riverDepth, setRiverDepth] = useState(0);
  const [lastUpdated, setLastUpdated] = useState('June 18, 2026 • 08:42 AM');
  const [isSpinning, setIsSpinning] = useState(false);
  const [mapTileStyle, setMapTileStyle] = useState('osm');

  const mapContainerRef = useRef(null);
  const leafletMapRef = useRef(null);
  const tileLayerRef = useRef(null);
  const polygonRef = useRef(null);
  const outerPolygonRef = useRef(null);

  // Initialize Map
  useEffect(() => {
    if (!mapContainerRef.current) return;
    if (leafletMapRef.current) return;

    // Centered around Marikina City (Concepcion / Tumana river curve)
    const map = L.map(mapContainerRef.current, {
      center: [14.6540, 121.1010],
      zoom: 15,
      zoomControl: false, // We will add zoom control on top right
    });

    // Custom zoom control placement
    L.control.zoom({ position: 'topright' }).addTo(map);

    const tileLayer = L.tileLayer(TILE_SERVERS[mapTileStyle].url, {
      maxZoom: 19,
      attribution: TILE_SERVERS[mapTileStyle].attribution
    }).addTo(map);

    tileLayerRef.current = tileLayer;

    // 1. Hospital Marker
    const hospitalIcon = L.divIcon({
      className: 'custom-hospital-pin-wrapper',
      html: `
        <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; cursor: pointer;">
          <div style="background: linear-gradient(135deg, #ef4444, #dc2626); width: 26px; height: 26px; border-radius: 50%; border: 2px solid #ffffff; display: flex; align-items: center; justify-content: center; box-shadow: 0 4px 10px rgba(220,38,38,0.4); animation: pulse-ring 2s infinite;">
            <span style="color: white; font-size: 15px; font-weight: 900; line-height: 1;">+</span>
          </div>
          <div style="background: rgba(255,255,255,0.92); backdrop-filter: blur(4px); padding: 2px 8px; border-radius: 12px; border: 1px solid #fca5a5; margin-top: 3px; box-shadow: 0 2px 5px rgba(0,0,0,0.15);">
            <span style="font-size: 10px; font-weight: 800; color: #991b1b; white-space: nowrap;">St. Vincent Hospital</span>
          </div>
        </div>
      `,
      iconSize: [140, 48],
      iconAnchor: [70, 13]
    });
    L.marker([14.6565, 121.1085], { icon: hospitalIcon }).addTo(map);

    // 2. Monitoring Sensor Marker along Marikina River
    const sensorIcon = L.divIcon({
      className: 'custom-sensor-pin-wrapper',
      html: `
        <div style="display: flex; flex-direction: column; align-items: center; cursor: pointer;">
          <div style="background: #0284c7; width: 22px; height: 22px; border-radius: 50%; border: 2px solid #ffffff; display: flex; align-items: center; justify-content: center; box-shadow: 0 2px 6px rgba(2,132,199,0.5);">
            <div style="width: 8px; height: 8px; background: white; border-radius: 50%;"></div>
          </div>
          <div style="background: #0f172a; color: white; padding: 2px 6px; border-radius: 8px; font-size: 9px; font-weight: 700; margin-top: 2px;">
            Sto. Niño Gauge
          </div>
        </div>
      `,
      iconSize: [110, 42],
      iconAnchor: [55, 11]
    });
    L.marker([14.6460, 121.0910], { icon: sensorIcon }).addTo(map);

    leafletMapRef.current = map;

    return () => {
      if (leafletMapRef.current) {
        leafletMapRef.current.remove();
        leafletMapRef.current = null;
      }
    };
  }, []);

  // Handle Tile Server Change
  useEffect(() => {
    if (tileLayerRef.current && leafletMapRef.current) {
      tileLayerRef.current.setUrl(TILE_SERVERS[mapTileStyle].url);
    }
  }, [mapTileStyle]);

  // Update Inundation Polygons
  useEffect(() => {
    const map = leafletMapRef.current;
    if (!map) return;

    if (polygonRef.current) {
      map.removeLayer(polygonRef.current);
      polygonRef.current = null;
    }
    if (outerPolygonRef.current) {
      map.removeLayer(outerPolygonRef.current);
      outerPolygonRef.current = null;
    }

    if (riverDepth > 0) {
      // Core inundation zone (Primary river basin & Tumana/Nangka)
      const coreCoords = [
        [14.6720, 121.0910],
        [14.6670, 121.0975],
        [14.6600, 121.1000],
        [14.6520, 121.0980],
        [14.6430, 121.0930],
        [14.6360, 121.0880],
        [14.6390, 121.0820],
        [14.6490, 121.0850],
        [14.6580, 121.0880],
        [14.6680, 121.0890]
      ];

      // Expanded flood basin contour for high water levels (>14m)
      const expandedCoords = [
        [14.6750, 121.0880],
        [14.6700, 121.0990],
        [14.6620, 121.1030],
        [14.6500, 121.1005],
        [14.6410, 121.0950],
        [14.6330, 121.0900],
        [14.6350, 121.0790],
        [14.6460, 121.0820],
        [14.6590, 121.0850],
        [14.6710, 121.0860]
      ];

      // Color scheme based on severity level
      let fillColor = '#0ea5e9'; // Blue (Moderate)
      let strokeColor = '#0284c7';
      if (riverDepth >= 18) {
        fillColor = '#ef4444'; // Red (Severe)
        strokeColor = '#b91c1c';
      } else if (riverDepth >= 15) {
        fillColor = '#f97316'; // Orange (Warning)
        strokeColor = '#c2410c';
      }

      const opacity = Math.min(0.65, 0.22 + (riverDepth / 30) * 0.4);

      if (riverDepth >= 14) {
        const outerPoly = L.polygon(expandedCoords, {
          color: strokeColor,
          weight: 1,
          dashArray: '4, 6',
          fillColor: fillColor,
          fillOpacity: opacity * 0.45,
        }).addTo(map);
        outerPolygonRef.current = outerPoly;
      }

      const polygon = L.polygon(coreCoords, {
        color: strokeColor,
        weight: 2,
        fillColor: fillColor,
        fillOpacity: opacity,
      }).addTo(map);

      polygonRef.current = polygon;
    }
  }, [riverDepth]);

  const handleRefresh = () => {
    setIsSpinning(true);
    setTimeout(() => {
      const now = new Date();
      const options = { month: 'long', day: 'numeric', year: 'numeric' };
      const dateStr = now.toLocaleDateString('en-US', options);
      const timeStr = now.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
      setLastUpdated(`${dateStr} • ${timeStr}`);
      setIsSpinning(false);
    }, 600);
  };

  const getImpactSummary = () => {
    if (riverDepth <= 0) return null;

    let barangays = [];
    let centers = [];
    
    if (riverDepth < 10) {
      barangays = ['Tumana'];
      centers = ['Concepcion Elementary School', 'Concepcion Integrated School ES'];
    } else if (riverDepth < 15) {
      barangays = ['Tumana', 'Nangka'];
      centers = ['Concepcion Elementary School', 'Nangka Elementary School', 'Nangka Gym'];
    } else if (riverDepth < 20) {
      barangays = ['Tumana', 'Nangka', 'Malanday'];
      centers = ['Concepcion Elementary School', 'Nangka Elementary School', 'Malanday Elementary School', 'Bulelak Gym'];
    } else {
      barangays = ['Tumana', 'Nangka', 'Malanday', 'Tañong', 'Jesus dela Peña'];
      centers = ['Concepcion Elementary School', 'Nangka Elementary School', 'Malanday Elementary School', 'Tañong High School', 'Jesus Dela Peña NHS', 'Bulelak Gym'];
    }

    let households = 4213;
    let population = 17842;

    if (riverDepth !== 16) {
      households = Math.round(riverDepth * 263.3125);
      population = Math.round(riverDepth * 1115.125);
    }

    return {
      affectedBarangays: barangays,
      householdsAffected: households.toLocaleString(),
      populationAffected: population.toLocaleString(),
      evacuationCenters: centers
    };
  };

  const impactData = getImpactSummary();
  const sliderPercentage = (riverDepth / 30) * 100;

  const getAlertLevelStatus = () => {
    if (riverDepth <= 0) return { label: 'NORMAL LEVEL', color: '#16a34a', bg: '#f0fdf4', border: '#bbf7d0', icon: CheckCircle2 };
    if (riverDepth < 15) return { label: 'ALARM LEVEL 1 - MONITORING', color: '#ca8a04', bg: '#fefce8', border: '#fef08a', icon: AlertTriangle };
    if (riverDepth < 18) return { label: 'ALARM LEVEL 2 - PREPARATION', color: '#ea580c', bg: '#fff7ed', border: '#ffedd5', icon: AlertTriangle };
    return { label: 'ALARM LEVEL 3 - MANDATORY EVACUATION', color: '#dc2626', bg: '#fef2f2', border: '#fecaca', icon: ShieldAlert };
  };

  const alertStatus = getAlertLevelStatus();
  const StatusIcon = alertStatus.icon;

  return (
    <div className="inundation-container">
      {/* Top Header Banner */}
      <div className="inundation-header">
        <div className="title-group">
          <div className="title-row">
            <h1 className="inundation-title">Inundation Prediction</h1>
            <span className="live-badge">
              <span className="pulse-dot"></span>
              Hydrodynamic Sim Engine v2.4
            </span>
          </div>
          <p className="inundation-subtitle">
            <span>Last updated: {lastUpdated}</span>
          </p>
        </div>

        <div className="header-actions">
          <button 
            className="refresh-btn"
            onClick={handleRefresh}
          >
            <RefreshCw size={15} className={isSpinning ? 'spin-icon' : ''} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* Main Responsive Grid */}
      <div className="inundation-grid">
        
        {/* Left Panel: OpenStreetMap Hydrodynamic Viewer */}
        <div className="inundation-card map-card">
          <div className="card-header-bar">
            <div className="card-title-container">
              <Compass size={18} className="text-brand" />
              <h2 className="card-heading">Flood Impact Map</h2>
            </div>
            
            {/* Map Theme Control */}
            <div className="map-style-toggle">
              <button 
                className={`tile-toggle-btn ${mapTileStyle === 'osm' ? 'active' : ''}`}
                onClick={() => setMapTileStyle('osm')}
              >
                Standard OSM
              </button>
              <button 
                className={`tile-toggle-btn ${mapTileStyle === 'voyager' ? 'active' : ''}`}
                onClick={() => setMapTileStyle('voyager')}
              >
                Carto Voyager
              </button>
            </div>
          </div>

          <div className="map-wrapper">
            <div 
              ref={mapContainerRef} 
              className="osm-map-container"
            />

            {/* Floating Glassmorphism Legend Overlay */}
            <div className="map-legend-glass">
              <div className="legend-header">
                <Layers size={13} />
                <span>Zone Legend</span>
              </div>
              <div className="legend-items">
                <div className="legend-item">
                  <span className="legend-swatch safe-swatch"></span>
                  <span>Dry Sector</span>
                </div>
                <div className="legend-item">
                  <span className={`legend-swatch ${riverDepth >= 18 ? 'severe-swatch' : riverDepth >= 15 ? 'warning-swatch' : 'flood-swatch'}`}></span>
                  <span>Inundated Sector</span>
                </div>
                <div className="legend-item">
                  <span className="legend-swatch hospital-swatch"></span>
                  <span>Critical Hospital</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Right Panel: Interactive Slider & Dynamic Impact Card */}
        <div className="right-panel-stack">
          
          {/* Prediction Input & Presets */}
          <div className="inundation-card input-card">
            <div className="card-header-bar">
              <div className="card-title-container">
                <Droplets size={18} className="text-brand" />
                <h2 className="card-heading">Prediction Input</h2>
              </div>
            </div>

            <label className="slider-label">Marikina River Depth (in meters)</label>
            
            {/* Main Interactive Slider */}
            <div className="slider-controls-row">
              <input 
                type="range"
                min="0"
                max="30"
                step="1"
                value={riverDepth}
                onChange={(e) => setRiverDepth(Number(e.target.value))}
                className="depth-range-input"
                style={{ '--slider-pct': `${sliderPercentage}%` }}
              />
              <div className="depth-display-box">
                {riverDepth}
              </div>
            </div>

            {/* Quick Level Preset Buttons */}
            <div className="preset-buttons-row">
              <span className="preset-title">Quick Presets:</span>
              <button 
                className={`preset-chip ${riverDepth === 0 ? 'active' : ''}`}
                onClick={() => setRiverDepth(0)}
              >
                0m Normal
              </button>
              <button 
                className={`preset-chip ${riverDepth === 15 ? 'active' : ''}`}
                onClick={() => setRiverDepth(15)}
              >
                15m Level 1
              </button>
              <button 
                className={`preset-chip ${riverDepth === 16 ? 'active' : ''}`}
                onClick={() => setRiverDepth(16)}
              >
                16m Level 2
              </button>
              <button 
                className={`preset-chip ${riverDepth === 18 ? 'active' : ''}`}
                onClick={() => setRiverDepth(18)}
              >
                18m Level 3
              </button>
            </div>
          </div>

          {/* Impact Summary Card */}
          <div className="inundation-card summary-card">
            <div className="card-header-bar">
              <div className="card-title-container">
                <Activity size={18} className="text-brand" />
                <h2 className="card-heading">Impact Summary</h2>
              </div>
            </div>

            {riverDepth === 0 ? (
              <div className="empty-summary-container">
                <div className="empty-icon-halo">
                  <CheckCircle2 size={36} color="#16a34a" />
                </div>
                <h3 className="empty-title">No Inundation Threat</h3>
                <p>No inundation predicted currently.</p>
                <p className="empty-subtext">Adjust river depth to simulate flood impact.</p>
              </div>
            ) : (
              <div className="impact-content-wrapper">
                {/* Alert Level Chip */}
                <div 
                  className="alert-status-banner"
                  style={{
                    color: alertStatus.color,
                    backgroundColor: alertStatus.bg,
                    borderColor: alertStatus.border
                  }}
                >
                  <StatusIcon size={16} />
                  <span>{alertStatus.label}</span>
                </div>

                {/* Details List */}
                <div className="impact-details-list">
                  
                  {/* Affected Barangays */}
                  <div className="impact-item-row">
                    <div className="impact-label-group">
                      <Home size={15} className="item-icon" />
                      <span className="impact-item-label">Affected Barangays</span>
                    </div>
                    <div className="barangay-tags-container">
                      {impactData.affectedBarangays.map(brgy => (
                        <span key={brgy} className="brgy-tag">{brgy}</span>
                      ))}
                    </div>
                  </div>

                  {/* Households Affected */}
                  <div className="impact-item-row">
                    <div className="impact-label-group">
                      <Building2 size={15} className="item-icon" />
                      <span className="impact-item-label">Households Affected</span>
                    </div>
                    <span className="impact-item-value highlight">{impactData.householdsAffected}</span>
                  </div>

                  {/* Population Affected */}
                  <div className="impact-item-row">
                    <div className="impact-label-group">
                      <Users size={15} className="item-icon" />
                      <span className="impact-item-label">Population Affected</span>
                    </div>
                    <span className="impact-item-value highlight">{impactData.populationAffected}</span>
                  </div>

                  {/* Recommended Evacuation Centers */}
                  <div className="impact-item-row" style={{ alignItems: 'flex-start', flexDirection: 'column', gap: '8px' }}>
                    <div className="impact-label-group">
                      <ArrowUpRight size={15} className="item-icon" />
                      <span className="impact-item-label">Recommended Evacuation Centers</span>
                    </div>
                    <div className="barangay-tags-container" style={{ width: '100%', flexWrap: 'wrap' }}>
                      {Array.isArray(impactData.evacuationCenters) ? impactData.evacuationCenters.map(center => (
                        <span key={center} className="brgy-tag" style={{ backgroundColor: '#eff6ff', color: '#1d4ed8', border: '1px solid #bfdbfe' }}>
                          {center}
                        </span>
                      )) : (
                        <span className="impact-item-value accent">{impactData.evacuationCenters}</span>
                      )}
                    </div>
                  </div>

                </div>
              </div>
            )}
          </div>

        </div>
      </div>
    </div>
  );
}
