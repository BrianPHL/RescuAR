import { createClient } from '@supabase/supabase-js';

async function fetchSchema() {
  const url = 'https://itxjqcnvxlgzeqkivhhc.supabase.co/rest/v1/?apikey=sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F';
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
