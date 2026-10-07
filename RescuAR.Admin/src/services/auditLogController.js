import { normalizeAuditFilters } from './webAuditService.js';

export function createAuditLogController(service, onState, onAccessDenied = () => {}) {
  let active = true;
  let revision = 0;
  let state = { pages: [], pageIndex: 0, filters: normalizeAuditFilters(), loading: true, error: '', selected: null };
  const publish = (patch) => {
    if (!active) return;
    state = { ...state, ...patch };
    onState(state);
  };

  async function run(eventType, work, { operationId = service.newOperationId(), filters = state.filters,
    pageIndex = 0, targetId = null } = {}) {
    const ticket = ++revision;
    publish({ loading: true, error: '', selected: null });
    try {
      await service.recordObservation({ operationId, eventType, targetId, details: {
        page: pageIndex + 1, filter_applied: Object.values(filters).some(Boolean),
      } });
      if (!active || ticket !== revision) return;
      const patch = await work();
      if (active && ticket === revision) publish({ ...patch, loading: false });
    } catch (error) {
      if (!active || ticket !== revision) return;
      publish({ loading: false, error: error.message || 'Could not load audit history. Please retry.',
        ...(error.accessDenied ? { pages: [], pageIndex: 0 } : {}) });
      if (error.accessDenied) onAccessDenied();
    }
  }

  const firstPage = async (filters) => ({
    pages: [await service.readPage({ filters })], pageIndex: 0, filters,
  });

  return {
    loadInitial(operationId) { return run('audit.viewed', () => firstPage(state.filters), { operationId }); },
    refresh() { return run('audit.refreshed', () => firstPage(state.filters)); },
    applyFilters(input) {
      try {
        const filters = normalizeAuditFilters(input);
        return run('audit.queried', () => firstPage(filters), { filters });
      } catch (error) {
        publish({ error: error.message });
        return Promise.resolve();
      }
    },
    older() {
      const current = state.pages[state.pageIndex];
      if (!current?.hasMore || state.loading) return Promise.resolve();
      const pageIndex = state.pageIndex + 1;
      return run('audit.queried', async () => {
        const pages = [...state.pages];
        if (!pages[pageIndex]) pages.push(await service.readPage({ filters: state.filters, cursor: current.nextCursor }));
        return { pages, pageIndex };
      }, { pageIndex });
    },
    newer() {
      if (!state.pageIndex || state.loading) return Promise.resolve();
      const pageIndex = state.pageIndex - 1;
      return run('audit.queried', async () => ({ pageIndex }), { pageIndex });
    },
    inspect(row) {
      if (state.loading) return Promise.resolve();
      return run('audit.viewed', async () => ({ selected: row }), { targetId: row.id, pageIndex: state.pageIndex });
    },
    closeDetails() { publish({ selected: null }); },
    dispose() { active = false; revision++; },
  };
}
