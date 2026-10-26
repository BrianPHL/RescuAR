import { createClient } from '@supabase/supabase-js';

async function fetchSchema() {
  const url = 'https://itxjqcnvxlgzeqkivhhc.supabase.co/rest/v1/?apikey=sb_publishable_QQi7onBtfe-d8fV0ep_K9g_N94rlE0s';
  const response = await fetch(url);
  const json = await response.json();

  if (json.definitions && json.definitions.admin_roles) {
    console.log("admin_roles schema:", json.definitions.admin_roles.properties);
  }
  if (json.definitions && json.definitions.users) {
    console.log("users schema:", json.definitions.users.properties);
  }
}

fetchSchema();
