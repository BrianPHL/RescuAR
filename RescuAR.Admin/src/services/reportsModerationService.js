import { createWebAuditRecorder } from './webAuditService.js';

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const targetPattern = /^[A-Za-z0-9][A-Za-z0-9_:-]{0,127}$/;
const statuses = ['Pending', 'Approved', 'Rejected', 'Resolved'];

function failure(error, message) {
  const accessDenied = ['42501', 'PGRST301', 'PGRST303'].includes(error?.code);
  const stale = error?.code === '40001';
  const invalid = ['22023', 'P0002', 'PGRST202'].includes(error?.code);
  return Object.assign(new Error(accessDenied ? 'Administrator access could not be confirmed.'
    : stale ? 'This report changed. Review its current status before deciding again.'
      : invalid ? 'This report could not be moderated. Refresh and review it before trying again.' : message), {
    accessDenied, stale, refreshRequired: stale || error?.code === 'P0002', retryable: !accessDenied && !stale && !invalid,
  });
}

export function filterReports(reports, query) {
  const text = query.trim().toLowerCase();
  return text ? reports.filter((report) => ['title', 'description', 'posted_by', 'category', 'address']
    .some((key) => typeof report[key] === 'string' && report[key].toLowerCase().includes(text))) : reports;
}

export function reportMediaUrl(report) {
  try {
    const url = new URL(report?.media_url);
    if (['https:', 'http:'].includes(url.protocol)) return url.href;
  } catch { /* Missing or unsupported attachment URL. */ }
  return null;
}

export function createReportsModerationService(client, newId = () => globalThis.crypto.randomUUID()) {
  return {
    ...createWebAuditRecorder(client, { module: 'community-reports-moderation', targetType: 'report' }, newId),
    async readReports() {
      let result;
      try {
        result = await client.from('community_reports').select('*').order('created_at', { ascending: false });
      } catch (error) {
        throw failure(error, 'Could not load reports. Please retry.');
      }
      if (result?.error) throw failure(result.error, 'Could not load reports. Please retry.');
      if (!Array.isArray(result?.data) || result.data.some((row) => !row || typeof row.id !== 'string' || !row.id
        || (row.status !== null && typeof row.status !== 'string'))) {
        throw failure(null, 'Reports could not be read. Please retry.');
      }
      return result.data;
    },
    async moderate(request) {
      const { operationId, reportId, expectedStatus, newStatus } = request;
      if (!uuidPattern.test(operationId || '') || !targetPattern.test(reportId || '')
        || (expectedStatus !== null && !statuses.includes(expectedStatus))
        || !statuses.slice(1).includes(newStatus)) {
        throw failure({ code: '22023' }, 'Invalid moderation request.');
      }
      let result;
      try {
        result = await client.rpc('admin_moderate_report', {
          p_operation_id: operationId, p_report_id: reportId,
          p_expected_status: expectedStatus, p_new_status: newStatus,
        });
      } catch (error) {
        throw failure(error, 'Could not confirm this decision. Retry the same decision to check its result.');
      }
      if (result?.error) throw failure(result.error, 'Could not confirm this decision. Retry the same decision to check its result.');
      const receipt = result?.data?.[0];
      if (!Array.isArray(result?.data) || result.data.length !== 1 || !receipt
        || receipt.report_id !== reportId || receipt.before_status !== expectedStatus || receipt.after_status !== newStatus
        || receipt.outcome !== (expectedStatus === newStatus ? 'no_change' : 'succeeded')
        || !uuidPattern.test(receipt.audit_event_id || '')
        || typeof receipt.recorded_at !== 'string' || !Number.isFinite(Date.parse(receipt.recorded_at))) {
        throw failure(null, 'Could not confirm this decision. Retry the same decision to check its result.');
      }
      return receipt;
    },
    subscribe(onChange) {
      const channel = client.channel('public:community_reports')
        .on('postgres_changes', { event: '*', schema: 'public', table: 'community_reports' }, onChange).subscribe();
      return () => { void client.removeChannel(channel); };
    },
  };
}
