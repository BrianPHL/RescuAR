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
import { supabase } from '../supabaseClient';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import { predictPrepInundation, PREP_API_URL } from '../services/prepInundationApi';

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
  const [mode, setMode] = useState('live');
  const [simulationDepth, setSimulationDepth] = useState(0);
  const [liveTelemetry, setLiveTelemetry] = useState(null);
  const [telemetryError, setTelemetryError] = useState('');
  const [telemetryLoading, setTelemetryLoading] = useState(true);
  const [nowMs, setNowMs] = useState(Date.now());
  const [isSpinning, setIsSpinning] = useState(false);
  const [mapTileStyle, setMapTileStyle] = useState('osm');
  const [prepPrediction, setPrepPrediction] = useState(null);
  const [prepApiLoading, setPrepApiLoading] = useState(true);
  const [prepApiError, setPrepApiError] = useState('');
  const [prepRefreshKey, setPrepRefreshKey] = useState(0);

  const mapContainerRef = useRef(null);
  const leafletMapRef = useRef(null);
  const tileLayerRef = useRef(null);
  const polygonRef = useRef(null);
  const outerPolygonRef = useRef(null);

  const liveLevel = Number(liveTelemetry?.level);
  const telemetryFreshness = getTelemetryFreshness(liveTelemetry?.updated_at, nowMs);
  const isLiveTelemetryActive = telemetryFreshness.isUsableAsLive && Number.isFinite(liveLevel);
  const isLiveMode = mode === 'live';
  const riverDepth = isLiveMode && isLiveTelemetryActive ? liveLevel : simulationDepth;
  const prepAffectedArea = prepPrediction?.inundation || {
    has_triggered_rule: false,
    affected_barangays: [],
    triggered_thresholds: [],
    evidence_status: 'unavailable',
  };

  const formatObservedAt = (value) => {
    if (!value) return 'No verified PAGASA observation';
    const date = new Date(value);
    if (!Number.isFinite(date.getTime())) return 'Invalid observation timestamp';
    return date.toLocaleString('en-PH', {
      month: 'long',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const fetchStoNinoTelemetry = async () => {
    setTelemetryLoading(true);
    setTelemetryError('');

    try {
      const { data, error } = await supabase
        .from('monitoring_stations')
        .select('id, station_name, level, status, updated_at')
        .order('updated_at', { ascending: false });

      if (error) throw error;

      const station = (data || []).find((row) => {
        const name = String(row?.station_name || '').toLowerCase();
        return name.includes('sto') && (name.includes('niño') || name.includes('nino'));
      });

      if (!station) {
        setLiveTelemetry(null);
        setTelemetryError('Sto. Niño PAGASA telemetry is not available in monitoring_stations.');
        return;
      }

      setLiveTelemetry(station);
      setNowMs(Date.now());
    } catch (error) {
      console.error('Unable to load Sto. Niño telemetry for inundation:', error);
      setTelemetryError('Verified PAGASA telemetry could not be loaded.');
    } finally {
      setTelemetryLoading(false);
    }
  };

  // Audits #13/#14: the existing PREP inundation model has one effective
  // river-level input. Live Mode uses only fresh verified PAGASA telemetry;
  // Simulation Mode uses only the local manual scenario value. Simulation
  // values are never written to monitoring_stations or river_level_history.
  useEffect(() => {
    fetchStoNinoTelemetry();

    const freshnessTimer = setInterval(() => setNowMs(Date.now()), 60 * 1000);
    const pollTimer = setInterval(() => {
      void fetchStoNinoTelemetry();
    }, 5 * 60 * 1000);

    const channel = supabase
      .channel('inundation-sto-nino-telemetry')
      .on(
        'postgres_changes',
        { event: '*', schema: 'public', table: 'monitoring_stations' },
        () => void fetchStoNinoTelemetry()
      )
      .subscribe();

    return () => {
      clearInterval(freshnessTimer);
      clearInterval(pollTimer);
      supabase.removeChannel(channel);
    };
  }, []);

  useEffect(() => {
    if (!telemetryLoading && mode === 'live' && !isLiveTelemetryActive) {
      setMode('simulation');
    }
  }, [telemetryLoading, mode, isLiveTelemetryActive]);

  // Audit #15 revised architecture: the PREP inundation model executes only
  // in the separately hosted R/Plumber service. RescuAR Admin sends the
  // effective Live/Simulation river level and renders the API response.
  // There is deliberately no embedded JavaScript fallback copy of the model.
  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setPrepApiLoading(true);
      setPrepApiError('');

      try {
        const result = await predictPrepInundation({
          riverLevel: riverDepth,
          mode: isLiveMode && isLiveTelemetryActive ? 'live' : 'simulation',
          inputSource: isLiveMode && isLiveTelemetryActive ? 'PAGASA' : 'MANUAL_SIMULATION',
          observedAt: isLiveMode && isLiveTelemetryActive ? liveTelemetry?.updated_at : null,
          signal: controller.signal,
        });
        setPrepPrediction(result);
      } catch (error) {
        if (error?.name === 'AbortError') return;
        console.error('PREP R inundation API request failed:', error);
        setPrepPrediction(null);
        setPrepApiError(error?.message || 'PREP R inundation API is unavailable.');
      } finally {
        if (!controller.signal.aborted) setPrepApiLoading(false);
      }
    }, 250);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [riverDepth, isLiveMode, isLiveTelemetryActive, liveTelemetry?.updated_at, prepRefreshKey]);

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

    if (prepAffectedArea.has_triggered_rule) {
      // Legacy static visualization. Audit #15 ties activation to the exact
      // PREP affected-area threshold model. Polygon geometry itself remains a
      // legacy visualization and is handled by the later spatial-model audits.
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
      const alarmLevel = Number(prepPrediction?.alarm?.level || 0);
      if (alarmLevel >= 3) {
        fillColor = '#ef4444'; // Red (Severe)
        strokeColor = '#b91c1c';
      } else if (alarmLevel >= 1) {
        fillColor = '#f97316'; // Orange (Warning)
        strokeColor = '#c2410c';
      }

      const opacity = Math.min(0.65, 0.22 + (riverDepth / 30) * 0.4);

      // The legacy PREP source does not define a second spatial boundary or
      // a 14 m expansion threshold. Do not activate the old unsupported outer
      // polygon here; Audit #17 will replace static geometry with evidence-
      // based spatial inundation data.
      void expandedCoords;

      const polygon = L.polygon(coreCoords, {
        color: strokeColor,
        weight: 2,
        fillColor: fillColor,
        fillOpacity: opacity,
      }).addTo(map);

      polygonRef.current = polygon;
    }
  }, [riverDepth, prepAffectedArea.has_triggered_rule, prepPrediction?.alarm?.level]);

  const handleRefresh = async () => {
    setIsSpinning(true);
    await fetchStoNinoTelemetry();
    setPrepRefreshKey((value) => value + 1);
    setIsSpinning(false);
  };

  const getImpactSummary = () => {
    if (!prepAffectedArea.has_triggered_rule) return null;

    // These evacuation-center suggestions remain a separate RescuAR response
    // layer and are not part of the R inundation model. Audit #25 will replace
    // them with spatial facility exposure/intersection logic.
    const alarmLevel = Number(prepPrediction?.alarm?.level || 0);
    let centers = [];
    if (alarmLevel <= 1) {
      centers = ['Concepcion Elementary School', 'Concepcion Integrated School ES'];
    } else if (alarmLevel === 2) {
      centers = ['Concepcion Elementary School', 'Nangka Elementary School', 'Nangka Gym', 'Malanday Elementary School'];
    } else {
      centers = ['Concepcion Elementary School', 'Nangka Elementary School', 'Malanday Elementary School', 'Tañong High School', 'Jesus Dela Peña NHS', 'Bulelak Gym'];
    }

    const population = Number(prepPrediction?.exposure?.total_population_affected);

    return {
      affectedBarangays: prepAffectedArea.affected_barangays || [],
      householdsAffected: 'Pending Audit #24',
      populationAffected: Number.isFinite(population) ? population.toLocaleString('en-PH') : 'Unavailable',
      evacuationCenters: centers,
      affectedAreaModel: prepPrediction?.model || null,
      triggeredThresholds: prepAffectedArea.triggered_thresholds || [],
    };
  };

  const impactData = getImpactSummary();
  const sliderPercentage = (riverDepth / 30) * 100;

  // Alarm classification is returned by the same R service. The JavaScript
  // client only maps the returned code to presentation colors/icons.
  const getAlertLevelPresentation = () => {
    const code = prepPrediction?.alarm?.code;
    const label = prepPrediction?.alarm?.label || 'PREP API RESULT UNAVAILABLE';

    if (code === 'ALARM_1') return { label, color: '#ca8a04', bg: '#fefce8', border: '#fef08a', icon: AlertTriangle };
    if (code === 'ALARM_2') return { label, color: '#ea580c', bg: '#fff7ed', border: '#ffedd5', icon: AlertTriangle };
    if (code === 'ALARM_3') return { label, color: '#dc2626', bg: '#fef2f2', border: '#fecaca', icon: ShieldAlert };
    return { label, color: '#16a34a', bg: '#f0fdf4', border: '#bbf7d0', icon: CheckCircle2 };
  };

  const alertStatus = getAlertLevelPresentation();
  const StatusIcon = alertStatus.icon;

  return (
    <div className="inundation-container">
      {/* Top Header Banner */}
      <div className="inundation-header">
        <div className="title-group">
          <div className="title-row">
            <h1 className="inundation-title">Preliminary Inundation Estimate</h1>
            <span
              className={`live-badge ${isLiveMode && isLiveTelemetryActive ? '' : isLiveMode ? 'telemetry-not-live' : 'simulation-mode-badge'}`}
              title={isLiveMode ? telemetryFreshness.description : 'Manual scenario testing; PAGASA telemetry is not driving the model.'}
            >
              <span className="pulse-dot"></span>
              {isLiveMode && isLiveTelemetryActive ? 'Live Mode • PAGASA' : 'Simulation Mode'}
            </span>
          </div>
          <p className="inundation-subtitle">
            <span>
              {isLiveMode && isLiveTelemetryActive
                ? `Sto. Niño live input: ${liveLevel.toFixed(2)} m • ${formatObservedAt(liveTelemetry?.updated_at)} • ${telemetryFreshness.ageText}`
                : `Simulation input: ${simulationDepth.toFixed(0)} m • PAGASA ${telemetryFreshness.label}${liveTelemetry?.updated_at ? ` • ${telemetryFreshness.ageText}` : ''}`}
            </span>
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
              <h2 className="card-heading">Legacy PREP Impact Visualization</h2>
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
                  <span className={`legend-swatch ${Number(prepPrediction?.alarm?.level || 0) >= 3 ? 'severe-swatch' : Number(prepPrediction?.alarm?.level || 0) >= 1 ? 'warning-swatch' : 'flood-swatch'}`}></span>
                  <span>Legacy PREP Visualization</span>
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

            <div className="inundation-mode-selector" role="group" aria-label="Inundation input mode">
              <button
                type="button"
                className={`inundation-mode-btn ${isLiveMode ? 'active live' : ''}`}
                onClick={() => setMode('live')}
                disabled={!isLiveTelemetryActive || telemetryLoading}
                title={isLiveTelemetryActive ? 'Use the latest fresh verified PAGASA Sto. Niño observation.' : 'Live Mode requires fresh verified PAGASA telemetry.'}
              >
                <span className="mode-btn-title">Live Mode</span>
                <span className="mode-btn-subtitle">Verified PAGASA telemetry</span>
              </button>
              <button
                type="button"
                className={`inundation-mode-btn ${!isLiveMode ? 'active simulation' : ''}`}
                onClick={() => setMode('simulation')}
              >
                <span className="mode-btn-title">Simulation Mode</span>
                <span className="mode-btn-subtitle">Manual scenario testing</span>
              </button>
            </div>

            <div
              className={`inundation-telemetry-status ${telemetryFreshness.status}`}
              title={telemetryFreshness.description}
            >
              <div className="inundation-telemetry-status-main">
                <span
                  className="inundation-telemetry-dot"
                  style={{ backgroundColor: telemetryFreshness.dotColor }}
                ></span>
                <strong>{telemetryLoading ? 'Checking PAGASA telemetry…' : `PAGASA ${telemetryFreshness.label}`}</strong>
              </div>
              <div className="inundation-telemetry-status-details">
                {telemetryError || (
                  isLiveMode && isLiveTelemetryActive
                    ? `Sto. Niño ${liveLevel.toFixed(2)} m is sent to the separate PREP R API. Affected-area thresholds remain unvalidated pending Audit #16.`
                    : isLiveTelemetryActive
                      ? `Fresh Sto. Niño telemetry (${liveLevel.toFixed(2)} m) is available, but Simulation Mode is active and does not use or modify live telemetry.`
                      : 'Live Mode is unavailable because PAGASA telemetry is not fresh. Simulation Mode remains available for scenario testing.'
                )}
              </div>
            </div>

            <label className="slider-label">
              {isLiveMode ? 'Verified Sto. Niño River Level' : 'Simulated Marikina River Depth'} (in meters)
              {isLiveMode && isLiveTelemetryActive && <span className="live-input-note"> • linked to PAGASA</span>}
              {!isLiveMode && <span className="simulation-input-note"> • manual scenario only</span>}
            </label>
            
            {/* Main Interactive Slider */}
            <div className="slider-controls-row">
              <input 
                type="range"
                min="0"
                max="30"
                step="1"
                value={isLiveMode ? riverDepth : simulationDepth}
                onChange={(e) => setSimulationDepth(Number(e.target.value))}
                disabled={isLiveMode}
                aria-label={isLiveMode ? 'River depth controlled by verified PAGASA telemetry' : 'Manual simulation river depth input'}
                className={`depth-range-input ${isLiveMode ? 'live-locked' : ''}`}
                style={{ '--slider-pct': `${sliderPercentage}%` }}
              />
              <div className="depth-display-box">
                {isLiveMode ? riverDepth.toFixed(2) : simulationDepth}
              </div>
            </div>

            {/* Quick Level Preset Buttons */}
            <div className="preset-buttons-row">
              <span className="preset-title">Quick Presets:</span>
              <button 
                className={`preset-chip ${!isLiveMode && simulationDepth === 0 ? 'active' : ''}`}
                onClick={() => setSimulationDepth(0)}
                disabled={isLiveMode}
              >
                0m Normal
              </button>
              <button 
                className={`preset-chip ${!isLiveMode && simulationDepth === 15 ? 'active' : ''}`}
                onClick={() => setSimulationDepth(15)}
                disabled={isLiveMode}
              >
                15m Level 1
              </button>
              <button 
                className={`preset-chip ${!isLiveMode && simulationDepth === 16 ? 'active' : ''}`}
                onClick={() => setSimulationDepth(16)}
                disabled={isLiveMode}
              >
                16m Level 2
              </button>
              <button 
                className={`preset-chip ${!isLiveMode && simulationDepth === 18 ? 'active' : ''}`}
                onClick={() => setSimulationDepth(18)}
                disabled={isLiveMode}
              >
                18m Level 3
              </button>
            </div>

            <div className={`inundation-mode-note ${isLiveMode ? 'live' : 'simulation'}`}>
              {isLiveMode
                ? 'Live Mode is read-only: the verified PAGASA observation drives the model and manual controls are locked.'
                : 'Simulation Mode is isolated from telemetry: manual values are local scenario inputs and are never written to monitoring_stations or river_level_history.'}
            </div>

            <div className={`inundation-mode-note ${prepApiError ? 'simulation' : 'live'}`} role="status">
              {prepApiLoading
                ? 'PREP R API: evaluating inundation model…'
                : prepApiError
                  ? `PREP R API unavailable: ${prepApiError}. Only the inundation endpoint is unavailable; the rest of RescuAR Admin remains operational.`
                  : `PREP R API connected${PREP_API_URL ? ` • ${PREP_API_URL}` : ''}. The affected-area model is executed outside RescuAR Admin.`}
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

            {prepApiLoading ? (
              <div className="empty-summary-container">
                <div className="empty-icon-halo"><Activity size={36} color="#0284c7" /></div>
                <h3 className="empty-title">Evaluating PREP R Model</h3>
                <p>The separate R-language inundation endpoint is processing the selected river level.</p>
              </div>
            ) : prepApiError ? (
              <div className="empty-summary-container">
                <div className="empty-icon-halo"><AlertTriangle size={36} color="#dc2626" /></div>
                <h3 className="empty-title">PREP Inundation Endpoint Unavailable</h3>
                <p>{prepApiError}</p>
                <p className="empty-subtext">No embedded JavaScript fallback is used. Other RescuAR Admin modules remain independent from this endpoint.</p>
              </div>
            ) : !impactData ? (
              <div className="empty-summary-container">
                <div className="empty-icon-halo">
                  <CheckCircle2 size={36} color="#16a34a" />
                </div>
                <h3 className="empty-title">No PREP Threshold Triggered</h3>
                <p>No legacy PREP affected-area rule is triggered at this river level.</p>
                <p className="empty-subtext">This conclusion came from the separate PREP R API. It does not prove that flooding is impossible; it only means the legacy threshold model did not trigger.</p>
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

                <div className="model-evidence-banner" role="note">
                  <div className="model-evidence-title">PREP R API • Legacy unvalidated affected-area thresholds</div>
                  <div className="model-evidence-text">
                    Source: {impactData.affectedAreaModel?.source || 'PREP R API'}. Triggered threshold(s): {impactData.triggeredThresholds.map((level) => `${level} m`).join(', ')}.
                    These barangay relationships are preserved from PREP for traceability and are not presented as empirically validated until Audit #16/#28 is completed.
                  </div>
                </div>

                {/* Details List */}
                <div className="impact-details-list">
                  
                  {/* Affected Barangays */}
                  <div className="impact-item-row">
                    <div className="impact-label-group">
                      <Home size={15} className="item-icon" />
                      <span className="impact-item-label">Modeled Affected Barangays</span>
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
