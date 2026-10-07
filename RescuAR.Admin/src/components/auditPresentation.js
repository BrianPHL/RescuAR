export const AUDIT_MODULES = {
  authentication: 'Authentication',
  dashboard: 'Dashboard',
  'monitoring-stations': 'Weather stations',
  'monitoring-river-level': 'River levels',
  'monitoring-inundation': 'Inundation analysis',
  'community-reports-moderation': 'Report moderation',
  'community-residents': 'Resident directory',
  'community-sms-parser': 'SMS parser',
  'community-user-management': 'User management',
  'content-advisories': 'Advisories',
  'content-evacuation': 'Evacuation centers',
  'content-news': 'Evacuation centers',
  'content-hotlines': 'Emergency hotlines',
  'system-settings': 'System settings',
  'system-logs': 'System logs',
  documentation: 'Documentation',
  'audit-logs': 'Audit logs',
};

export const AUDIT_ACTIONS = {
  'module.opened': 'Module opened',
  'records.listed': 'Records listed',
  'record.viewed': 'Record viewed',
  'report.media_opened': 'Report media opened',
  'records.queried': 'Records filtered or paged',
  'module.refreshed': 'Module refreshed',
  'draft.opened': 'Draft opened',
  'draft.template_applied': 'Draft template applied',
  'task.requested': 'Task requested',
  'task.completed': 'Task completion reported',
  'task.failed': 'Task failure reported',
  'task.cancelled': 'Task cancelled',
  'analysis.controls_changed': 'Analysis controls changed',
  'dashboard.export_requested': 'Dashboard export requested',
  'dashboard.export_generated': 'Dashboard export generated',
  'prediction.requested': 'Prediction requested',
  'prediction.simulation_run': 'Prediction simulation run',
  'advisory.copy_requested': 'Advisory copy requested',
  'asset.upload_requested': 'Image upload requested',
  'asset.upload_completed': 'Image upload reported complete',
  'settings.save_requested': 'Settings save requested',
  'notification.simulation_run': 'Notification simulation run',
  'audit.viewed': 'Audit history viewed',
  'audit.queried': 'Audit history filtered or paged',
  'audit.refreshed': 'Audit history refreshed',
  'audit.export_generated': 'Audit export generated',
  'auth.logout_requested': 'Sign-out requested',
  'users.listed': 'User directory read',
  'user.updated': 'User updated',
  'user.status_changed': 'User status changed',
  'user.deleted': 'User deleted',
};

export const AUDIT_OUTCOMES = {
  observed: 'Observed', requested: 'Requested', succeeded: 'Succeeded',
  failed: 'Failed', denied: 'Denied', simulated: 'Simulated', cancelled: 'Cancelled',
  no_change: 'No change', unknown: 'Unknown',
};

export function readableName(value) {
  return String(value || 'Unknown').replace(/[_.-]+/g, ' ');
}

export function auditSourceLabel(source) {
  if (source === 'database') return 'Database confirmed';
  if (source === 'client_observed') return 'Browser observed';
  return 'Unspecified';
}

export function auditDetailLines(details) {
  const lines = [];
  if (Array.isArray(details.changed_fields)) lines.push(`Changed fields: ${details.changed_fields.map(readableName).join(', ')}`);
  const labels = {
    before_status: 'Previous status', after_status: 'New status', returned_count: 'Records returned',
    selected_count: 'Records selected', page: 'Page', page_size: 'Page size', mode: 'Mode',
    template_id: 'Template', sort_key: 'Sort order',
  };
  for (const [key, label] of Object.entries(labels)) {
    if (['string', 'number'].includes(typeof details[key])) lines.push(`${label}: ${details[key]}`);
  }
  if (typeof details.filter_applied === 'boolean') lines.push(`Filters applied: ${details.filter_applied ? 'Yes' : 'No'}`);
  return lines;
}
