import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  dailyTrend, distribution, summarizeUsers, summarizeResources,
  buildFloodStations, fetchDashboardInsights,
} from '../src/services/dashboardInsightsService.js';

const now = Date.parse('2026-11-23T07:00:00.000Z');
const iso = (delta) => new Date(now + delta).toISOString();

const query = (table, fixtures, calls) => ({
  select(columns) { this.columns = columns; return this; },
  eq(column, value) { this.filters = [...(this.filters || []), ['eq', column, value]]; return this; },
  gte(column, value) { this.filters = [...(this.filters || []), ['gte', column, value]]; return this; },
  lte(column, value) { this.filters = [...(this.filters || []), ['lte', column, value]]; return this; },
  order(column, options) { this.filters = [...(this.filters || []), ['order', column, options]]; return this; },
  limit(value) { this.limitCount = value; return this; },
  then(resolve, reject) { calls.push({ table, columns: this.columns, filters: this.filters || [] }); return Promise.resolve(fixtures[table] || { data: [], error: null }).then(resolve, reject); },
});
const client = (fixtures) => {
  const calls = [];
  return { calls, from(table) { return query(table, fixtures, calls); }, rpc(name, args) {
    calls.push({ rpc: name, args });
    return Promise.resolve(fixtures[name] || { data: [], error: null });
  } };
};

test('day trend uses real timestamps, keeps missing days at zero and ignores outside dates', () => {
  const trend = dailyTrend([
    { created_at: iso(-120_000) }, { created_at: iso(-120_000) },
    { created_at: iso(-8 * 86_400_000) }, { created_at: 'invalid' },
  ], 7, now);
  assert.equal(trend.length, 7);
  assert.equal(trend[6].count, 2);
  assert.equal(trend.reduce((sum, row) => sum + row.count, 0), 2);
});

test('status distribution groups only observed values, preserving missing status as Not specified', () => {
  const result = distribution([{ status: 'Pending' }, { status: 'Pending' }, { status: null }], 'status');
  assert.deepEqual(result, [{ name: 'Pending', value: 2 }, { name: 'Not specified', value: 1 }]);
});

test('user analytics excludes admins, does not claim active sessions and respects period', () => {
  const result = summarizeUsers([
    { role: 'admin', status: 'active', created_at: iso(-50_000) },
    { role: 'user', status: 'active', created_at: iso(-50_000) },
    { role: 'user', status: 'suspended', created_at: iso(-50 * 86_400_000) },
  ], 30, now);
  assert.equal(result.total, 2);
  assert.equal(result.activeAccounts, 1);
  assert.equal(result.suspendedAccounts, 1);
  assert.equal(result.newRegistrations, 1);
});

test('resources only aggregates real complete occupancy and never invents missing capacity', () => {
  const result = summarizeResources([
    { name: 'A', capacity: 100, current_evacuees: 20 },
    { name: 'B', capacity: 50, current_evacuees: null },
  ], [{ status: 'Active' }, { status: 'Archived' }]);
  assert.equal(result.totalCenters, 2);
  assert.equal(result.measuredCenters, 1);
  assert.equal(result.occupancy, 20);
  assert.equal(result.available, 80);
  assert.equal(result.activeAdvisories, 1);
});

test('flood stations use only enabled codes; archived gauge stays excluded', () => {
  const result = buildFloodStations([
    { station_code: '11104201', enabled: true, is_primary: true },
    { station_code: '11104204', enabled: false },
  ], [
    { station_code: '11104201', station_name: 'Sto. Niño (2nd)', level: 12.09, observed_at: iso(-600_000) },
    { station_code: '11104204', station_name: 'Archived', level: 11.88, observed_at: iso(-900_000) },
  ], now);
  assert.equal(result.length, 1);
  assert.equal(result[0].code, '11104201');
  assert.equal(result[0].freshness.label, 'Updated');
});

test('community tab loads only its own table and groups actual report records', async () => {
  const db = client({ community_reports: { data: [
    { id: 1, created_at: iso(-60_000), status: 'Pending', category: 'Flood' },
    { id: 2, created_at: iso(-60_000), status: 'Approved', category: 'Flood' },
  ], error: null } });
  const output = await fetchDashboardInsights(db, 'community', { days: 7, nowMs: now });
  assert.equal(output.reportsTotal, 2);
  assert.equal(output.pending, 1);
  assert.equal(output.categories[0].value, 2);
  assert.deepEqual(db.calls.map((call) => call.table), ['community_reports']);
});

test('users tab calls existing admin-only RPC and returns aggregate data', async () => {
  const db = client({ get_all_users_with_roles: { data: [{ role: 'user', status: 'active', created_at: iso(-60_000), email: 'private@example.com' }], error: null } });
  const output = await fetchDashboardInsights(db, 'users', { days: 30, nowMs: now });
  assert.equal(output.summary.total, 1);
  assert.equal(JSON.stringify(output).includes('private@example.com'), false);
  assert.deepEqual(db.calls.map((call) => call.rpc), ['get_all_users_with_roles']);
});

test('failed user RPC yields unavailable rather than fabricated zeros', async () => {
  const db = client({ get_all_users_with_roles: { data: null, error: { message: 'permission denied' } } });
  const output = await fetchDashboardInsights(db, 'users', { days: 30, nowMs: now });
  assert.equal(output.summary, null);
  assert.match(output.warnings[0], /permission denied/);
});

test('resources respect module permissions and do not query restricted tables', async () => {
  const db = client({ advisories: { data: [{ status: 'Active' }], error: null } });
  const output = await fetchDashboardInsights(db, 'resources', { allowCenters: false, allowAdvisories: true });
  assert.equal(output.summary.totalCenters, null);
  assert.equal(output.summary.activeAdvisories, 1);
  assert.deepEqual(db.calls.map((call) => call.table), ['advisories']);
});

test('missing flood catalog suppresses readings for potentially archived gauges', async () => {
  const db = client({
    station_catalog: { data: null, error: { message: 'denied' } },
    monitoring_stations: { data: [{ station_code: '11104204', level: 11.88 }], error: null },
  });
  const output = await fetchDashboardInsights(db, 'flood', { nowMs: now, days: 7 });
  assert.equal(output.stations.length, 0);
  assert.equal(output.history, null);
  assert.equal(db.calls.some((call) => call.rpc === 'pagasa_river_chart'), false);
});

test('flood history preserves missing daily values and selects only verified active gauge', async () => {
  const db = client({
    station_catalog: { data: [{ station_code: '11104201', enabled: true, is_primary: true }], error: null },
    monitoring_stations: { data: [{ station_code: '11104201', level: 12.09, observed_at: iso(-100_000) }], error: null },
    pagasa_river_chart: { data: [{ observed_at: iso(-DAY()), level: 12.5 }, { observed_at: iso(0), level: null }], error: null },
  });
  function DAY() { return 86_400_000; }
  const output = await fetchDashboardInsights(db, 'flood', { days: 7, nowMs: now });
  assert.equal(output.selectedStationCode, '11104201');
  assert.deepEqual(output.history.map((row) => row.level), [12.5, null]);
  assert.equal(db.calls.find((call) => call.rpc)?.args.p_station_code, '11104201');
});

test('dashboard tabs are code split and role-checked, not a new mock analytics route', () => {
  const source = readFileSync(new URL('../src/components/Dashboard.jsx', import.meta.url), 'utf8');
  assert.match(source, /React\.lazy\(\(\) => import\('\.\/DashboardInsightsPanel\.jsx'\)\)/);
  for (const tab of ['overview', 'flood', 'community', 'users', 'resources']) assert.match(source, new RegExp(`key: '${tab}'`));
  assert.match(source, /canView\(access, 'community-user-management'\)/);
  assert.match(source, /activeTab === 'overview' \? /);
  assert.doesNotMatch(source, /<Analytics\s*\/>/);
});
