export const AUDIT_PAGE_SIZE = 25;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function normalizeAuditFilters(filters = {}) {
  const result = {
    module: (filters.module || '').trim(),
    actorId: (filters.actorId || '').trim(),
    eventType: (filters.eventType || '').trim(),
  };
  if (result.actorId && !uuidPattern.test(result.actorId)) throw new Error('Enter a valid administrator ID.');
  return result;
}

function failure(error, message) {
  const denied = error?.code === '42501' || error?.code === 'PGRST301' || error?.code === 'PGRST303';
  return Object.assign(new Error(denied ? 'Administrator access could not be confirmed.' : message), { accessDenied: denied });
}

export function createWebAuditRecorder(client, { module, targetType }, newId = () => globalThis.crypto.randomUUID()) {
  return {
    newOperationId: newId,
    async recordObservation({ operationId, eventType, targetId = null, details = {} }) {
      let result;
      try {
        result = await client.rpc('record_web_audit_event', {
          p_operation_id: operationId,
          p_event_type: eventType,
          p_module: module,
          p_target_type: targetId ? targetType : 'module',
          p_target_id: targetId || module,
          p_details: details,
        });
      } catch (error) {
        throw failure(error, 'Could not record this action. Please retry.');
      }
      if (result?.error) throw failure(result.error, 'Could not record this action. Please retry.');
      if (!uuidPattern.test(result?.data || '')) throw failure(null, 'Could not confirm that this action was recorded. Please retry.');
      return result.data;
    },
  };
}

export function createWebAuditService(client, newId = () => globalThis.crypto.randomUUID()) {
  return {
    ...createWebAuditRecorder(client, { module: 'audit-logs', targetType: 'audit_event' }, newId),
    async readPage({ filters = {}, cursor = null } = {}) {
      const normalized = normalizeAuditFilters(filters);
      let result;
      try {
        result = await client.rpc('get_web_audit_logs', {
          p_limit: AUDIT_PAGE_SIZE + 1,
          p_before_at: cursor?.recordedAt || null,
          p_before_id: cursor?.id || null,
          p_module: normalized.module || null,
          p_actor_id: normalized.actorId || null,
          p_event_type: normalized.eventType || null,
        });
      } catch (error) {
        throw failure(error, 'Could not load audit history. Please retry.');
      }
      if (result?.error) throw failure(result.error, 'Could not load audit history. Please retry.');
      if (!Array.isArray(result?.data) || result.data.some((row) => !row || !uuidPattern.test(row.id || '')
        || typeof row.recorded_at !== 'string' || !Number.isFinite(Date.parse(row.recorded_at))
        || !uuidPattern.test(row.actor_id || '') || !uuidPattern.test(row.operation_id || '')
        || typeof row.event_type !== 'string' || typeof row.module !== 'string'
        || typeof row.source !== 'string' || typeof row.outcome !== 'string'
        || !row.details || typeof row.details !== 'object' || Array.isArray(row.details))) {
        throw failure(null, 'Audit history could not be read. Please retry.');
      }
      const records = result.data.slice(0, AUDIT_PAGE_SIZE);
      const hasMore = result.data.length > AUDIT_PAGE_SIZE;
      const last = records.at(-1);
      return { records, hasMore, nextCursor: hasMore ? { recordedAt: last.recorded_at, id: last.id } : null };
    },
  };
}
