import { createClient } from '@supabase/supabase-js';
import dotenv from 'dotenv';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
dotenv.config({ path: path.resolve(__dirname, '../.env') });

const { SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY } = process.env;
if (!SUPABASE_URL || !SUPABASE_SERVICE_ROLE_KEY) {
  console.error('Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY.');
  process.exit(2);
}

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, {
  auth: { persistSession: false, autoRefreshToken: false }
});

const [{ data: history, error: historyError }, { data: sourceStatus, error: sourceError }] = await Promise.all([
  supabase
    .from('river_level_history')
    .select('station_code, station_name, level, status, source, observed_at')
    .eq('source', 'PAGASA_FFWWS')
    .order('observed_at', { ascending: false })
    .limit(20),
  supabase
    .from('telemetry_source_status')
    .select('source, worker_status, source_status, last_check_at, last_success_at, last_observation_at, records_received, last_error_code, last_error_message')
    .eq('source', 'PAGASA_FFWWS')
    .maybeSingle()
]);

if (historyError) {
  console.error('PAGASA history verification query failed:', historyError.message);
  process.exit(1);
}
if (sourceError) {
  console.error('PAGASA source-health verification query failed:', sourceError.message);
  process.exit(1);
}

if (!history?.length) {
  console.error(
    'PAGASA STORAGE CHECK NOT PASSED: river_level_history contains 0 PAGASA_FFWWS rows. ' +
    'No synthetic fallback is permitted.'
  );
  process.exit(1);
}

console.log('\nRecent PAGASA history:');
console.table(history);
console.log('\nPAGASA source health:');
console.table(sourceStatus ? [sourceStatus] : []);
console.log(`PAGASA STORAGE CHECK PASSED: ${history.length} recent official row(s) returned.`);
