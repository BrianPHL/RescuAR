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

const { data, error } = await supabase
  .from('river_level_history')
  .select('station_name, level, status, source, observed_at')
  .eq('source', 'PAGASA')
  .order('observed_at', { ascending: false })
  .limit(20);

if (error) {
  console.error('PAGASA history verification query failed:', error.message);
  process.exit(1);
}

if (!data?.length) {
  console.error(
    'AUDIT #8 NOT PASSED AT RUNTIME: river_level_history contains 0 PAGASA rows. ' +
    'This is expected while PAGASA is unavailable or publishing no usable observations; no synthetic fallback is permitted.'
  );
  process.exit(1);
}

console.table(data);
console.log(`AUDIT #8 PAGASA STORAGE CHECK PASSED: ${data.length} recent official row(s) returned.`);
