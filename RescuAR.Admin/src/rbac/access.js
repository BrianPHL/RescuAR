export const VIEW_PERMISSION = {
  dashboard: 'dashboard.view',
  'monitoring-stations': 'monitoring.view',
  'monitoring-river-level': 'monitoring.view',
  'monitoring-hazard-exposure': 'hazard.view',
  'community-reports-moderation': 'reports.moderate',
  'community-user-management': 'residents.manage',
  'content-advisories': 'advisory.manage',
  'content-evacuation': 'evacuation.manage',
  'content-hotlines': 'hotline.manage',
  'system-settings': 'system.all',
  'system-logs': 'system.all',
  'rbac-admins': 'system.all',
  'rbac-tests': 'system.all',
  documentation: 'dashboard.view',
};

export function hasPermission(access, permission) {
  if (!access || !Array.isArray(access.permissions)) return false;
  return access.permissions.includes('system.all') || access.permissions.includes(permission);
}
export const canView = (access, view) => hasPermission(access, VIEW_PERMISSION[view] || 'system.all');
export const preferredView = (access) =>
  Object.keys(VIEW_PERMISSION).find((view) => canView(access, view)) || null;

export async function loadAccess(client) {
  const {data,error} = await client.rpc('rbac_my_access');
  if (error) throw error;
  return {
    roles: Array.isArray(data?.roles) ? data.roles : [],
    permissions: Array.isArray(data?.permissions) ? data.permissions : [],
  };
}
