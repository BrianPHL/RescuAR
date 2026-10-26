import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const file = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const sidebar = file('src/components/Sidebar.jsx');
const admins = file('src/components/RbacAdmins.jsx');
const testing = file('src/components/RbacTesting.jsx');
const access = file('src/rbac/access.js');
const app = file('src/App.jsx');

test('RBAC administration is nested under Community Management > User Management', () => {
  const community = sidebar.slice(sidebar.indexOf('{/* Community Management Section */}'), sidebar.indexOf('{/* Content Management Section */}'));
  assert.ok(community.includes('User Management'));
  assert.ok(community.includes('Administrator Management'));
  assert.ok(community.indexOf('User Management') < community.indexOf('Administrator Management'));
  assert.ok(community.includes("canView(access, 'rbac-admins')"));
  assert.ok(community.includes('sidebar-user-subpages'));
  assert.ok(sidebar.includes("activeView === 'rbac-admins'"));
});

test('Testing & Simulation is nested under Monitoring > Flood Hazard Exposure', () => {
  const monitoring = sidebar.slice(sidebar.indexOf('{/* Monitoring Section */}'), sidebar.indexOf('{/* Community Management Section */}'));
  assert.ok(monitoring.includes('Flood Hazard Exposure'));
  assert.ok(monitoring.includes('Testing &amp; Simulation'));
  assert.ok(monitoring.indexOf('Flood Hazard Exposure') < monitoring.indexOf('Testing &amp; Simulation'));
  assert.ok(monitoring.includes("canView(access, 'rbac-tests')"));
  assert.ok(monitoring.includes('sidebar-hazard-subpages'));
  assert.ok(sidebar.includes("activeView === 'rbac-tests'"));
});

test('RBAC page and navigation styles are scoped to avoid changes elsewhere', () => {
  assert.ok(sidebar.includes("import './SidebarRbac.css'"));
  assert.ok(admins.includes("import './RbacPages.css'"));
  assert.ok(testing.includes("import './RbacPages.css'"));
  assert.ok(file('src/components/RbacPages.css').includes('.rbac-page'));
  assert.ok(file('src/components/SidebarRbac.css').includes('.sidebar-nested-link'));
});

test('Only system.all holders can access RBAC administrator and testing views', () => {
  assert.match(access, /'rbac-admins':\s*'system\.all'/);
  assert.match(access, /'rbac-tests':\s*'system\.all'/);
  assert.match(app, /case 'rbac-admins':/);
  assert.match(app, /case 'rbac-tests':/);
  assert.ok(app.includes('canView(access, activeView)'));
});

test('Existing RPCs are retained; testing does not dispatch advisories or overwrite river levels', () => {
  for (const name of ['rbac_list_accounts','rbac_list_roles','rbac_set_assignment','rbac_create_custom_role']) {
    assert.ok(admins.includes(`'${name}'`), `missing administrator RPC: ${name}`);
  }
  for (const name of ['rbac_list_tests', 'rbac_log_test']) {
    assert.ok(testing.includes(`'${name}'`), `missing testing RPC: ${name}`);
  }
  assert.ok(testing.includes('legacy-unvalidated'));
  assert.ok(testing.includes('test_only'));
  assert.ok(!testing.includes(".from('monitoring_stations')"));
  assert.ok(!testing.includes('sendSMS'));
});
