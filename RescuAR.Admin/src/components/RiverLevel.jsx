import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { supabase } from '../supabaseClient';
import { calculateAlertStatus, getStationThresholds } from '../utils/waterLevelUtils';
import { getTelemetryFreshness } from '../utils/telemetryFreshness';
import { RefreshCw, Waves, Clock3, Database, AlertTriangle } from 'lucide-react';
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine
} from 'recharts';

const DEFAULT_STATION = 'Sto. Niño Station';
const HISTORY_HOURS = 24;

function formatObservedTime(value) {
  if (!value) return '--';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '--';
  return date.toLocaleString('en-PH', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

function chartTime(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString('en-PH', {
    hour: '2-digit',
    minute: '2-digit'
  });
}

export default function RiverLevel() {
  const [stationName, setStationName] = useState(DEFAULT_STATION);
  const [stations, setStations] = useState([]);
  const [latest, setLatest] = useState(null);
  const [history, setHistory] = useState([]);
  const [loading, setLoading] = useState(true);
  const [historyError, setHistoryError] = useState('');
  const [nowMs, setNowMs] = useState(Date.now());

  const fetchTelemetry = useCallback(async () => {
    setLoading(true);
    setHistoryError('');

    const since = new Date(Date.now() - HISTORY_HOURS * 60 * 60 * 1000).toISOString();

    try {
      const [latestResult, historyResult] = await Promise.all([
        supabase
          .from('monitoring_stations')
          .select('station_name, level, status, updated_at')
          .order('station_name', { ascending: true }),
        supabase
          .from('river_level_history')
          .select('station_name, level, status, source, observed_at')
          .eq('station_name', stationName)
          .gte('observed_at', since)
          .order('observed_at', { ascending: true })
      ]);

      if (latestResult.error) {
        console.error('Failed to load latest monitoring stations:', latestResult.error.message);
      } else {
        const rows = latestResult.data || [];
        setStations(rows.map(row => row.station_name).filter(Boolean));
        setLatest(rows.find(row => row.station_name === stationName) || null);
      }

      if (historyResult.error) {
        console.error('Failed to load river telemetry history:', historyResult.error.message);
        setHistory([]);
        setHistoryError(
          historyResult.error.message.includes('river_level_history')
            ? 'Historical telemetry is not available yet. Run the supplied Supabase migration and deploy the updated scraper.'
            : `Historical telemetry could not be loaded: ${historyResult.error.message}`
        );
      } else {
        setHistory(historyResult.data || []);
      }
    } catch (error) {
      console.error('Unexpected telemetry error:', error);
      setHistoryError('Historical telemetry could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [stationName]);

  useEffect(() => {
    fetchTelemetry();

    const ageTimer = setInterval(() => setNowMs(Date.now()), 60 * 1000);

    const latestChannel = supabase
      .channel(`river-level-latest-${stationName}`)
      .on('postgres_changes', { event: '*', schema: 'public', table: 'monitoring_stations' }, fetchTelemetry)
      .subscribe();

    const historyChannel = supabase
      .channel(`river-level-history-${stationName}`)
      .on('postgres_changes', { event: 'INSERT', schema: 'public', table: 'river_level_history' }, payload => {
        if (payload.new?.station_name === stationName) fetchTelemetry();
      })
      .subscribe();

    return () => {
      clearInterval(ageTimer);
      supabase.removeChannel(latestChannel);
      supabase.removeChannel(historyChannel);
    };
  }, [fetchTelemetry, stationName]);

  const freshness = getTelemetryFreshness(latest?.updated_at, nowMs);
  const numericLevel = latest?.level !== null && Number.isFinite(Number(latest?.level))
    ? Number(latest.level)
    : null;
  const alertInfo = numericLevel === null
    ? { label: 'Unavailable', color: '#b91c1c' }
    : calculateAlertStatus(numericLevel, stationName);
  const thresholds = getStationThresholds(stationName);

  const chartData = useMemo(() => history.map(row => ({
    observedAt: row.observed_at,
    time: chartTime(row.observed_at),
    level: Number(row.level),
    status: row.status,
    source: row.source
  })).filter(row => Number.isFinite(row.level)), [history]);

  const minLevel = chartData.length ? Math.min(...chartData.map(row => row.level), thresholds.ALARM_1) : 0;
  const maxLevel = chartData.length ? Math.max(...chartData.map(row => row.level), thresholds.ALARM_3) : thresholds.ALARM_3;
  const yMin = Math.max(0, Math.floor(minLevel - 1));
  const yMax = Math.ceil(maxLevel + 1);

  return (
    <main className="main-content" style={{ paddingBottom: 32 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap', marginBottom: 20 }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 24, display: 'flex', alignItems: 'center', gap: 10 }}>
            <Waves size={24} /> River Level History
          </h1>
          <p style={{ margin: '6px 0 0', color: 'var(--text-muted)' }}>
            Verified timestamped measurements from the telemetry scraper. No synthetic observed or forecast values are plotted.
          </p>
        </div>

        <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
          <select
            value={stationName}
            onChange={event => setStationName(event.target.value)}
            style={{ padding: '9px 12px', border: '1px solid var(--color-border)', borderRadius: 8, background: '#fff' }}
          >
            {(stations.length ? stations : [DEFAULT_STATION]).map(name => (
              <option key={name} value={name}>{name}</option>
            ))}
          </select>
          <button
            type="button"
            onClick={fetchTelemetry}
            disabled={loading}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 7, padding: '9px 12px', border: '1px solid var(--color-border)', borderRadius: 8, background: '#fff', cursor: 'pointer' }}
          >
            <RefreshCw size={15} className={loading ? 'spin' : ''} /> Refresh
          </button>
        </div>
      </div>

      <section style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 14, marginBottom: 18 }}>
        <div className="card" style={{ padding: 18 }}>
          <div style={{ color: 'var(--text-muted)', fontSize: 12, marginBottom: 8 }}>LATEST VERIFIED LEVEL</div>
          <div style={{ fontSize: 30, fontWeight: 700 }}>{numericLevel === null ? '--' : `${numericLevel.toFixed(2)} m`}</div>
          <div style={{ marginTop: 7, color: alertInfo.color, fontWeight: 600 }}>{alertInfo.label}</div>
        </div>

        <div className="card" style={{ padding: 18 }}>
          <div style={{ color: 'var(--text-muted)', fontSize: 12, marginBottom: 8 }}>TELEMETRY FRESHNESS</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 20, fontWeight: 700, color: freshness.color }}>
            <span style={{ width: 9, height: 9, borderRadius: '50%', background: freshness.dotColor }} />
            {freshness.label}
          </div>
          <div style={{ marginTop: 8, color: 'var(--text-muted)', fontSize: 13 }}>{freshness.ageText}</div>
        </div>

        <div className="card" style={{ padding: 18 }}>
          <div style={{ color: 'var(--text-muted)', fontSize: 12, marginBottom: 8 }}>LAST OBSERVED</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 600 }}>
            <Clock3 size={16} /> {formatObservedTime(latest?.updated_at)}
          </div>
          <div style={{ marginTop: 8, color: 'var(--text-muted)', fontSize: 13 }}>Based on monitoring_stations.updated_at</div>
        </div>

        <div className="card" style={{ padding: 18 }}>
          <div style={{ color: 'var(--text-muted)', fontSize: 12, marginBottom: 8 }}>24-HOUR OBSERVATIONS</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 26, fontWeight: 700 }}>
            <Database size={20} /> {chartData.length}
          </div>
          <div style={{ marginTop: 8, color: 'var(--text-muted)', fontSize: 13 }}>Stored measurements, not generated points</div>
        </div>
      </section>

      <section className="card" style={{ padding: 18 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'center', flexWrap: 'wrap', marginBottom: 16 }}>
          <div>
            <h2 style={{ margin: 0, fontSize: 17 }}>Historical water level — last {HISTORY_HOURS} hours</h2>
            <p style={{ margin: '5px 0 0', color: 'var(--text-muted)', fontSize: 13 }}>
              Each point is one row from <code>river_level_history</code>.
            </p>
          </div>
        </div>

        {historyError ? (
          <div style={{ display: 'flex', gap: 10, padding: 16, border: '1px solid #fecaca', background: '#fef2f2', color: '#991b1b', borderRadius: 8 }}>
            <AlertTriangle size={18} style={{ flex: '0 0 auto' }} />
            <span>{historyError}</span>
          </div>
        ) : chartData.length === 0 ? (
          <div style={{ padding: 28, textAlign: 'center', color: 'var(--text-muted)' }}>
            No historical observations have been stored for this station in the last {HISTORY_HOURS} hours yet.
          </div>
        ) : (
          <div style={{ width: '100%', height: 410 }}>
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData} margin={{ top: 10, right: 25, left: 5, bottom: 10 }}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="time" minTickGap={28} tick={{ fontSize: 11 }} />
                <YAxis domain={[yMin, yMax]} unit=" m" tick={{ fontSize: 11 }} />
                <Tooltip
                  labelFormatter={(_, payload) => payload?.[0]?.payload?.observedAt ? formatObservedTime(payload[0].payload.observedAt) : ''}
                  formatter={(value, name, item) => [`${Number(value).toFixed(2)} m`, `${item?.payload?.source || 'Verified source'} · ${item?.payload?.status || ''}`]}
                />
                <ReferenceLine y={thresholds.ALARM_1} strokeDasharray="4 4" label={{ value: '1st Alarm', position: 'insideTopRight', fontSize: 10 }} />
                <ReferenceLine y={thresholds.ALARM_2} strokeDasharray="4 4" label={{ value: '2nd Alarm', position: 'insideTopRight', fontSize: 10 }} />
                <ReferenceLine y={thresholds.ALARM_3} strokeDasharray="4 4" label={{ value: '3rd Alarm', position: 'insideTopRight', fontSize: 10 }} />
                <Line type="monotone" dataKey="level" name="Observed level" strokeWidth={2.5} dot={false} activeDot={{ r: 4 }} connectNulls={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </section>
    </main>
  );
}
