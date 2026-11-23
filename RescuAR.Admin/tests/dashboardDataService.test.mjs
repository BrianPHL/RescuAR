import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildIncidentTrend, buildMapMarkers, buildCapacityChart, buildRecentActivities,
  countReportingStations, fetchDashboardData, INCIDENT_LOOKBACK_MS,
} from '../src/services/dashboardDataService.js';

const NOW = Date.parse('2026-11-23T05:00:00.000Z');
const ISO = (ms) => new Date(ms).toISOString();

test('24-hour chart counts only actual report creation times and respects bucket edges', () => {
  const rows = [
    { created_at: ISO(NOW - INCIDENT_LOOKBACK_MS) },
    { created_at: ISO(NOW - 20 * 60 * 60 * 1000) },
    { created_at: ISO(NOW - 1) },
    { created_at: ISO(NOW - INCIDENT_LOOKBACK_MS - 1) },
    { created_at: ISO(NOW + 1) },
    { created_at: 'bad timestamp' },
  ];
  const trend = buildIncidentTrend(rows, NOW);
  assert.equal(trend.length, 6);
  assert.deepEqual(trend.map((bucket) => bucket.reports), [1, 1, 0, 0, 0, 1]);
});

test('map does not invent points or treat rejected reports as hazards', () => {
  const markers = buildMapMarkers([
    { id: '1', title: 'Flooding', category: 'Flood', status: 'Approved', latitude: '14.65', longitude: '121.09' },
    { id: '2', title: 'Other event', category: 'Road', status: 'Pending', latitude: 14.65, longitude: 121.10 },
    { id: '3', status: 'Rejected', latitude: 14.60, longitude: 121.01 },
    { id: '4', status: 'Approved', latitude: '', longitude: 121.09 },
    { id: '5', status: 'Approved', latitude: 999, longitude: 121.09 },
  ], [
    { id: 7, name: 'Shelter', latitude: '14.61', longitude: '121.11', status: 'Open' },
    { id: 8, latitude: null, longitude: 121.1 },
  ]);
  assert.equal(markers.length, 3);
  assert.deepEqual(markers.map((m) => m.type), ['flood', 'pending', 'evac']);
  assert.deepEqual(markers[0].position, [14.65, 121.09]);
});

test('capacity ignores missing values and does not fabricate available space', () => {
  assert.deepEqual(buildCapacityChart([
    { name: 'A', capacity: 100, current_evacuees: 25 },
    { name: 'B', capacity: null, current_evacuees: 5 },
    { name: 'C', capacity: 30, current_evacuees: 40 },
  ]), [
    { name: 'A', available: 75, used: 25, capacity: 100 },
    { name: 'C', available: 0, used: 40, capacity: 30 },
  ]);
});

test('recent activities merge actual records in time order only', () => {
  const activities = buildRecentActivities([
    { id: 'r1', title: 'Bridge', created_at: ISO(NOW - 60_000), status: 'Pending' },
  ], [
    { id: 'a1', title: 'Notice', created_at: ISO(NOW - 10_000) },
    { id: 'a2', title: 'No time' },
  ]);
  assert.equal(activities.length, 2);
  assert.deepEqual(activities.map((event) => event.id), ['advisory:a1', 'report:r1']);
});

test('station reporting requires verified active catalog, fresh timestamps and valid levels', () => {
  const rows = [
    { station_code: 'A', level: 14.5, observed_at: ISO(NOW - 60_000) },
    { station_code: 'B', level: 15.5, observed_at: ISO(NOW - 90 * 60_000) },
    { station_code: 'C', level: 18, observed_at: ISO(NOW - 60_000) },
    { station_code: 'D', level: null, observed_at: ISO(NOW - 60_000) },
  ];
  const catalog = ['A','B','D'].map((station_code) => ({station_code, enabled:true}));
  assert.deepEqual(countReportingStations(rows, catalog, NOW), {healthy:1,total:3});
  assert.equal(countReportingStations(rows, [], NOW), null);
});

function fakeClient(results) {
  const calls = [];
  return {
    calls,
    from(table) {
      const query = { table, filters: [],
        select(columns, options) { this.columns = columns; this.options = options; return this; },
        eq(column, value) { this.filters.push(['eq',column,value]); return this; },
        or(expression) { this.filters.push(['or',expression]); return this; },
        gte(column, value) { this.filters.push(['gte',column,value]); return this; },
        lte(column, value) { this.filters.push(['lte',column,value]); return this; },
        order(column, options) { this.filters.push(['order',column,options]); return this; },
        limit(value) { this.filters.push(['limit',value]); return this; },
        then(resolve, reject) {
          calls.push(this);
          const name = table + '/' + (this.options?.head ? 'count' : this.columns);
          const response = results[name] ?? {data:[],error:null};
          return Promise.resolve(response).then(resolve,reject);
        },
      };
      return query;
    },
  };
}

test('dashboard combines real Supabase results without fallback demo data', async () => {
  const client = fakeClient({
    'advisories/count': {count:2,error:null},
    'community_reports/count': {count:7,error:null},
    'community_reports/id,created_at': {data:[{id:'1',created_at:ISO(NOW-20_000)}],error:null},
    'community_reports/id,title,category,status,address,latitude,longitude,created_at': {data:[
      {id:'1',title:'Rain',category:'Flood',status:'Approved',latitude:14.64,longitude:121.09,created_at:ISO(NOW-20_000)},
    ],error:null},
    'community_reports/id,title,category,status,created_at': {data:[{id:'1',title:'Rain',created_at:ISO(NOW-20_000)}],error:null},
    'advisories/id,title,status,created_at': {data:[{id:'a1',title:'Advisory',created_at:ISO(NOW-30_000)}],error:null},
    'evacuation_centers/id,name,latitude,longitude,status,capacity,current_evacuees': {data:[{
      id:10,name:'Shelter',latitude:14.62,longitude:121.1,status:'Standby',capacity:50,current_evacuees:15,
    }],error:null},
    'monitoring_stations/station_code,level,observed_at,updated_at': {data:[{
      station_code:'A',level:14,observed_at:ISO(NOW-30_000),
    }],error:null},
    'station_catalog/station_code,enabled': {data:[{station_code:'A',enabled:true}],error:null},
  });
  const result = await fetchDashboardData(client, NOW);
  assert.equal(result.successfulQueries, 9);
  assert.deepEqual(result.metrics, {activeAdvisories:2,pendingReports:7,reportingStations:{healthy:1,total:1}});
  assert.equal(result.incidents.reduce((sum,row)=>sum+row.reports,0),1);
  assert.equal(result.mapMarkers.length,2);
  assert.equal(result.resourceData[0].used,15);
  assert.equal(result.activities[0].id,'report:1');
  assert.equal(result.warnings.length,0);
  assert.ok(client.calls.some((query)=>query.table==='community_reports' && query.options?.head));
});

test('query errors are visible and unavailable datasets remain null, not fake zero', async () => {
  const client = fakeClient({
    'advisories/count': {count:null,error:{message:'permission denied'}},
    'community_reports/id,created_at': {data:null,error:{message:'Network failure'}},
  });
  const result = await fetchDashboardData(client, NOW);
  assert.equal(result.metrics.activeAdvisories,null);
  assert.equal(result.incidents,null);
  assert.ok(result.warnings.some((notice)=>notice.includes('permission denied')));
  assert.equal(result.successfulQueries,7);
});
