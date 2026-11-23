import { getTelemetryFreshness } from '../utils/telemetryFreshness.js';
import { buildCapacityChart } from './dashboardDataService.js';

const DAY_MS = 86_400_000;
const ROW_LIMIT = 1000;
const PH_DATE_PARTS = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit',
});
const isoDate = (value) => {
  const ms = new Date(value).getTime();
  if (!Number.isFinite(ms)) return null;
  const parts = Object.fromEntries(PH_DATE_PARTS.formatToParts(new Date(ms))
    .filter(({ type }) => type !== 'literal').map(({ type, value: part }) => [type, part]));
  return `${parts.year}-${parts.month}-${parts.day}`;
};
const asFinite = (value) => value === null || value === undefined || String(value).trim() === ''
  ? null : (Number.isFinite(Number(value)) ? Number(value) : null);
const nonnegative = (value) => { const n = asFinite(value); return n !== null && n >= 0 ? n : null; };
const safeError = (error) => error?.message || 'Source could not be read';
const success = (response) => !response?.error;
const dateSince = (nowMs, days) => new Date(nowMs - days * DAY_MS).toISOString();

export function dailyTrend(rows, days, nowMs = Date.now()) {
  const window = Math.max(1, Math.min(180, Math.floor(days)));
  const daysList = Array.from({ length: window }, (_, index) => ({
    date: isoDate(nowMs - (window - index - 1) * DAY_MS), count: 0,
  }));
  const indexByDate = new Map(daysList.map((entry) => [entry.date, entry]));
  for (const row of rows || []) {
    const point = indexByDate.get(isoDate(row.created_at));
    if (point) point.count += 1;
  }
  return daysList;
}

export function distribution(rows, field, maxGroups = 6) {
  const totals = new Map();
  for (const row of rows || []) {
    const label = String(row?.[field] ?? '').trim() || 'Not specified';
    totals.set(label, (totals.get(label) || 0) + 1);
  }
  const ordered = [...totals].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
  if (ordered.length <= maxGroups) return ordered.map(([name, value]) => ({ name, value }));
  return [
    ...ordered.slice(0, maxGroups - 1).map(([name, value]) => ({ name, value })),
    { name: 'Other', value: ordered.slice(maxGroups - 1).reduce((sum, [, value]) => sum + value, 0) },
  ];
}

export function summarizeUsers(rows, days = 30, nowMs = Date.now()) {
  const regularUsers = (rows || []).filter((row) => String(row.role || 'user').toLowerCase() === 'user');
  const active = regularUsers.filter((row) => String(row.status || '').toLowerCase() === 'active').length;
  const suspended = regularUsers.filter((row) => String(row.status || '').toLowerCase() === 'suspended').length;
  return {
    total: regularUsers.length,
    activeAccounts: active,
    suspendedAccounts: suspended,
    newRegistrations: regularUsers.filter((row) => {
      const ms = Date.parse(row.created_at || '');
      return Number.isFinite(ms) && ms >= nowMs - days * DAY_MS && ms <= nowMs;
    }).length,
    registrations: dailyTrend(regularUsers, days, nowMs),
    statuses: distribution(regularUsers, 'status'),
  };
}

export function summarizeResources(centers, advisories) {
  const valid = (centers || []).filter((center) =>
    nonnegative(center.capacity) !== null && nonnegative(center.current_evacuees) !== null);
  const capacity = valid.reduce((sum, center) => sum + nonnegative(center.capacity), 0);
  const occupancy = valid.reduce((sum, center) => sum + nonnegative(center.current_evacuees), 0);
  return {
    totalCenters: centers?.length ?? null,
    measuredCenters: valid.length,
    capacity: valid.length ? capacity : null,
    occupancy: valid.length ? occupancy : null,
    // Exceeding capacity is not converted into negative available spaces.
    available: valid.length ? valid.reduce((sum, center) =>
      sum + Math.max(0, nonnegative(center.capacity) - nonnegative(center.current_evacuees)), 0) : null,
    centers: centers ? buildCapacityChart(centers) : null,
    advisories: advisories ? distribution(advisories, 'status') : null,
    activeAdvisories: advisories ? advisories.filter((a) => a.status === 'Active').length : null,
  };
}

export function buildFloodStations(catalog, stations, nowMs = Date.now()) {
  const enabled = new Map((catalog || []).filter((row) => row.enabled === true)
    .map((row) => [String(row.station_code).trim(), row]));
  return (stations || []).filter((row) => enabled.has(String(row.station_code).trim()))
    .map((row) => ({
      code: String(row.station_code).trim(),
      name: row.station_name || String(row.station_code),
      level: asFinite(row.level),
      observedAt: row.observed_at || null,
      freshness: getTelemetryFreshness(row.observed_at, nowMs),
      primary: enabled.get(String(row.station_code).trim())?.is_primary === true,
    })).sort((a, b) => Number(b.primary) - Number(a.primary) || a.name.localeCompare(b.name));
}

async function read(query, source, warnings) {
  try {
    const result = await query;
    if (!success(result)) {
      warnings.push(`${source}: ${safeError(result.error)}`);
      return null;
    }
    return result.data || [];
  } catch (error) {
    warnings.push(`${source}: ${safeError(error)}`);
    return null;
  }
}

async function floodInsights(client, { days, stationCode, nowMs }) {
  const warnings = [];
  const [catalog, observations] = await Promise.all([
    read(client.from('station_catalog').select('station_code,enabled,is_primary')
      .eq('source', 'PAGASA_FFWWS').limit(300), 'Station catalog', warnings),
    read(client.from('monitoring_stations').select('station_code,station_name,level,observed_at')
      .eq('source', 'PAGASA_FFWWS').limit(300), 'Station observations', warnings),
  ]);
  // A missing catalog must not silently reactivate archived stations.
  const stations = catalog && observations ? buildFloodStations(catalog, observations, nowMs) : [];
  const selected = stations.find((station) => station.code === stationCode) || stations[0] || null;
  let history = null;
  if (selected) {
    const bucket = days <= 1 ? 'hour' : 'day';
    const rows = await read(client.rpc('pagasa_river_chart', {
      p_station_code: selected.code,
      p_since: dateSince(nowMs, days),
      p_until: new Date(nowMs).toISOString(),
      p_bucket: bucket,
    }), 'PAGASA historical chart', warnings);
    history = rows?.map((row) => ({
      time: row.observed_at,
      label: days <= 1 ? new Intl.DateTimeFormat('en-PH', { timeZone: 'Asia/Manila', hour: '2-digit', minute: '2-digit' }).format(new Date(row.observed_at)) : isoDate(row.observed_at),
      level: asFinite(row.level),
    })) ?? null;
  }
  return { stations, selectedStationCode: selected?.code || '', history, warnings, fetchedAt: new Date().toISOString() };
}

async function communityInsights(client, { days, nowMs }) {
  const warnings = [];
  const reports = await read(client.from('community_reports')
    .select('id,created_at,status,category')
    .gte('created_at', dateSince(nowMs, days))
    .lte('created_at', new Date(nowMs).toISOString())
    .order('created_at', { ascending: false }).limit(ROW_LIMIT), 'Community reports', warnings);
  if (reports?.length === ROW_LIMIT) warnings.push(`Community analytics is limited to the latest ${ROW_LIMIT} reports for this period; counts may be incomplete.`);
  return {
    reportsTotal: reports?.length ?? null,
    pending: reports ? reports.filter((row) => !row.status || String(row.status).toLowerCase() === 'pending').length : null,
    trend: reports ? dailyTrend(reports, days, nowMs) : null,
    statuses: reports ? distribution(reports, 'status') : null,
    categories: reports ? distribution(reports, 'category') : null,
    warnings, fetchedAt: new Date().toISOString(),
  };
}

async function userInsights(client, { days, nowMs }) {
  const warnings = [];
  let rows = null;
  try {
    const result = await client.rpc('get_all_users_with_roles');
    if (result.error) warnings.push(`User records: ${safeError(result.error)}`);
    else if (Array.isArray(result.data)) rows = result.data;
    else warnings.push('User records: unexpected response format.');
  } catch (error) {
    warnings.push(`User records: ${safeError(error)}`);
  }
  // Only aggregated fields leave this function. The UI never renders user PII.
  return { summary: rows ? summarizeUsers(rows, days, nowMs) : null, warnings, fetchedAt: new Date().toISOString() };
}

async function resourceInsights(client, { allowCenters, allowAdvisories }) {
  const warnings = [];
  const [centers, advisories] = await Promise.all([
    allowCenters ? read(client.from('evacuation_centers')
      .select('id,name,status,capacity,current_evacuees')
      .order('name', { ascending: true }).limit(500), 'Evacuation centers', warnings) : Promise.resolve(null),
    allowAdvisories ? read(client.from('advisories')
      .select('id,status,category,severity,created_at')
      .order('created_at', { ascending: false }).limit(ROW_LIMIT), 'Advisories', warnings) : Promise.resolve(null),
  ]);
  if (centers?.length === 500) warnings.push('Center figures are limited to the first 500 records.');
  if (advisories?.length === ROW_LIMIT) warnings.push('Advisory figures are limited to the latest 1,000 records.');
  return { summary: summarizeResources(centers, advisories), warnings, fetchedAt: new Date().toISOString() };
}

export async function fetchDashboardInsights(client, tab, options = {}) {
  const days = [1, 7, 30, 90, 180].includes(Number(options.days)) ? Number(options.days) : 30;
  const nowMs = options.nowMs ?? Date.now();
  if (tab === 'flood') return floodInsights(client, { days, stationCode: options.stationCode, nowMs });
  if (tab === 'community') return communityInsights(client, { days, nowMs });
  if (tab === 'users') return userInsights(client, { days, nowMs });
  if (tab === 'resources') return resourceInsights(client, options);
  throw new Error('Unknown analytics tab');
}
