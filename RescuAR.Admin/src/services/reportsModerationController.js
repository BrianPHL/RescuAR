export const initialReportsState = {
  reports: [], selectedId: null, selectionOperation: null, query: '',
  loading: true, busy: false, error: '', notice: '', lastUpdated: null,
  initialized: false, retryDecision: false, retryTitle: '', retryStatus: '',
};

export function createReportsModerationController(service, onState, onAccessDenied = () => {}) {
  let state = { ...initialReportsState };
  let active = true;
  let readRevision = 0;
  let queuedRefresh = false;
  let retryRequest = null;
  const publish = (patch) => {
    if (!active) return;
    state = { ...state, ...patch };
    onState(state);
  };
  const fail = (error) => {
    if (!active) return;
    if (error.accessDenied) {
      readRevision++;
      queuedRefresh = false;
      retryRequest = null;
      publish({ reports: [], selectedId: null, selectionOperation: null, initialized: false, retryDecision: false, retryTitle: '', retryStatus: '' });
      onAccessDenied();
    }
    publish({ error: error.message || 'This action could not be completed. Please retry.' });
  };

  async function readLatest() {
    const ticket = ++readRevision;
    publish({ loading: true });
    try {
      const reports = await service.readReports();
      if (!active || ticket !== readRevision) return;
      const selectedId = reports.some((row) => row.id === state.selectedId) ? state.selectedId : reports[0]?.id || null;
      publish({ reports, selectedId, lastUpdated: new Date().toLocaleString(),
        ...(selectedId !== state.selectedId ? { selectionOperation: null } : {}) });
    } catch (error) {
      if (active && ticket === readRevision) throw error;
    } finally {
      if (active && ticket === readRevision) publish({ loading: false });
    }
  }

  async function automaticRefresh() {
    if (!active || !state.initialized) return;
    if (state.busy) { queuedRefresh = true; return; }
    try { await readLatest(); } catch (error) { if (active) fail(error); }
  }

  async function run(work, observation = null) {
    if (!active || state.busy) return false;
    readRevision++;
    publish({ busy: true, loading: false, error: '', notice: '' });
    try {
      if (observation) await service.recordObservation(observation);
      if (!active) return false;
      await work();
      return active;
    } catch (error) {
      if (active) {
        let reportedError = error;
        if (error.refreshRequired) {
          try { await readLatest(); } catch (refreshError) { if (refreshError.accessDenied) reportedError = refreshError; }
        }
        fail(reportedError);
      }
      return false;
    } finally {
      publish({ busy: false, loading: false });
      if (active && queuedRefresh) { queuedRefresh = false; void automaticRefresh(); }
    }
  }

  function observe(eventType, work, { operationId = service.newOperationId(), targetId = null, details = {} } = {}) {
    return run(work, { operationId, eventType, targetId, details });
  }

  function decide(request) {
    return run(async () => {
      let receipt;
      try {
        receipt = await service.moderate(request);
      } catch (error) {
        if (active) {
          retryRequest = error.retryable === false ? null : request;
          publish({ retryDecision: Boolean(retryRequest), retryTitle: retryRequest?.displayName || '', retryStatus: retryRequest?.newStatus || '' });
        }
        throw error;
      }
      if (!active) return;
      retryRequest = null;
      publish({ retryDecision: false, retryTitle: '', retryStatus: '', notice: receipt.outcome === 'no_change'
        ? 'The report already had this status. The decision was recorded.' : 'Decision saved and recorded.' });
      // Receipts can describe an earlier retry; always read the current report state.
      await readLatest();
    });
  }

  return {
    loadInitial(operationId) {
      return observe('module.opened', async () => { await readLatest(); publish({ initialized: true }); }, { operationId });
    },
    refresh() {
      if (!state.initialized) return this.loadInitial(service.newOperationId());
      return observe('module.refreshed', readLatest);
    },
    applyFilter(query) {
      if (!state.initialized) return Promise.resolve(false);
      const normalized = query.trim();
      return observe('records.queried', async () => { publish({ query: normalized }); },
        { details: { filter_applied: Boolean(normalized) } });
    },
    inspect(reportId) {
      if (!state.initialized || !state.reports.some((row) => row.id === reportId)) return Promise.resolve(false);
      const operationId = service.newOperationId();
      return observe('record.viewed', async () => { publish({ selectedId: reportId, selectionOperation: operationId }); },
        { operationId, targetId: reportId });
    },
    openMedia(reportId, navigate) {
      if (!state.initialized || !state.reports.some((row) => row.id === reportId)) return Promise.resolve(false);
      return observe('report.media_opened', async () => { navigate(); }, { targetId: reportId });
    },
    moderate(newStatus) {
      if (!state.initialized || state.busy || retryRequest) return Promise.resolve(false);
      const report = state.reports.find((row) => row.id === state.selectedId);
      if (!report) return Promise.resolve(false);
      return decide({ operationId: service.newOperationId(), reportId: report.id,
        expectedStatus: report.status, newStatus, displayName: report.title || report.id });
    },
    retryModeration() { return retryRequest ? decide(retryRequest) : Promise.resolve(false); },
    automaticRefresh,
    showError(message) { publish({ error: message }); },
    dispose() { active = false; readRevision++; queuedRefresh = false; },
  };
}
