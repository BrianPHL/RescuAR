import { supabase } from '../supabaseClient';
import { createReportsModerationService } from './reportsModerationService.js';

export const reportsModerationService = createReportsModerationService(supabase);
