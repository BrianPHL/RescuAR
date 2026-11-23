import { getTelemetryFreshness } from '../utils/telemetryFreshness.js';

const MS_HOUR = 60 * 60 * 1000;
export const INCIDENT_LOOKBACK_MS = 24 * MS_HOUR;
export const INCIDENT_BUCKET_MS = 4 * MS_HOUR;
export const REPORT_PAGE_LIMIT = 1000;
export const MAP_REPORT_LIMIT = 250;
const MAP_LOOKBACK_MS = 30 * 24 * MS_HOUR;
const PH_TIME_ZONE = 'Asia/Manila';

const safeDate = (value) => {
  const ms = Date.parse(value || '');
  return Number.isFinite(ms) ? ms : null;
};

const validCoordinate = (value, min, max) => {
  if (value === null || value === undefined || String(value).trim() === '') return null;
  const number = Number(value);
  return Number.isFinite(number) && number >= min && number <= max ? number : null;
};

const timestampLabel = (ms) => new Intl.DateTimeFormat('en-PH', {
  timeZone: PH_TIME_ZONE, month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false,
}).format(new Date(ms));

export function buildIncidentTrend(rows, nowMs) {
  const start = nowMs - INCIDENT_LOOKBACK_MS;
  const buckets = Array.from({ length: 6 }, (_, i) => ({
    time: timestampLabel(start + i * INCIDENT_BUCKET_MS), reports: 0,
  }));
  for (const report of rows) {
    const createdMs = safeDate(report.created_at);
    if (createdMs === null || createdMs < start || createdMs > nowMs) continue;
    const index = Math.floor((createdMs - start) / INCIDENT_BUCKET_MS);
    if (index >= 0 && index < buckets.length) buckets[index].reports += 1;
  }
  return buckets;
}

export function buildMapMarkers(reports, centers) {
  const markers = [];
  for (const report of reports) {
    if (!['Pending', 'Approved'].includes(report.status || 'Pending')) continue;
    const lat = validCoordinate(report.latitude, -90, 90);
    const lng = validCoordinate(report.longitude, -180, 180);
    if (lat === null || lng === null) continue;
    const category = String(report.category || '').toLowerCase();
    markers.push({
      id: `report:${report.id}`, position: [lat, lng],
      type: report.status === 'Approved' ? (category.includes('flood') ? 'flood' : 'report') : 'pending',
      label: report.title || report.category || 'Community incident',
      detail: `Community report · ${report.status || 'Pending'}${report.address ? ` · ${report.address}` : ''}`,
      observedAt: report.created_at || null,
    });
  }
  for (const center of centers) {
    const lat = validCoordinate(center.latitude, -90, 90);
    const lng = validCoordinate(center.longitude, -180, 180);
    if (lat === null || lng === null) continue;
    markers.push({
      id: `center:${center.id}`, position: [lat, lng], type: 'evac',
      label: center.name || 'Evacuation center',
      detail: `Evacuation center · ${center.status || 'Status unavailable'}`,
    });
  }
  return markers;
}

const validNonNegative = (input) => {
  if (input === null || input === undefined || String(input).trim() === '') return null;
  const number = Number(input);
  return Number.isFinite(number) && number >= 0 ? number : null;
};

export function buildCapacityChart(centers) {
  return centers.flatMap((center) => {
    const capacity = validNonNegative(center.capacity);
    const occupancy = validNonNegative(center.current_evacuees);
    if (capacity === null || occupancy === null) return [];
    return [{
      name: center.name || 'Unnamed center', available: Math.max(0, capacity - occupancy),
      used: occupancy, capacity,
    }];
  }).sort((a, b) => b.capacity - a.capacity).slice(0, 6);
}

export function buildRecentActivities(reports, advisories) {
  const reportEvents = reports.map((row) => ({
    id: `report:${row.id}`, text: `Community report: ${row.title || row.category || 'Untitled'}`,
    time: row.created_at, type: row.status === 'Pending' ? 'medium' : 'info',
    source: 'community report',
  }));
  const advisoryEvents = advisories.map((row) => ({
    id: `advisory:${row.id}`, text: `Advisory created: ${row.title || 'Untitled'}`,
    time: row.created_at, type: 'info', source: 'advisory',
  }));
  return [...reportEvents, ...advisoryEvents]
    .filter((entry) => safeDate(entry.time) !== null)
    .sort((a, b) => safeDate(b.time) - safeDate(a.time))
    .slice(0, 8);
}

export function countReportingStations(stations, catalog, nowMs) {
  if (!Array.isArray(catalog) || catalog.length === 0) return null;
  const active = new Set(catalog.filter((row) => row.enabled === true)
    .map((row) => String(row.station_code || '').trim()).filter(Boolean));
  const unique = new Map(stations.filter((row) => active.has(String(row.station_code || '').trim()))
    .map((row) => [String(row.station_code).trim(), row]));
  return {
    healthy: [...unique.values()].filter((row) => {
      const level = validNonNegative(row.level);
      return level !== null && getTelemetryFreshness(row.observed_at || row.updated_at, nowMs).isUsableAsLive;
    }).length,
    total: active.size,
  };
}

function makeQuery(client, table, selectColumns) {
  return client.from(table).select(selectColumns);
}

// Individual read failures stay visible as unavailable, never fall back to invented values.
export async function fetchDashboardData(client, nowMs = Date.now()) {
  const since24 = new Date(nowMs - INCIDENT_LOOKBACK_MS).toISOString();
  const since30 = new Date(nowMs - MAP_LOOKBACK_MS).toISOString();
  const queries = {
    advisoryCount: client.from('advisories').select('id', { count: 'exact', head: true }).eq('status', 'Active'),
    pendingCount: client.from('community_reports').select('id', { count: 'exact', head: true }).or('status.eq.Pending,status.is.null'),
    incidentReports: makeQuery(client, 'community_reports', 'id,created_at')
      .gte('created_at', since24).lte('created_at', new Date(nowMs).toISOString())
      .order('created_at', { ascending: false }).limit(REPORT_PAGE_LIMIT),
    mapReports: makeQuery(client, 'community_reports', 'id,title,category,status,address,latitude,longitude,created_at')
      .gte('created_at', since30).or('status.eq.Pending,status.eq.Approved,status.is.null')
      .order('created_at', { ascending: false }).limit(MAP_REPORT_LIMIT),
    latestReports: makeQuery(client, 'community_reports', 'id,title,category,status,created_at')
      .order('created_at', { ascending: false }).limit(12),
    latestAdvisories: makeQuery(client, 'advisories', 'id,title,status,created_at')
      .order('created_at', { ascending: false }).limit(12),
    centers: makeQuery(client, 'evacuation_centers', 'id,name,latitude,longitude,status,capacity,current_evacuees')
      .order('name', { ascending: true }).limit(500),
    stations: makeQuery(client, 'monitoring_stations', 'station_code,level,observed_at,updated_at')
      .eq('source', 'PAGASA_FFWWS').limit(300),
    catalog: makeQuery(client, 'station_catalog', 'station_code,enabled')
      .eq('source', 'PAGASA_FFWWS').limit(300),
  };
  // These are PostgREST builders (thenables), so await each without letting one failure hide other data.
  const entries = await Promise.all(Object.entries(queries).map(async ([key, query]) => {
    try {
      const response = await query;
      return [key, response?.error ? { ok: false, error: response.error.message || 'Query failed' }
        : { ok: true, data: response?.data ?? [], count: response?.count }];
    } catch (error) {
      return [key, { ok: false, error: error?.message || 'Connection failed' }];
    }
  }));
  const result = Object.fromEntries(entries);
  const read = (key) => result[key].ok ? result[key].data : [];
  const warnings = Object.entries(result)
    .filter(([, response]) => !response.ok)
    .map(([key, response]) => `${key}: ${response.error}`);
  const mapMarkers = buildMapMarkers(read('mapReports'), read('centers'));
  const resourceData = result.centers.ok ? buildCapacityChart(read('centers')) : null;
  const incidents = result.incidentReports.ok ? buildIncidentTrend(read('incidentReports'), nowMs) : null;
  const stations = result.stations.ok && result.catalog.ok
    ? countReportingStations(read('stations'), read('catalog'), nowMs) : null;
  if (result.catalog.ok && result.stations.ok && stations === null) {
    warnings.push('No verified active station catalog; reporting-stations metric is unavailable.');
  }
  const truncated = result.incidentReports.ok && read('incidentReports').length >= REPORT_PAGE_LIMIT;
  if (truncated) warnings.push('Incident chart shows at most the most recent 1,000 reports in the last 24 hours.');
  if (result.mapReports.ok && read('mapReports').length >= MAP_REPORT_LIMIT) {
    warnings.push('Map report pins are limited to the latest 250 records from the last 30 days.');
  }
  if (result.centers.ok && read('centers').length >= 500) {
    warnings.push('Map and capacity data are limited to the first 500 evacuation centers.');
  }
  const reportActivityOk = result.latestReports.ok;
  const advisoryActivityOk = result.latestAdvisories.ok;
  return {
    metrics: {
      activeAdvisories: result.advisoryCount.ok ? result.advisoryCount.count : null,
      pendingReports: result.pendingCount.ok ? result.pendingCount.count : null,
      reportingStations: stations,
    },
    incidents,
    incidentTruncated: Boolean(truncated),
    mapMarkers: result.mapReports.ok || result.centers.ok ? mapMarkers : null,
    resourceData,
    activities: reportActivityOk || advisoryActivityOk
      ? buildRecentActivities(read('latestReports'), read('latestAdvisories')) : null,
    warnings,
    successfulQueries: Object.values(result).filter((response) => response.ok).length,
    totalQueries: Object.keys(queries).length,
    fetchedAt: new Date().toISOString(),
  };
}

export function formatDashboardTimestamp(value) {
  const ms = safeDate(value);
  return ms === null ? 'Unknown time' : timestampLabel(ms);
}
