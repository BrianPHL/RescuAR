import { createClient } from '@supabase/supabase-js';

const supabase = createClient('https://itxjqcnvxlgzeqkivhhc.supabase.co', 'sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F');

async function testFetch() {
  const { data, error } = await supabase.rpc('get_all_users_with_roles');
  if (error) {
    console.error('Error fetching users:', error);
  } else {
    console.log('Success fetching users via RPC! Count:', data?.length);
    if (data?.length > 0) {
      console.log('First user:', data[0]);
    }
  }
}

testFetch();
