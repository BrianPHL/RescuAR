import { createClient } from '@supabase/supabase-js';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL || 'https://itxjqcnvxlgzeqkivhhc.supabase.co';
const supabaseAnonKey = import.meta.env.VITE_SUPABASE_ANON_KEY || 'sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F';

export const supabase = createClient(supabaseUrl, supabaseAnonKey);
