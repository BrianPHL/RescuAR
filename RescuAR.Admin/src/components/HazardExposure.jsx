import React, { useEffect, useMemo, useState } from 'react';
import {
  RefreshCw,
  Activity,
  AlertTriangle,
  Users,
  Building2,
  Baby,
  Waves,
  ShieldAlert,
  Radio,
  SlidersHorizontal,
  Map as MapIcon,
  BarChart3,
  Hospital,
} from 'lucide-react';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  Cell,
  LabelList,
} from 'recharts';
import { MapContainer, TileLayer, GeoJSON, CircleMarker, Popup } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import { supabase } from '../supabaseClient';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import { calculateAlertStatus } from '../utils/waterLevelUtils';
import {
  deriveProvisionalExposureScenario,
  describeProvisionalRules,
  normalizeExposureRuleConfig,
  DEFAULT_EXPOSURE_RULE_CONFIG,
} from '../utils/provisionalExposureScenario';
import {
  PREP_EXPOSURE_API_URL,
  isPrepExposureApiConfigured,
  fetchPrepExposureScenario,
  fetchPrepExposureRules,
} from '../services/prepExposureApi';
import barangayBoundaries from '../data/marikinaBarangays.json';
import facilityDirectory from '../data/marikinaFacilities.json';

const SOURCE_ID = 'PAGASA_FFWWS';
const STO_NINO_CODE = '11104204';
const MARIKINA_CENTER = [14.6488, 121.1029];

const CHOROPLETH_BUCKETS = [
  { min: 80, color: '#08306b', label: '80%+' },
  { min: 60, color: '#2171b5', label: '60% - 79.9%' },
  { min: 40, color: '#6baed6', label: '40% - 59.9%' },
  { min: 20, color: '#9ecae1', label: '20% - 39.9%' },
  { min: 0.01, color: '#deebf7', label: '0.1% - 19.9%' },
  { min: 0, color: '#f8fafc', label: 'No mapped exposure' },
];

const FACILITY_TYPE_COLORS = {
  hospital: '#dc2626',
  clinic: '#f97316',
  school: '#2563eb',
  evacuation_center: '#16a34a',
  default: '#475569',
};

function numberFrom(...values) {
  for (const value of values) {
    if (value === null || value === undefined || value === '') continue;
    const number = Number(value);
    if (Number.isFinite(number)) return number;
  }
  return null;
}

function normalizeKey(value) {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

function formatNumber(value) {
  if (value === null || value === undefined || value === '') {
    return '—';
  }

  const number = Number(value);

  return Number.isFinite(number)
    ? Math.round(number).toLocaleString('en-PH')
    : '—';
}

function formatPercent(value, digits = 1) {
  const number = numberFrom(value);
  return number == null ? '—' : `${number.toFixed(digits)}%`;
}

function formatObservedAt(value) {
  if (!value) return 'No verified PAGASA observation';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Invalid timestamp' : date.toLocaleString('en-PH', {
    month: 'long', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit'
  });
}

function hazardLabel(value) {
  if (value === 'High') return 'High only (>1.5 m)';
  if (value === 'Medium') return 'Medium or worse (>0.5 m)';
  return 'Low or worse (0.1 m+)';
}

function exposureColor(percent) {
  const number = numberFrom(percent) ?? 0;
  const bucket = CHOROPLETH_BUCKETS.find((item) => number >= item.min);
  return bucket?.color || '#f8fafc';
}

function facilityTypeLabel(value) {
  if (!value) return 'Other';
  return String(value)
    .replace(/_/g, ' ')
    .replace(/\b\w/g, (match) => match.toUpperCase());
}

function facilityTypeColor(value) {
  const key = String(value || '').toLowerCase();
  return FACILITY_TYPE_COLORS[key] || FACILITY_TYPE_COLORS.default;
}

function metricTooltipFormatter(value) {
  const number = numberFrom(value);
  return number == null ? '—' : number.toLocaleString('en-PH');
}

function pctTooltipFormatter(value) {
  const number = numberFrom(value);
  return number == null ? '—' : `${number.toFixed(1)}%`;
}

function SectionHint({ children }) {
  return (
    <p style={{ margin: '4px 0 0', fontSize: 12, color: '#64748b' }}>
      {children}
    </p>
  );
}

function LegendSwatch({ color, label }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <span style={{ width: 14, height: 14, borderRadius: 4, background: color, border: '1px solid rgba(15, 23, 42, 0.08)' }} />
      <span style={{ fontSize: 12, color: '#475569' }}>{label}</span>
    </div>
  );
}

export default function HazardExposure() {
  const [scenarioMode, setScenarioMode] = useState('automatic');
  const [manualReturnPeriod, setManualReturnPeriod] = useState(25);
  const [manualMinimumHazard, setManualMinimumHazard] = useState('Low');
  const [liveTelemetry, setLiveTelemetry] = useState(null);
  const [sourceStatus, setSourceStatus] = useState(null);
  const [telemetryError, setTelemetryError] = useState('');
  const [exposure, setExposure] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);
  const [nowMs, setNowMs] = useState(Date.now());
  const [exposureRuleConfig, setExposureRuleConfig] = useState(DEFAULT_EXPOSURE_RULE_CONFIG);
  const [ruleConfigError, setRuleConfigError] = useState('');

  const loadExposureRuleConfig = async () => {
    setRuleConfigError('');

    if (!isPrepExposureApiConfigured()) {
      setExposureRuleConfig(DEFAULT_EXPOSURE_RULE_CONFIG);
      return;
    }

    try {
      const payload = await fetchPrepExposureRules();
      setExposureRuleConfig(normalizeExposureRuleConfig(payload));
    } catch (requestError) {
      console.error('Unable to load PREP exposure-rule config:', requestError);
      setExposureRuleConfig(DEFAULT_EXPOSURE_RULE_CONFIG);
      setRuleConfigError(requestError?.message || 'Unable to load PREP exposure-rule configuration.');
    }
  };

  const loadTelemetryContext = async () => {
    setTelemetryError('');
    try {
      const [{ data: station, error: stationError }, { data: status, error: statusError }] = await Promise.all([
        supabase
          .from('monitoring_stations')
          .select('station_code,station_name,source_station_name,level,status,trend,observed_at,source_checked_at,alert_threshold_m,alarm_threshold_m,critical_threshold_m,source')
          .eq('source', SOURCE_ID)
          .eq('station_code', STO_NINO_CODE)
          .maybeSingle(),
        supabase
          .from('telemetry_source_status')
          .select('*')
          .eq('source', SOURCE_ID)
          .maybeSingle()
      ]);

      if (stationError) throw stationError;
      if (statusError) throw statusError;

      setLiveTelemetry(station || null);
      setSourceStatus(status || null);
      setNowMs(Date.now());
    } catch (requestError) {
      console.error('Unable to load PAGASA exposure context:', requestError);
      setTelemetryError(requestError?.message || 'Unable to load PAGASA telemetry context.');
      setLiveTelemetry(null);
      setSourceStatus(null);
      setNowMs(Date.now());
    }
  };

  useEffect(() => {
    void loadTelemetryContext();
    void loadExposureRuleConfig();
    const timer = setInterval(() => setNowMs(Date.now()), 60 * 1000);
    const channel = supabase
      .channel('exposure-live-context')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations', filter: `station_code=eq.${STO_NINO_CODE}` }, () => void loadTelemetryContext())
      .on('postgres_changes', { event: '*', schema: 'public', table: 'telemetry_source_status', filter: `source=eq.${SOURCE_ID}` }, () => void loadTelemetryContext())
      .subscribe();
    return () => {
      clearInterval(timer);
      supabase.removeChannel(channel);
    };
  }, []);

  const freshness = getTelemetryFreshness(liveTelemetry?.observed_at, nowMs);
  const liveLevel = numberFrom(liveTelemetry?.level);
  const alert = calculateAlertStatus(liveLevel, liveTelemetry?.station_name, liveTelemetry);
  const automaticScenario = useMemo(() => deriveProvisionalExposureScenario({
    telemetry: liveTelemetry,
    freshness,
    sourceStatus,
    ruleConfig: exposureRuleConfig,
  }), [
    liveTelemetry,
    freshness.status,
    freshness.ageText,
    sourceStatus?.worker_status,
    sourceStatus?.source_status,
    exposureRuleConfig,
  ]);

  const activeScenario = scenarioMode === 'automatic'
    ? (automaticScenario.active ? {
      returnPeriod: automaticScenario.returnPeriod,
      minimumHazard: automaticScenario.minimumHazard
    } : null)
    : {
      returnPeriod: manualReturnPeriod,
      minimumHazard: manualMinimumHazard
    };

  useEffect(() => {
    const controller = new AbortController();

    if (!activeScenario) {
      setLoading(false);
      setError('');
      setExposure(null);
      return () => controller.abort();
    }

    if (!isPrepExposureApiConfigured()) {
      setLoading(false);
      setExposure(null);
      setError('VITE_PREP_EXPOSURE_API_URL is not configured for the RescuAR Admin deployment.');
      return () => controller.abort();
    }

    setLoading(true);
    setError('');
    fetchPrepExposureScenario({
      returnPeriod: activeScenario.returnPeriod,
      minimumHazard: activeScenario.minimumHazard,
      signal: controller.signal
    })
      .then((result) => {
        if (!controller.signal.aborted) setExposure(result);
      })
      .catch((requestError) => {
        if (requestError?.name === 'AbortError') return;
        console.error('PREP exposure request failed:', requestError);
        setExposure(null);
        setError(requestError?.message || 'PREP R exposure service is unavailable.');
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [scenarioMode, activeScenario?.returnPeriod, activeScenario?.minimumHazard, refreshKey]);

  const barangays = useMemo(
    () => Array.isArray(exposure?.barangays)
      ? exposure.barangays
      : Array.isArray(exposure?.results)
        ? exposure.results
        : [],
    [exposure]
  );

  const facilityRows = useMemo(
    () => Array.isArray(exposure?.facilities) ? exposure.facilities : [],
    [exposure]
  );

  const summary = exposure?.summary || exposure?.city_summary || exposure?.exposure || exposure || {};

  const affectedPopulation = numberFrom(
    summary.estimated_affected_population,
    summary.estimatedAffectedPopulation,
    summary.population_exposed,
    summary.total_population_affected
  );

  const affectedChildren = numberFrom(
    summary.estimated_affected_children,
    summary.estimatedAffectedChildren,
    summary.children_exposed,
    summary.total_children_affected
  );

  const cityPopulationBaseline = numberFrom(
    summary.city_population_baseline,
    summary.cityPopulationBaseline
  );

  const cityChildrenBaseline = numberFrom(
    summary.city_children_baseline,
    summary.cityChildrenBaseline
  );

  const affectedPopulationPct = numberFrom(
    summary.estimated_affected_population_pct,
    summary.estimatedAffectedPopulationPct,
  );

  const affectedChildrenPct = numberFrom(
    summary.estimated_affected_children_pct,
    summary.estimatedAffectedChildrenPct,
  );

  const facilities = numberFrom(
    summary.facilitiesInHazardArea,
    summary.facilities_exposed,
    summary.facilities_at_risk,
    summary.exposed_facilities,
    exposure && Array.isArray(exposure.facilities) ? facilityRows.length : null
  );

  const barangayCount = numberFrom(
    summary.barangaysWithExposure,
    summary.barangays_with_mapped_exposure,
    summary.affected_barangays,
    summary.barangays_with_exposure,
    exposure && Array.isArray(exposure?.barangays) ? barangays.length : null
  );

  const rules = useMemo(
    () => describeProvisionalRules(exposureRuleConfig),
    [exposureRuleConfig]
  );
  const automaticRuleApproved = Boolean(exposureRuleConfig?.approved);
  const automaticRuleLabel = automaticRuleApproved ? 'Calibrated Auto-Scenario' : 'Provisional Auto-Scenario';
  const automaticRuleHeading = automaticRuleApproved ? 'Configured calibrated mapping' : 'Configured provisional mapping';
  const noAutomaticScenario = scenarioMode === 'automatic' && !automaticScenario.active;
  const exposureUnavailableMessage = error
    ? 'Exposure service unavailable.'
    : noAutomaticScenario
      ? automaticScenario.reason
      : 'No barangay rows were returned by the exposure service.';

  const enrichedBarangays = useMemo(() => {
    return barangays
      .map((row, index) => {
        const hazardAreaPct = numberFrom(row.selected_hazard_area_pct, row.hazard_area_pct, row.exposure_pct);
        const estimatedAffectedPopulation = numberFrom(row.estimated_affected_population, row.population_exposed);
        const estimatedAffectedChildren = numberFrom(row.estimated_affected_children, row.children_exposed);
        const totalPopulation = numberFrom(row.population_2024, row.population, row.city_population);
        const totalChildren = numberFrom(row.children_2020, row.children);

        return {
          ...row,
          chartKey: String(row.barangay_id || row.barangay_name || row.name || index),
          barangay_name: row.barangay_name || row.name || 'Unnamed barangay',
          barangay_id: row.barangay_id || null,
          hazardAreaPct,
          estimatedAffectedPopulation,
          estimatedAffectedChildren,
          totalPopulation,
          totalChildren,
        };
      })
      .sort((a, b) => (b.estimatedAffectedPopulation || 0) - (a.estimatedAffectedPopulation || 0));
  }, [barangays]);

  const choroplethGeoJson = useMemo(() => {
    const byId = new Map();
    const byName = new Map();

    enrichedBarangays.forEach((row) => {
      if (row.barangay_id) byId.set(String(row.barangay_id), row);
      byName.set(normalizeKey(row.barangay_name), row);
    });

    return {
      ...barangayBoundaries,
      features: (barangayBoundaries?.features || []).map((feature) => {
        const props = feature.properties || {};
        const joined = byId.get(String(props.barangay_id)) || byName.get(normalizeKey(props.barangay_name)) || null;

        return {
          ...feature,
          properties: {
            ...props,
            exposure_joined: Boolean(joined),
            hazard_area_pct: joined?.hazardAreaPct ?? 0,
            estimated_affected_population: joined?.estimatedAffectedPopulation ?? 0,
            estimated_affected_children: joined?.estimatedAffectedChildren ?? 0,
            total_population: joined?.totalPopulation ?? null,
            total_children: joined?.totalChildren ?? null,
          },
        };
      })
    };
  }, [enrichedBarangays]);

  const rankedBarangayChart = useMemo(() => {
    return enrichedBarangays.map((row) => ({
      barangay: row.barangay_name,
      affectedPopulation: row.estimatedAffectedPopulation || 0,
      affectedChildren: row.estimatedAffectedChildren || 0,
      hazardAreaPct: row.hazardAreaPct || 0,
    }));
  }, [enrichedBarangays]);

  const exposureSplitData = useMemo(() => {
    const totalPopulation = cityPopulationBaseline || 0;
    const totalChildren = cityChildrenBaseline || 0;
    const affectedPop = affectedPopulation || 0;
    const affectedKids = affectedChildren || 0;

    return [
      {
        cohort: 'Population',
        affected: affectedPop,
        unaffected: Math.max(totalPopulation - affectedPop, 0),
      },
      {
        cohort: 'Children',
        affected: affectedKids,
        unaffected: Math.max(totalChildren - affectedKids, 0),
      },
    ];
  }, [affectedPopulation, affectedChildren, cityPopulationBaseline, cityChildrenBaseline]);

  const facilityTypeData = useMemo(() => {
    const counts = new Map();

    facilityRows.forEach((row) => {
      const key = String(row.facility_type || 'other').toLowerCase();
      counts.set(key, (counts.get(key) || 0) + 1);
    });

    return Array.from(counts.entries())
      .map(([type, count]) => ({
        type,
        label: facilityTypeLabel(type),
        exposedFacilities: count,
        color: facilityTypeColor(type),
      }))
      .sort((a, b) => b.exposedFacilities - a.exposedFacilities);
  }, [facilityRows]);

  const exposedFacilitiesWithCoords = useMemo(() => {
    const byOsmId = new Map();
    const byNameAndType = new Map();

    facilityDirectory.forEach((facility) => {
      const osmKey = String(facility.osm_id || '');
      if (osmKey) byOsmId.set(osmKey, facility);
      byNameAndType.set(
        `${normalizeKey(facility.facility_name)}|${normalizeKey(facility.facility_type)}`,
        facility
      );
    });

    return facilityRows
      .map((row, index) => {
        const directoryMatch = byOsmId.get(String(row.osm_id || ''))
          || byNameAndType.get(`${normalizeKey(row.facility_name)}|${normalizeKey(row.facility_type)}`);

        if (!directoryMatch) return null;

        return {
          ...row,
          key: String(row.osm_id || `${row.facility_name}-${index}`),
          latitude: directoryMatch.latitude,
          longitude: directoryMatch.longitude,
          facility_type: row.facility_type || directoryMatch.facility_type,
          facility_name: row.facility_name || directoryMatch.facility_name,
        };
      })
      .filter(Boolean);
  }, [facilityRows]);

  const choroplethStyle = (feature) => {
    const percent = feature?.properties?.hazard_area_pct || 0;
    return {
      color: '#334155',
      weight: 1,
      fillColor: exposureColor(percent),
      fillOpacity: percent > 0 ? 0.75 : 0.25,
      opacity: 0.85,
    };
  };

  const bindBarangayPopup = (feature, layer) => {
    const props = feature?.properties || {};
    layer.bindPopup(`
      <div style="font-family: Inter, Arial, sans-serif; min-width: 220px;">
        <div style="font-weight: 700; margin-bottom: 6px;">${props.barangay_name || 'Barangay'}</div>
        <div>Hazard area: <strong>${formatPercent(props.hazard_area_pct)}</strong></div>
        <div>Estimated exposed population: <strong>${formatNumber(props.estimated_affected_population)}</strong></div>
        <div>Estimated exposed children: <strong>${formatNumber(props.estimated_affected_children)}</strong></div>
      </div>
    `);
  };

  return (
    <div className="inundation-container">
      <div className="inundation-header">
        <div className="title-group">
          <div className="title-row">
            <h1 className="inundation-title">Flood Hazard Exposure</h1>
            <span className="live-badge"><span className="pulse-dot"></span>{automaticRuleLabel}</span>
          </div>
          <p className="inundation-subtitle">Uses live PAGASA station condition to select a LiPAD/UP DREAM reference hazard scenario. {automaticRuleApproved ? 'The status-to-scenario mapping is an approved historical calibration, but the output remains a reference exposure estimate rather than a live hydraulic inundation prediction.' : 'The current mapping is provisional and has not been promoted through independent holdout validation.'}</p>
        </div>
        <button className="refresh-btn" onClick={() => { void loadTelemetryContext(); void loadExposureRuleConfig(); setRefreshKey((key) => key + 1); }}><RefreshCw size={15} /><span>Refresh View</span></button>
      </div>

      <div className="inundation-grid">
        <div className="inundation-card map-card" style={{ minHeight: 560 }}>
          <div className="card-header-bar"><div className="card-title-container"><Waves size={18} className="text-brand" /><h2 className="card-heading">Current PAGASA Hydrological Context</h2></div></div>
          <div style={{ padding: 22 }}>
            <div className="inundation-context-grid row g-3">
              <div className="col-12 col-md-6"><div className="h-100" style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}>
                <div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>STO. NIÑO (MMDA)</div>
                <div style={{ fontSize: 34, fontWeight: 800, marginTop: 5 }}>{liveLevel == null ? '--' : `${liveLevel.toFixed(2)} m EL`}</div>
                <div style={{ marginTop: 8, color: alert.color, fontWeight: 700 }}>{alert.label}</div>
                <div style={{ marginTop: 6, color: '#64748b', fontSize: 12 }}>Trend: {liveTelemetry?.trend || 'Unknown'}</div>
              </div></div>
              <div className="col-12 col-md-6"><div className="h-100" style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}>
                <div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>OBSERVATION FRESHNESS</div>
                <div style={{ fontSize: 22, fontWeight: 800, marginTop: 8, color: freshness.color }}>{freshness.label}</div>
                <div style={{ marginTop: 6, color: '#64748b' }}>{freshness.ageText}</div>
                <div style={{ marginTop: 6, color: '#64748b', fontSize: 12 }}>{freshness.description}</div>
              </div></div>
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#f8fafc', border: '1px solid #e2e8f0', fontSize: 13, color: '#475569' }}>
              <strong>Last verified observation:</strong> {formatObservedAt(liveTelemetry?.observed_at)}<br />
              <strong>PAGASA source:</strong> {sourceStatus?.source_status || 'unknown'} • <strong>Worker:</strong> {sourceStatus?.worker_status || 'unknown'}
              {telemetryError && <><br /><strong>Telemetry error:</strong> {telemetryError}</>}
              {ruleConfigError && <><br /><strong>Rule config:</strong> using provisional fallback ({ruleConfigError})</>}
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: automaticScenario.active ? '#ecfdf5' : '#eff6ff', border: automaticScenario.active ? '1px solid #a7f3d0' : '1px solid #bfdbfe', color: automaticScenario.active ? '#065f46' : '#1e40af', fontSize: 13 }}>
              <strong>Automatic reference rule:</strong> {automaticScenario.shortLabel}<br />
              {automaticScenario.reason}
            </div>

            <div style={{ marginTop: 18 }}>
              <div style={{ fontWeight: 800, fontSize: 13, marginBottom: 8 }}>{automaticRuleHeading}</div>
              <div className="inundation-rules-grid row g-2">
                {rules.map((rule) => (
                  <div key={rule.condition} className="col-12 col-sm-6 col-xxl-3"><div className="h-100" style={{ padding: 10, border: '1px solid #e2e8f0', borderRadius: 9, background: '#fff' }}>
                    <div style={{ fontWeight: 800, fontSize: 12 }}>{rule.condition}</div>
                    <div style={{ marginTop: 4, fontSize: 12 }}>{rule.scenario}</div>
                    <div style={{ marginTop: 2, fontSize: 11, color: '#64748b' }}>{rule.severity}</div>
                  </div></div>
                ))}
              </div>
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#fff7ed', border: '1px solid #fed7aa', color: '#9a3412', fontSize: 12 }}>
              <strong>Scientific limitation:</strong> {automaticRuleApproved ? 'The Alert/Alarm/Critical mapping has passed the configured promotion gate using historical calibration plus held-out event validation; this does not make the reference scenario a live hydraulic flood-extent prediction.' : 'Alert/Alarm/Critical levels are currently mapped by provisional project rule only.'} Population and child figures remain exposure estimates, and facility markers indicate spatial intersection with a mapped hazard area. These outputs must not be presented as confirmed impacts or confirmed safe areas.
            </div>
          </div>
        </div>

        <div className="right-panel-stack">
          <div className="inundation-card input-card">
            <div className="card-header-bar"><div className="card-title-container"><ShieldAlert size={18} className="text-brand" /><h2 className="card-heading">Scenario Control</h2></div></div>
            <div style={{ padding: '16px 20px' }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginBottom: 16 }}>
                <button
                  type="button"
                  onClick={() => setScenarioMode('automatic')}
                  style={{ padding: 10, borderRadius: 8, border: scenarioMode === 'automatic' ? '1px solid #2563eb' : '1px solid #cbd5e1', background: scenarioMode === 'automatic' ? '#eff6ff' : '#fff', color: scenarioMode === 'automatic' ? '#1d4ed8' : '#475569', fontWeight: 700, cursor: 'pointer' }}
                ><Radio size={14} style={{ marginRight: 6, verticalAlign: 'middle' }} />Automatic</button>
                <button
                  type="button"
                  onClick={() => setScenarioMode('manual')}
                  style={{ padding: 10, borderRadius: 8, border: scenarioMode === 'manual' ? '1px solid #2563eb' : '1px solid #cbd5e1', background: scenarioMode === 'manual' ? '#eff6ff' : '#fff', color: scenarioMode === 'manual' ? '#1d4ed8' : '#475569', fontWeight: 700, cursor: 'pointer' }}
                ><SlidersHorizontal size={14} style={{ marginRight: 6, verticalAlign: 'middle' }} />Manual</button>
              </div>

              {scenarioMode === 'automatic' ? (
                <div style={{ padding: 12, borderRadius: 9, background: '#f8fafc', border: '1px solid #e2e8f0', marginBottom: 14 }}>
                  <div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>AUTOMATIC RESULT</div>
                  <div style={{ marginTop: 6, fontWeight: 800 }}>{automaticScenario.shortLabel}</div>
                  <div style={{ marginTop: 5, fontSize: 12, color: '#64748b' }}>{automaticScenario.reason}</div>
                </div>
              ) : (
                <>
                  <label className="slider-label">Return period</label>
                  <select value={manualReturnPeriod} onChange={(e) => setManualReturnPeriod(Number(e.target.value))} style={{ width: '100%', padding: 10, border: '1px solid #cbd5e1', borderRadius: 8, marginBottom: 16 }}>
                    <option value={5}>5-Year Flood (~20% annual exceedance probability)</option>
                    <option value={25}>25-Year Flood (~4% annual exceedance probability)</option>
                    <option value={100}>100-Year Flood (~1% annual exceedance probability)</option>
                  </select>
                  <label className="slider-label">Minimum flood severity counted as affected</label>
                  <select value={manualMinimumHazard} onChange={(e) => setManualMinimumHazard(e.target.value)} style={{ width: '100%', padding: 10, border: '1px solid #cbd5e1', borderRadius: 8 }}>
                    <option value="Low">Low or worse (0.1 m+)</option>
                    <option value="Medium">Medium or worse (&gt;0.5 m)</option>
                    <option value="High">High only (&gt;1.5 m)</option>
                  </select>
                </>
              )}

              {activeScenario && (
                <div style={{ marginTop: 14, fontSize: 12, color: '#475569' }}>
                  Requesting: <strong>{activeScenario.returnPeriod}-Year • {hazardLabel(activeScenario.minimumHazard)}</strong>
                </div>
              )}

              <div className={`inundation-mode-note ${error ? 'simulation' : 'live'}`} style={{ marginTop: 16 }}>
                {loading
                  ? 'PREP R API: calculating hazard exposure…'
                  : error
                    ? error
                    : !activeScenario
                      ? 'No PREP R exposure request is made while the automatic rule is inactive or suspended.'
                      : `PREP R API connected${PREP_EXPOSURE_API_URL ? ` • ${PREP_EXPOSURE_API_URL}` : ''}`}
              </div>
              {!isPrepExposureApiConfigured() && activeScenario && (
                <div style={{ marginTop: 10, padding: 10, borderRadius: 8, background: '#faf5ff', border: '1px solid #e9d5ff', color: '#6b21a8', fontSize: 12 }}>
                  Configure <strong>VITE_PREP_EXPOSURE_API_URL</strong> and rebuild the Admin to enable PREP R calculations.
                </div>
              )}
            </div>
          </div>

          <div className="inundation-card summary-card">
            <div className="card-header-bar"><div className="card-title-container"><Activity size={18} className="text-brand" /><h2 className="card-heading">Exposure Summary</h2></div></div>
            <div style={{ padding: 18, display: 'grid', gap: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Users size={15} style={{ marginRight: 6 }} />Estimated exposed population</span><strong>{formatNumber(affectedPopulation)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Baby size={15} style={{ marginRight: 6 }} />Estimated exposed children</span><strong>{formatNumber(affectedChildren)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Building2 size={15} style={{ marginRight: 6 }} />Facilities within mapped hazard area</span><strong>{formatNumber(facilities)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><AlertTriangle size={15} style={{ marginRight: 6 }} />Barangays with mapped exposure</span><strong>{formatNumber(barangayCount)}</strong></div>
              {!activeScenario && <div style={{ marginTop: 5, fontSize: 12, color: '#64748b' }}>No automatic exposure estimate is active. Switch to Manual to inspect a reference scenario while conditions remain below Alert or telemetry is not Fresh.</div>}
            </div>
          </div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr', gap: 16, marginTop: 16 }}>
        <div className="inundation-card">
          <div className="card-header-bar">
            <div className="card-title-container"><MapIcon size={18} className="text-brand" /><h2 className="card-heading">Barangay Choropleth Map</h2></div>
          </div>
          <div style={{ padding: 16 }}>
            <SectionHint>Shows where the affected areas are. Darker shading means a higher portion of the barangay is inside the selected hazard scenario.</SectionHint>
            <div style={{ marginTop: 14, border: '1px solid #e2e8f0', borderRadius: 14, overflow: 'hidden' }}>
              <MapContainer center={MARIKINA_CENTER} zoom={13} style={{ height: 380, width: '100%' }} scrollWheelZoom={false}>
                <TileLayer
                  attribution='&copy; OpenStreetMap contributors'
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                />
                <GeoJSON key={`${activeScenario?.returnPeriod || 'none'}-${activeScenario?.minimumHazard || 'none'}-${rankedBarangayChart.length}`} data={choroplethGeoJson} style={choroplethStyle} onEachFeature={bindBarangayPopup} />
              </MapContainer>
            </div>
            <div style={{ marginTop: 14, display: 'flex', flexWrap: 'wrap', gap: 12 }}>
              {CHOROPLETH_BUCKETS.map((bucket) => (
                <LegendSwatch key={bucket.label} color={bucket.color} label={bucket.label} />
              ))}
            </div>
          </div>
        </div>

        <div className="inundation-card">
          <div className="card-header-bar">
            <div className="card-title-container"><BarChart3 size={18} className="text-brand" /><h2 className="card-heading">Barangays by Estimated Exposed Population</h2></div>
          </div>
          <div style={{ padding: 16 }}>
            <SectionHint>Ranks barangays by estimated exposed population. This is an exposure ranking, not an operational priority score.</SectionHint>
            <div style={{ marginTop: 12, height: Math.max(360, rankedBarangayChart.length * 28) }}>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={rankedBarangayChart} layout="vertical" margin={{ top: 8, right: 24, left: 18, bottom: 8 }}>
                  <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                  <XAxis type="number" tickFormatter={(value) => Number(value).toLocaleString('en-PH')} />
                  <YAxis dataKey="barangay" type="category" width={110} tick={{ fontSize: 11 }} />
                  <Tooltip
                    formatter={(value, name) => {
                      if (name === 'Hazard area %') return [pctTooltipFormatter(value), name];
                      return [metricTooltipFormatter(value), name];
                    }}
                  />
                  <Bar dataKey="affectedPopulation" name="Estimated exposed population" fill="#2563eb" radius={[0, 6, 6, 0]}>
                    <LabelList dataKey="affectedPopulation" position="right" formatter={(value) => formatNumber(value)} style={{ fill: '#475569', fontSize: 11 }} />
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '0.9fr 1.1fr', gap: 16, marginTop: 16 }}>
        <div className="inundation-card">
          <div className="card-header-bar">
            <div className="card-title-container"><Users size={18} className="text-brand" /><h2 className="card-heading">Estimated Exposed vs Not Mapped as Exposed</h2></div>
          </div>
          <div style={{ padding: 16 }}>
            <SectionHint>Shows the estimated population inside the selected mapped hazard area versus the population not mapped as exposed by this reference scenario. Areas not mapped as exposed are not guaranteed safe.</SectionHint>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 12, marginTop: 12, marginBottom: 8 }}>
              <div style={{ padding: 12, borderRadius: 12, border: '1px solid #dbeafe', background: '#eff6ff' }}>
                <div style={{ fontSize: 12, fontWeight: 700, color: '#1d4ed8' }}>Estimated exposed share</div>
                <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800 }}>{formatPercent(affectedPopulationPct)}</div>
                <div style={{ marginTop: 4, fontSize: 12, color: '#475569' }}>{formatNumber(affectedPopulation)} people</div>
              </div>
              <div style={{ padding: 12, borderRadius: 12, border: '1px solid #dcfce7', background: '#f0fdf4' }}>
                <div style={{ fontSize: 12, fontWeight: 700, color: '#15803d' }}>Children exposed</div>
                <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800 }}>{formatPercent(affectedChildrenPct)}</div>
                <div style={{ marginTop: 4, fontSize: 12, color: '#475569' }}>{formatNumber(affectedChildren)} children</div>
              </div>
            </div>
            <div style={{ height: 300 }}>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={exposureSplitData} layout="vertical" margin={{ top: 10, right: 10, left: 10, bottom: 10 }}>
                  <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                  <XAxis type="number" tickFormatter={(value) => Number(value).toLocaleString('en-PH')} />
                  <YAxis type="category" dataKey="cohort" width={90} />
                  <Tooltip formatter={(value) => metricTooltipFormatter(value)} />
                  <Legend />
                  <Bar dataKey="affected" name="Estimated exposed" stackId="a" fill="#ef4444" radius={[0, 0, 0, 0]} />
                  <Bar dataKey="unaffected" name="Not mapped as exposed" stackId="a" fill="#cbd5e1" radius={[0, 6, 6, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>

        <div className="inundation-card">
          <div className="card-header-bar">
            <div className="card-title-container"><Hospital size={18} className="text-brand" /><h2 className="card-heading">Facilities within Mapped Hazard Area</h2></div>
          </div>
          <div style={{ padding: 16 }}>
            <SectionHint>Shows facilities whose mapped points intersect the selected hazard area. This indicates spatial exposure only and does not confirm flooding, damage, closure, or loss of service.</SectionHint>
            <div style={{ display: 'grid', gridTemplateColumns: '0.9fr 1.1fr', gap: 16, marginTop: 12 }}>
              <div style={{ minHeight: 320 }}>
                <ResponsiveContainer width="100%" height={320}>
                  <BarChart data={facilityTypeData} layout="vertical" margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
                    <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                    <XAxis type="number" allowDecimals={false} />
                    <YAxis dataKey="label" type="category" width={90} tick={{ fontSize: 11 }} />
                    <Tooltip formatter={(value) => metricTooltipFormatter(value)} />
                    <Bar dataKey="exposedFacilities" name="Facilities within mapped hazard area" radius={[0, 6, 6, 0]}>
                      {facilityTypeData.map((entry) => (
                        <Cell key={entry.type} fill={entry.color} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <div style={{ border: '1px solid #e2e8f0', borderRadius: 14, overflow: 'hidden' }}>
                <MapContainer center={MARIKINA_CENTER} zoom={13} style={{ height: 320, width: '100%' }} scrollWheelZoom={false}>
                  <TileLayer
                    attribution='&copy; OpenStreetMap contributors'
                    url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                  />
                  {exposedFacilitiesWithCoords.map((facility) => (
                    <CircleMarker
                      key={facility.key}
                      center={[facility.latitude, facility.longitude]}
                      radius={7}
                      pathOptions={{
                        color: facilityTypeColor(facility.facility_type),
                        fillColor: facilityTypeColor(facility.facility_type),
                        fillOpacity: 0.85,
                        weight: 1,
                      }}
                    >
                      <Popup>
                        <div style={{ minWidth: 200 }}>
                          <div style={{ fontWeight: 700 }}>{facility.facility_name || 'Unnamed facility'}</div>
                          <div style={{ marginTop: 4 }}>Type: <strong>{facilityTypeLabel(facility.facility_type)}</strong></div>
                          <div>Barangay: <strong>{facility.barangay_name || 'Unknown'}</strong></div>
                          {facility.hazard_class && <div>Hazard class: <strong>{facility.hazard_class}</strong></div>}
                        </div>
                      </Popup>
                    </CircleMarker>
                  ))}
                </MapContainer>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="inundation-card" style={{ marginTop: 16 }}>
        <div className="card-header-bar"><div className="card-title-container"><Users size={18} className="text-brand" /><h2 className="card-heading">Detailed Barangay Exposure Table</h2></div></div>
        <div className="table-container" style={{ padding: 16 }}>
          <SectionHint>Retained for officers who need the exact barangay-level exposure estimates behind the visualizations.</SectionHint>
          <table className="data-table" style={{ marginTop: 12 }}>
            <thead><tr><th>BARANGAY</th><th>MAPPED HAZARD AREA</th><th>EST. EXPOSED POPULATION</th><th>EST. EXPOSED CHILDREN</th></tr></thead>
            <tbody>
              {enrichedBarangays.map((row) => (
                <tr key={row.chartKey}>
                  <td style={{ fontWeight: 600 }}>{row.barangay_name}</td>
                  <td>{formatPercent(row.hazardAreaPct)}</td>
                  <td>{formatNumber(row.estimatedAffectedPopulation)}</td>
                  <td>{formatNumber(row.estimatedAffectedChildren)}</td>
                </tr>
              ))}
              {!enrichedBarangays.length && <tr><td colSpan="4" style={{ textAlign: 'center', padding: 30, color: '#64748b' }}>{loading ? 'Calculating exposure…' : exposureUnavailableMessage}</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
