import React, { useEffect, useMemo, useState } from 'react';
import { RefreshCw, Activity, AlertTriangle, Users, Building2, Baby, Waves, ShieldAlert, Radio, SlidersHorizontal } from 'lucide-react';
import { supabase } from '../supabaseClient';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import { calculateAlertStatus } from '../utils/waterLevelUtils';
import { deriveProvisionalExposureScenario, describeProvisionalRules } from '../utils/provisionalExposureScenario';
import {
  PREP_EXPOSURE_API_URL,
  isPrepExposureApiConfigured,
  fetchPrepExposureScenario,
} from "../services/prepExposureApi";


const SOURCE_ID = 'PAGASA_FFWWS';
const STO_NINO_CODE = '11104204';

function numberFrom(...values) {
  for (const value of values) {
    if (value === null || value === undefined || value === '') continue;
    const number = Number(value);
    if (Number.isFinite(number)) return number;
  }
  return null;
}

function formatNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) ? Math.round(number).toLocaleString('en-PH') : '—';
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
    sourceStatus
  }), [liveTelemetry, freshness.status, freshness.ageText, sourceStatus?.worker_status, sourceStatus?.source_status]);

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
    summary.estimatedAffectedPopulation,
    summary.estimated_affected_population,
    summary.population_exposed,
    summary.total_population_affected
  );
  const affectedChildren = numberFrom(
    summary.estimatedAffectedChildren,
    summary.estimated_affected_children,
    summary.children_exposed,
    summary.total_children_affected
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
  const rules = useMemo(() => describeProvisionalRules(), []);

  const noAutomaticScenario = scenarioMode === 'automatic' && !automaticScenario.active;
  const exposureUnavailableMessage = error
    ? 'Exposure service unavailable.'
    : noAutomaticScenario
      ? automaticScenario.reason
      : 'No barangay rows were returned by the exposure service.';

  return (
    <div className="inundation-container">
      <div className="inundation-header">
        <div className="title-group">
          <div className="title-row">
            <h1 className="inundation-title">Flood Hazard Exposure</h1>
            <span className="live-badge"><span className="pulse-dot"></span>Provisional Auto-Scenario</span>
          </div>
          <p className="inundation-subtitle">Uses live PAGASA station condition to automatically select a LiPAD/UP DREAM reference hazard scenario. This is a provisional rule-based reference, not a calibrated prediction of current flood extent.</p>
        </div>
        <button className="refresh-btn" onClick={() => { void loadTelemetryContext(); setRefreshKey((key) => key + 1); }}><RefreshCw size={15} /><span>Refresh View</span></button>
      </div>

      <div className="inundation-grid">
        <div className="inundation-card map-card" style={{ minHeight: 560 }}>
          <div className="card-header-bar"><div className="card-title-container"><Waves size={18} className="text-brand" /><h2 className="card-heading">Current PAGASA Hydrological Context</h2></div></div>
          <div style={{ padding: 22 }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 16 }}>
              <div style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}>
                <div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>STO. NIÑO (MMDA)</div>
                <div style={{ fontSize: 34, fontWeight: 800, marginTop: 5 }}>{liveLevel == null ? '--' : `${liveLevel.toFixed(2)} m EL`}</div>
                <div style={{ marginTop: 8, color: alert.color, fontWeight: 700 }}>{alert.label}</div>
                <div style={{ marginTop: 6, color: '#64748b', fontSize: 12 }}>Trend: {liveTelemetry?.trend || 'Unknown'}</div>
              </div>
              <div style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}>
                <div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>OBSERVATION FRESHNESS</div>
                <div style={{ fontSize: 22, fontWeight: 800, marginTop: 8, color: freshness.color }}>{freshness.label}</div>
                <div style={{ marginTop: 6, color: '#64748b' }}>{freshness.ageText}</div>
                <div style={{ marginTop: 6, color: '#64748b', fontSize: 12 }}>{freshness.description}</div>
              </div>
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#f8fafc', border: '1px solid #e2e8f0', fontSize: 13, color: '#475569' }}>
              <strong>Last verified observation:</strong> {formatObservedAt(liveTelemetry?.observed_at)}<br />
              <strong>PAGASA source:</strong> {sourceStatus?.source_status || 'unknown'} • <strong>Worker:</strong> {sourceStatus?.worker_status || 'unknown'}
              {telemetryError && <><br /><strong>Telemetry error:</strong> {telemetryError}</>}
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: automaticScenario.active ? '#ecfdf5' : '#eff6ff', border: automaticScenario.active ? '1px solid #a7f3d0' : '1px solid #bfdbfe', color: automaticScenario.active ? '#065f46' : '#1e40af', fontSize: 13 }}>
              <strong>Automatic reference rule:</strong> {automaticScenario.shortLabel}<br />
              {automaticScenario.reason}
            </div>

            <div style={{ marginTop: 18 }}>
              <div style={{ fontWeight: 800, fontSize: 13, marginBottom: 8 }}>Configured provisional mapping</div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 8 }}>
                {rules.map((rule) => (
                  <div key={rule.condition} style={{ padding: 10, border: '1px solid #e2e8f0', borderRadius: 9, background: '#fff' }}>
                    <div style={{ fontWeight: 800, fontSize: 12 }}>{rule.condition}</div>
                    <div style={{ marginTop: 4, fontSize: 12 }}>{rule.scenario}</div>
                    <div style={{ marginTop: 2, fontSize: 11, color: '#64748b' }}>{rule.severity}</div>
                  </div>
                ))}
              </div>
            </div>

            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#fff7ed', border: '1px solid #fed7aa', color: '#9a3412', fontSize: 12 }}>
              <strong>Scientific limitation:</strong> Alert/Alarm/Critical levels are being mapped to reference 5/25/100-year hazard scenarios by project rule only. The mapping must not be presented as a calibrated current inundation prediction until validated against historical water levels and observed flood extents.
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
                  <div style={{ marginTop: 6, fontWeight: 800 }}>{automaticScenario.active ? automaticScenario.shortLabel : automaticScenario.shortLabel}</div>
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
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Users size={15} style={{ marginRight: 6 }} />Estimated affected population</span><strong>{formatNumber(affectedPopulation)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Baby size={15} style={{ marginRight: 6 }} />Estimated affected children</span><strong>{formatNumber(affectedChildren)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Building2 size={15} style={{ marginRight: 6 }} />Facilities in hazard area</span><strong>{formatNumber(facilities)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><AlertTriangle size={15} style={{ marginRight: 6 }} />Barangays with exposure</span><strong>{formatNumber(barangayCount)}</strong></div>
              {!activeScenario && <div style={{ marginTop: 5, fontSize: 12, color: '#64748b' }}>No automatic exposure estimate is active. Switch to Manual to inspect a reference scenario while conditions remain below Alert or telemetry is not Fresh.</div>}
            </div>
          </div>
        </div>
      </div>

      <div className="inundation-card" style={{ marginTop: 16 }}>
        <div className="card-header-bar"><div className="card-title-container"><Users size={18} className="text-brand" /><h2 className="card-heading">Barangay Exposure Estimate</h2></div></div>
        <div className="table-container" style={{ padding: 16 }}>
          <table className="data-table">
            <thead><tr><th>BARANGAY</th><th>HAZARD AREA</th><th>EST. AFFECTED POPULATION</th><th>EST. AFFECTED CHILDREN</th></tr></thead>
            <tbody>
              {barangays.map((row, index) => (
                <tr key={row.barangay_id || row.barangay_name || index}>
                  <td style={{ fontWeight: 600 }}>{row.barangay_name || row.name || 'Unnamed barangay'}</td>
                  <td>{numberFrom(row.selected_hazard_area_pct, row.hazard_area_pct, row.exposure_pct) == null ? '—' : `${numberFrom(row.selected_hazard_area_pct, row.hazard_area_pct, row.exposure_pct).toFixed(1)}%`}</td>
                  <td>{formatNumber(numberFrom(row.estimated_affected_population, row.population_exposed))}</td>
                  <td>{formatNumber(numberFrom(row.estimated_affected_children, row.children_exposed))}</td>
                </tr>
              ))}
              {!barangays.length && <tr><td colSpan="4" style={{ textAlign: 'center', padding: 30, color: '#64748b' }}>{loading ? 'Calculating exposure…' : exposureUnavailableMessage}</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
