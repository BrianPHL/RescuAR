import { createClient } from '@supabase/supabase-js';

const supabase = createClient('https://itxjqcnvxlgzeqkivhhc.supabase.co', 'sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F');

const adminRolesColumns = [
  'id',
  'user_id',
  'role',
  'created_at'
];

async function verifyAdminRolesColumns() {
  const existingColumns = [];
  const missingColumns = [];

  for (const col of adminRolesColumns) {
    const { error } = await supabase.from('admin_roles').select(col).limit(1);
    if (error) {
      if (error.message.includes('Could not find') || error.code === 'PGRST106') {
        missingColumns.push(col);
      } else {
        console.log(`Error checking ${col}:`, error.message);
      }
    } else {
      existingColumns.push(col);
    }
  }

  console.log('\\n--- admin_roles Results ---');
  console.log('Existing columns:', existingColumns);
  console.log('Missing columns:', missingColumns);
}

verifyAdminRolesColumns();
