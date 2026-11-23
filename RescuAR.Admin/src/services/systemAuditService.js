export const SYSTEM_AUDIT_PAGE_SIZE = 25;
const uuidPattern = /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i;

export function createSystemAuditService(client) {
  return {
    async list({ beforeId = null, module = '', eventType = '', actorId = '' } = {}) {
      const cleanActor = actorId.trim();
      if (cleanActor && !uuidPattern.test(cleanActor)) throw new Error('Enter a valid administrator UUID.');
      const { data, error } = await client.rpc('web_admin_audit_list', {
        p_limit: SYSTEM_AUDIT_PAGE_SIZE + 1,
        p_before_id: beforeId,
        p_module: module.trim() || null,
        p_event_type: eventType.trim() || null,
        p_actor_id: cleanActor || null,
      });
      if (error) {
        if (error.code === '42501') throw new Error('Access denied. System Logs requires system administrator permissions.');
        throw new Error(`Could not load audit history: ${error.message}`);
      }
      if (!Array.isArray(data)) throw new Error('Unexpected audit response from Supabase.');
      const rows = data.slice(0, SYSTEM_AUDIT_PAGE_SIZE);
      if (rows.some((r) => !Number.isSafeInteger(Number(r?.id)) || !r?.event_type || !r?.recorded_at)) {
        throw new Error('Invalid audit records returned by server.');
      }
      const hasMore = data.length > SYSTEM_AUDIT_PAGE_SIZE;
      return { rows, hasMore, nextId: hasMore ? rows.at(-1).id : null };
    }
  };
}
