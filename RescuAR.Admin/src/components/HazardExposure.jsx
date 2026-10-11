import React, { useEffect, useMemo, useState } from 'react';
import { RefreshCw, Activity, AlertTriangle, Users, Building2, Baby, Waves, ShieldAlert } from 'lucide-react';
import { supabase } from '../supabaseClient';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import { calculateAlertStatus } from '../utils/waterLevelUtils';
import { fetchExposureScenario, PREP_EXPOSURE_API_URL } from '../services/prepExposureApi';

const SOURCE_ID = 'PAGASA_FFWWS';
const STO_NINO_CODE = '11104204';

function numberFrom(...values) {
  for (const value of values) {
    const number = Number(value);
    if (Number.isFinite(number)) return number;
  }
  return null;
}

function formatNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) ? Math.round(number).toLocaleString('en-PH') : 'Unavailable';
}

function formatObservedAt(value) {
  if (!value) return 'No verified PAGASA observation';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Invalid timestamp' : date.toLocaleString('en-PH', {
    month: 'long', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit'
  });
}

export default function HazardExposure() {
  const [returnPeriod, setReturnPeriod] = useState(25);
  const [minimumHazard, setMinimumHazard] = useState('Low');
  const [liveTelemetry, setLiveTelemetry] = useState(null);
  const [sourceStatus, setSourceStatus] = useState(null);
  const [exposure, setExposure] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);
  const [nowMs, setNowMs] = useState(Date.now());

  const loadTelemetryContext = async () => {
    const [{ data: stationRows }, { data: sourceRows }] = await Promise.all([
      supabase.from('monitoring_stations').select('*').eq('source', SOURCE_ID).eq('station_code', STO_NINO_CODE).limit(1),
      supabase.from('telemetry_source_status').select('*').eq('source', SOURCE_ID).limit(1)
    ]);
    setLiveTelemetry(stationRows?.[0] || null);
    setSourceStatus(sourceRows?.[0] || null);
    setNowMs(Date.now());
  };

  useEffect(() => {
    void loadTelemetryContext();
    const timer = setInterval(() => setNowMs(Date.now()), 60 * 1000);
    const channel = supabase
      .channel('exposure-live-context')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, () => void loadTelemetryContext())
      .on('postgres_changes', { event: '*', schema: 'public', table: 'telemetry_source_status' }, () => void loadTelemetryContext())
      .subscribe();
    return () => {
      clearInterval(timer);
      supabase.removeChannel(channel);
    };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError('');
    fetchExposureScenario({ returnPeriod, minimumHazard, signal: controller.signal })
      .then(setExposure)
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
  }, [returnPeriod, minimumHazard, refreshKey]);

  const freshness = getTelemetryFreshness(liveTelemetry?.observed_at || liveTelemetry?.updated_at, nowMs);
  const liveLevel = numberFrom(liveTelemetry?.level);
  const alert = calculateAlertStatus(liveLevel, liveTelemetry?.station_name, liveTelemetry);

  const summary = exposure?.summary || exposure?.city_summary || exposure?.exposure || exposure || {};
  const affectedPopulation = numberFrom(summary.estimated_affected_population, summary.population_exposed, summary.total_population_affected);
  const affectedChildren = numberFrom(summary.estimated_affected_children, summary.children_exposed, summary.total_children_affected);
  const facilities = numberFrom(summary.facilities_exposed, summary.facilities_at_risk, summary.exposed_facilities);
  const barangayCount = numberFrom(summary.affected_barangays, summary.barangays_with_exposure, Array.isArray(exposure?.barangays) ? exposure.barangays.length : null);
  const barangays = useMemo(() => Array.isArray(exposure?.barangays) ? exposure.barangays : Array.isArray(exposure?.results) ? exposure.results : [], [exposure]);

  return (
    <div className="inundation-container">
      <div className="inundation-header">
        <div className="title-group">
          <div className="title-row">
            <h1 className="inundation-title">Flood Hazard Exposure</h1>
            <span className="live-badge"><span className="pulse-dot"></span>Reference Exposure Model</span>
          </div>
          <p className="inundation-subtitle">Estimates potentially exposed population using UP DREAM/LiPAD hazard scenarios and demographic/spatial data. Live PAGASA telemetry is displayed as hydrological context and is not converted directly into a flood extent.</p>
        </div>
        <button className="refresh-btn" onClick={() => { void loadTelemetryContext(); setRefreshKey((key) => key + 1); }}><RefreshCw size={15} /><span>Refresh View</span></button>
      </div>

      <div className="inundation-grid">
        <div className="inundation-card map-card" style={{ minHeight: 520 }}>
          <div className="card-header-bar"><div className="card-title-container"><Waves size={18} className="text-brand" /><h2 className="card-heading">Current PAGASA Hydrological Context</h2></div></div>
          <div style={{ padding: 22 }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 16 }}>
              <div style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}><div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>STO. NIÑO (MMDA)</div><div style={{ fontSize: 34, fontWeight: 800, marginTop: 5 }}>{liveLevel == null ? '--' : `${liveLevel.toFixed(2)} m`}</div><div style={{ marginTop: 8, color: alert.color, fontWeight: 700 }}>{alert.label}</div></div>
              <div style={{ padding: 18, border: '1px solid #e2e8f0', borderRadius: 12 }}><div style={{ fontSize: 12, color: '#64748b', fontWeight: 700 }}>OBSERVATION FRESHNESS</div><div style={{ fontSize: 22, fontWeight: 800, marginTop: 8, color: freshness.color }}>{freshness.label}</div><div style={{ marginTop: 6, color: '#64748b' }}>{freshness.ageText}</div></div>
            </div>
            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#f8fafc', border: '1px solid #e2e8f0', fontSize: 13, color: '#475569' }}>
              <strong>Last verified observation:</strong> {formatObservedAt(liveTelemetry?.observed_at || liveTelemetry?.updated_at)}<br />
              <strong>PAGASA source:</strong> {sourceStatus?.source_status || 'unknown'} • <strong>Worker:</strong> {sourceStatus?.worker_status || 'unknown'}
            </div>
            <div style={{ marginTop: 18, padding: 14, borderRadius: 10, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e40af', fontSize: 13 }}>
              <strong>Interpretation:</strong> the live river condition does not directly determine the population number below. Exposure is calculated from the selected reference flood-hazard layer and severity threshold.
            </div>
          </div>
        </div>

        <div className="right-panel-stack">
          <div className="inundation-card input-card">
            <div className="card-header-bar"><div className="card-title-container"><ShieldAlert size={18} className="text-brand" /><h2 className="card-heading">Reference Hazard Scenario</h2></div></div>
            <div style={{ padding: '16px 20px' }}>
              <label className="slider-label">Return period</label>
              <select value={returnPeriod} onChange={(e) => setReturnPeriod(Number(e.target.value))} style={{ width: '100%', padding: 10, border: '1px solid #cbd5e1', borderRadius: 8, marginBottom: 16 }}>
                <option value={5}>5-Year Flood (~20% annual exceedance probability)</option>
                <option value={25}>25-Year Flood (~4% annual exceedance probability)</option>
                <option value={100}>100-Year Flood (~1% annual exceedance probability)</option>
              </select>
              <label className="slider-label">Minimum flood severity counted as affected</label>
              <select value={minimumHazard} onChange={(e) => setMinimumHazard(e.target.value)} style={{ width: '100%', padding: 10, border: '1px solid #cbd5e1', borderRadius: 8 }}>
                <option value="Low">Low or worse (0.1 m+)</option>
                <option value="Medium">Medium or worse (0.5 m+)</option>
                <option value="High">High only (&gt;1.5 m)</option>
              </select>
              <div className={`inundation-mode-note ${error ? 'simulation' : 'live'}`} style={{ marginTop: 16 }}>
                {loading ? 'PREP R API: calculating hazard exposure…' : error ? error : `PREP R API connected${PREP_EXPOSURE_API_URL ? ` • ${PREP_EXPOSURE_API_URL}` : ''}`}
              </div>
            </div>
          </div>

          <div className="inundation-card summary-card">
            <div className="card-header-bar"><div className="card-title-container"><Activity size={18} className="text-brand" /><h2 className="card-heading">Exposure Summary</h2></div></div>
            <div style={{ padding: 18, display: 'grid', gap: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Users size={15} style={{ marginRight: 6 }} />Estimated affected population</span><strong>{formatNumber(affectedPopulation)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Baby size={15} style={{ marginRight: 6 }} />Estimated affected children</span><strong>{formatNumber(affectedChildren)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><Building2 size={15} style={{ marginRight: 6 }} />Facilities in hazard area</span><strong>{formatNumber(facilities)}</strong></div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}><span><AlertTriangle size={15} style={{ marginRight: 6 }} />Barangays with exposure</span><strong>{formatNumber(barangayCount)}</strong></div>
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
              {!barangays.length && <tr><td colSpan="4" style={{ textAlign: 'center', padding: 30, color: '#64748b' }}>{loading ? 'Calculating exposure…' : error ? 'Exposure service unavailable.' : 'No barangay rows were returned by the exposure service.'}</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
