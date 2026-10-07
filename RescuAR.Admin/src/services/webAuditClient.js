import { supabase } from '../supabaseClient';
import { createWebAuditService } from './webAuditService.js';

export const webAuditService = createWebAuditService(supabase);
