import { createClient } from '@supabase/supabase-js';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL || 'https://itxjqcnvxlgzeqkivhhc.supabase.co';
const supabaseAnonKey = import.meta.env.VITE_SUPABASE_ANON_KEY || 'sb_publishable_QQi7onBtfe-d8fV0ep_K9g_N94rlE0s';

export const supabase = createClient(supabaseUrl, supabaseAnonKey);
