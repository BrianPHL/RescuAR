import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const dashboard = read('src/components/Dashboard.jsx');
const modals = read('src/components/Modals.jsx');
const responsive = read('src/responsive.css');

test('overview cards use three equal grid columns without Bootstrap four-column widths', () => {
  assert.match(dashboard, /grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)/);
  assert.match(dashboard, /className="stats-grid"/);
  assert.match(dashboard, /className="stat-card-cell"/);
  assert.doesNotMatch(dashboard, /col-xxl-3|className="stats-grid row/);
});

test('dashboard injected styles no longer override all app box sizing', () => {
  assert.doesNotMatch(dashboard, /\*\s*\{\s*margin:\s*0;\s*padding:\s*0;\s*box-sizing:\s*inherit/);
  assert.match(dashboard, /\.dashboard-container \*\s*\{\s*box-sizing:\s*border-box/);
  assert.match(dashboard, /\.dashboard-container \.btn-primary/);
});

test('advisory modal is styled independently and has responsive fields', () => {
  assert.match(modals, /className="advisory-modal"/);
  assert.match(modals, /className="form-group advisory-form-row"/);
  assert.match(responsive, /\.modal-container \*\s*\{ box-sizing:\s*border-box;/);
  assert.match(responsive, /\.advisory-modal \.form-textarea\s*\{[\s\S]*?max-width:\s*100%/);
  assert.match(responsive, /\.advisory-modal \.advisory-form-row\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
});

test('sidebar menu can scroll rather than clipping expanded items', () => {
  assert.match(responsive, /\.app-sidebar \.sidebar-menu-list\s*\{[\s\S]*?overflow-y:\s*auto/);
  assert.match(responsive, /\.app-sidebar \.sidebar-link span[\s\S]*?overflow-wrap:\s*anywhere/);
});
