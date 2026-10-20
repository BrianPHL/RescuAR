/**
 * One-time or scheduled PAGASA backfill for the five live RescuAR stations.
 * Reuses collectHistoricalPagasa.js for source raw files + provenance, and
 * imports ONLY real, normalized measurements to Supabase. Safe to re-run.
 *
 * Usage:
 *   node scripts/backfillToSupabase.js --start '2026-04-17 00:00' --end now
 *   node scripts/backfillToSupabase.js --lookback-days 2 --end now
 *   node scripts/backfillToSupabase.js --start '2026-04-17 00:00' --dry-run
 */
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import dotenv from 'dotenv';
import { createClient } from '@supabase/supabase-js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const COLLECTOR = path.join(ROOT, 'scripts', 'collectHistoricalPagasa.js');
const PROCESSED = path.join(ROOT, 'data', 'historical', 'processed');
const LIVE_STATIONS = 'rodriguez,montalban,nangka,tumana,sto_nino_mmda';
const SOURCE_ID = 'PAGASA_FFWWS';

function manilaString(date) {
  const fields = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).formatToParts(date);
  const p = Object.fromEntries(fields.map((f) => [f.type, f.value]));
  return `${p.year}-${p.month}-${p.day} ${p.hour}:${p.minute}`;
}
function parseDate(input) {
  if (input === 'now') return new Date();
  if (!/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/.test(input || '')) {
    throw new Error(`Use Manila date YYYY-MM-DD HH:mm or now; received ${input}`);
  }
  const date = new Date(`${input.replace(' ', 'T')}:00+08:00`);
  if (Number.isNaN(date.getTime()) || manilaString(date) !== input) {
    throw new Error(`Invalid Manila date: ${input}`);
  }
  return date;
}
function argsFromCli(tokens) {
  const opts = { end: 'now', stepHours: 12, delayMs: 600, dryRun: false };
  for (let i = 0; i < tokens.length; i++) {
    const arg = tokens[i];
    if (['--start', '--end', '--lookback-days', '--step-hours', '--delay-ms'].includes(arg)) {
      if (tokens[i + 1] == null) throw new Error(`Missing value for ${arg}`);
      opts[{'--start':'start','--end':'end','--lookback-days':'lookbackDays','--step-hours':'stepHours','--delay-ms':'delayMs'}[arg]] = tokens[++i];
    } else if (arg === '--dry-run') opts.dryRun = true;
    else throw new Error(`Unknown option: ${arg}`);
  }
  if (opts.start && opts.lookbackDays !== undefined) throw new Error('Choose --start OR --lookback-days.');
  if (!opts.start && opts.lookbackDays === undefined) throw new Error('Provide --start or --lookback-days.');
  for (const k of ['lookbackDays','stepHours','delayMs']) if (opts[k] !== undefined) {
    opts[k] = Number(opts[k]);
    if (!Number.isFinite(opts[k]) || opts[k] < 0) throw new Error(`Invalid ${k}.`);
  }
  if (opts.lookbackDays !== undefined && opts.lookbackDays <= 0) throw new Error('--lookback-days must be positive.');
  if (opts.stepHours <= 0 || opts.stepHours > 24) throw new Error('--step-hours must be in (0,24].');
  return opts;
}
function monthlyWindows(first, last) {
  const out = [];
  let from = new Date(first);
  while (from <= last) {
    const local = manilaString(from);
    const year = Number(local.slice(0,4));
    const month = Number(local.slice(5,7));
    const nextMonth = month === 12
      ? new Date(`${year+1}-01-01T00:00:00+08:00`)
      : new Date(`${year}-${String(month+1).padStart(2,'0')}-01T00:00:00+08:00`);
    const to = new Date(Math.min(last.getTime(), nextMonth.getTime() - 60000));
    out.push({ from, to });
    from = nextMonth;
  }
  return out;
}
function eventName(from, to) {
  const s = manilaString(from).replace(/[- :]/g, '');
  const e = manilaString(to).replace(/[- :]/g, '');
  return `marikina_${s}_${e}`;
}
function rowForDatabase(row) {
  if (row.source !== SOURCE_ID || !/^\d{8}$/.test(String(row.station_code || ''))) return null;
  const level = Number(row.water_level_m);
  if (!Number.isFinite(level) || level < 0 || level > 100) return null;
  const date = new Date(row.observed_at);
  if (Number.isNaN(date.getTime())) return null;
  return {
    station_code: String(row.station_code),
    station_name: row.station_name,
    source_station_name: row.source_station_name,
    observed_at: date.toISOString(),
    level,
    raw_water_level: row.raw_water_level ?? null,
    quality_flag: row.quality_flag ?? null,
    alert_threshold_m: row.alert_threshold_m ?? null,
    alarm_threshold_m: row.alarm_threshold_m ?? null,
    critical_threshold_m: row.critical_threshold_m ?? null,
    status: row.status,
    source: SOURCE_ID
  };
}
async function storeFile(supabase, jsonPath, from, to) {
  const sourceRows = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
  if (!Array.isArray(sourceRows)) throw new Error(`Expected array in ${jsonPath}`);
  const unique = new Map();
  for (const raw of sourceRows) {
    const row = rowForDatabase(raw);
    if (!row) continue;
    const ts = new Date(row.observed_at).getTime();
    if (ts < from.getTime() || ts > to.getTime()) continue;
    unique.set(`${row.station_code}|${row.observed_at}`, row);
  }
  const rows = [...unique.values()];
  for (let i = 0; i < rows.length; i += 300) {
    const batch = rows.slice(i, i + 300);
    let ok = false;
    for (let attempt = 1; attempt <= 4; attempt++) {
      const { error } = await supabase.from('river_level_history').upsert(batch, {
        onConflict: 'station_code,observed_at,source', ignoreDuplicates: true
      });
      if (!error) { ok = true; break; }
      if (attempt === 4) throw new Error(`Supabase import failed: ${error.message}`);
      console.warn(`[Backfill] Supabase retry ${attempt}/3: ${error.message}`);
      await new Promise((resolve) => setTimeout(resolve, 1200 * attempt));
    }
    if (!ok) throw new Error('Unsuccessful import');
  }
  return { count: rows.length, raw: sourceRows.length };
}
async function main() {
  const opts = argsFromCli(process.argv.slice(2));
  const end = parseDate(opts.end);
  const start = opts.start ? parseDate(opts.start) : new Date(end.getTime() - opts.lookbackDays * 86400000);
  if (start > end) throw new Error('Start cannot be after end.');
  if (end > new Date(Date.now() + 60000)) throw new Error('End cannot be in the future.');
  const windows = monthlyWindows(start, end);
  console.log(`[Backfill] ${manilaString(start)} through ${manilaString(end)} Asia/Manila; ${windows.length} batch(es).`);
  console.log(`[Backfill] Stations: ${LIVE_STATIONS}`);
  for (const w of windows) console.log(`  ${manilaString(w.from)} -> ${manilaString(w.to)}`);
  if (opts.dryRun) { console.log('[Backfill] Dry-run finished. No download or database writes.'); return; }

  dotenv.config({ path: path.resolve(ROOT, '..', '.env') });
  const { SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY } = process.env;
  if (!SUPABASE_URL || !SUPABASE_SERVICE_ROLE_KEY) {
    throw new Error('SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY must be configured in scraper server environment (not frontend).');
  }
  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, {
    auth: { persistSession: false, autoRefreshToken: false }
  });

  let totalProcessed = 0;
  for (const { from, to } of windows) {
    const event = eventName(from, to);
    const collectorArgs = [
      ...process.execArgv.filter((arg) => arg === '--use-system-ca'),
      COLLECTOR, '--event', event,
      '--start', manilaString(from), '--end', manilaString(to),
      '--stations', LIVE_STATIONS,
      '--step-hours', String(opts.stepHours), '--delay-ms', String(opts.delayMs)
    ];
    console.log(`[Backfill] Collecting ${event}`);
    const result = spawnSync(process.execPath, collectorArgs, {
      cwd: ROOT, env: process.env, stdio: 'inherit'
    });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`${event} collection failed (exit code ${result.status}). No later batches were imported.`);
    const file = path.join(PROCESSED, `${event}_water_levels.json`);
    if (!fs.existsSync(file)) throw new Error(`Collector did not create ${file}`);
    const imported = await storeFile(supabase, file, from, to);
    totalProcessed += imported.count;
    console.log(`[Backfill] ${event}: ${imported.count} valid unique rows processed (existing duplicates skipped).`);
  }
  console.log(`[Backfill] COMPLETE: ${totalProcessed} rows processed across all 5 stations. Check database coverage with SQL.`);
}
main().catch((err) => { console.error('[Backfill] FAILED:', err.message); process.exitCode = 1; });
