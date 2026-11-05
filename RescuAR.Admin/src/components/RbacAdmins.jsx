import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  ChevronDown,
  KeyRound,
  Plus,
  RefreshCw,
  ShieldCheck,
  ShieldOff,
  UserRoundCog,
  UserPlus,
  Copy,
  Users,
} from 'lucide-react';
import { supabase } from '../supabaseClient';
import './RbacPages.css';

const DEFAULT_ROLES = [
  ['superadmin', 'Superadmin'],
  ['research_head', 'Research Head'],
  ['operations_admin', 'Operations Admin'],
  ['community_moderator', 'Community Moderator'],
  ['data_analyst', 'Data Analyst'],
];

// Keep this aligned with the permissions accepted by the existing custom-role RPC.
const CUSTOM_PERMISSIONS = [
  ['dashboard.view', 'View Dashboard'],
  ['monitoring.view', 'View Monitoring'],
  ['hazard.view', 'View Flood Hazard Exposure'],
  ['research.view', 'View Research Data'],
  ['research.manage', 'Manage Research Data'],
  ['analytics.view', 'View Analytics'],
  ['advisory.manage', 'Manage Advisories'],
  ['evacuation.manage', 'Manage Evacuation Centers'],
  ['hotline.manage', 'Manage Emergency Hotlines'],
  ['reports.moderate', 'Moderate Community Reports'],
  ['residents.manage', 'Manage Mobile Users'],
];

const formatDate = (date) => {
  if (!date) return '—';
  const parsed = new Date(date);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString('en-PH', {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
};

export default function RbacAdmins() {
  const [roles, setRoles] = useState(DEFAULT_ROLES);
  const [rows, setRows] = useState([]);
  const [email, setEmail] = useState('');
  const [newAdminEmail, setNewAdminEmail] = useState('');
  const [newAdminRole, setNewAdminRole] = useState('operations_admin');
  const [oneTimeCredential, setOneTimeCredential] = useState(null);
  const [role, setRole] = useState('data_analyst');
  const [existingPasswordMode, setExistingPasswordMode] = useState('keep');
  const [customName, setCustomName] = useState('');
  const [customLabel, setCustomLabel] = useState('');
  const [customPermissions, setCustomPermissions] = useState([]);
  const [notice, setNotice] = useState(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const roleLabels = useMemo(() => Object.fromEntries(roles), [roles]);
  const adminCount = useMemo(() => new Set(rows.map((row) => row.user_id)).size, [rows]);

  const refresh = async () => {
    setLoading(true);
    try {
      const [{ data: accounts, error: accountsError }, { data: availableRoles, error: rolesError }] = await Promise.all([
        supabase.rpc('rbac_list_accounts'),
        supabase.rpc('rbac_list_roles'),
      ]);
      if (accountsError) throw accountsError;
      setRows(Array.isArray(accounts) ? accounts : []);
      if (rolesError) throw rolesError;
      if (Array.isArray(availableRoles)) {
        setRoles(availableRoles.map((item) => [item.id, item.label || item.id]));
      }
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to load administrator information.' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void refresh(); }, []);

  const changeAssignment = async (enabled, targetEmail = email, targetRole = role) => {
    if (busy) return;
    const normalizedEmail = targetEmail.trim();
    if (!normalizedEmail) {
      setNotice({ type: 'error', text: 'Enter an email belonging to an existing Supabase Auth account.' });
      return;
    }
    setBusy(true);
    setNotice(null);
    try {
      // A temporary password for an existing Auth account REPLACES its current
      // Supabase password, including for any mobile-app login sharing this Auth account.
      // The server refuses to reset current administrators or the Superadmin.
      const useTemporaryPassword = enabled && targetEmail === email && existingPasswordMode === 'temporary';
      if (useTemporaryPassword) {
        if (targetRole === 'superadmin') {
          throw new Error('Temporary-password promotion cannot grant Superadmin access.');
        }
        const accepted = window.confirm(
          `Reset the existing Supabase Auth password for ${normalizedEmail} and assign ${roleLabels[targetRole] || targetRole}?\n\n` +
          'This replaces the current password, including mobile-app access for this account. ' +
          'Only promote an account whose owner has been independently verified. Continue?'
        );
        if (!accepted) return;
        setOneTimeCredential(null);
        const { data, error } = await supabase.functions.invoke('rbac-admin-provision', {
          body: { action: 'assign_existing_admin', email: normalizedEmail, role: targetRole },
        });
        if (error) {
          let message = error.message || 'Existing-user promotion failed.';
          try { const response = await error.context?.json?.(); if (response?.error) message = response.error; } catch { /* ignore invalid body */ }
          throw new Error(message);
        }
        if (!data?.temporaryPassword || !data?.email || !data?.mustChangePassword) {
          throw new Error('Unexpected promotion response. Verify the account in Supabase before retrying.');
        }
        setOneTimeCredential({ email: data.email, temporaryPassword: data.temporaryPassword, source: 'existing' });
        setExistingPasswordMode('keep');
        setEmail('');
        await refresh();
        setNotice({ type: 'success', text: 'Existing Auth user promoted. Share the temporary password securely; first-login change is mandatory.' });
      } else {
        const { error } = await supabase.rpc('rbac_set_assignment', {
          p_email: normalizedEmail,
          p_role: targetRole,
          p_enabled: enabled,
        });
        if (error) throw error;
        if (enabled) setEmail('');
        await refresh();
        setNotice({ type: 'success', text: enabled ? 'Administrator role assigned without changing the account password.' : 'Administrator role revoked.' });
      }
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to update the administrator role.' });
    } finally {
      setBusy(false);
    }
  };

  // Account creation is performed exclusively by the Supabase Edge Function.
  // This UI never receives or stores a service-role key. The generated password
  // is returned once, held in component memory, and never added to persistent browser storage.
  const provisionAdministrator = async (event) => {
    event.preventDefault();
    if (busy) return;
    if (!newAdminEmail.trim() || newAdminRole === 'superadmin') {
      setNotice({ type: 'error', text: 'Enter a valid email and select a non-Superadmin role.' });
      return;
    }
    setBusy(true);
    setNotice(null);
    setOneTimeCredential(null);
    try {
      const { data, error } = await supabase.functions.invoke('rbac-admin-provision', {
        body: { action: 'create_admin', email: newAdminEmail.trim(), role: newAdminRole },
      });
      if (error) {
        let message = error.message || 'Account provisioning failed.';
        try { const response = await error.context?.json?.(); if (response?.error) message = response.error; } catch { /* no JSON body */ }
        throw new Error(message);
      }
      if (!data?.temporaryPassword || !data?.email) throw new Error('Unexpected provisioning response. Verify the new account in Supabase.');
      setOneTimeCredential({ email: data.email, temporaryPassword: data.temporaryPassword, source: 'create' });
      setNewAdminEmail('');
      await refresh();
      setNotice({ type: 'success', text: 'Administrator created, assigned and marked for mandatory first-login password change.' });
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to create an administrator.' });
    } finally {
      setBusy(false);
    }
  };

  const copyOneTimePassword = async () => {
    if (!oneTimeCredential) return;
    try {
      await navigator.clipboard.writeText(oneTimeCredential.temporaryPassword);
      setNotice({ type: 'success', text: 'Temporary password copied. Deliver it securely and clear your clipboard afterward.' });
    } catch {
      setNotice({ type: 'error', text: 'Clipboard unavailable. Copy the displayed password manually.' });
    }
  };

  const createCustomRole = async (event) => {
    event.preventDefault();
    if (busy) return;
    if (!/^custom_[a-z0-9_]+$/.test(customName.trim())) {
      setNotice({ type: 'error', text: 'Role ID must begin with custom_ and contain only lowercase letters, numbers, or underscores.' });
      return;
    }
    if (!customLabel.trim() || customPermissions.length === 0) {
      setNotice({ type: 'error', text: 'Enter a role label and select at least one permission.' });
      return;
    }
    setBusy(true);
    setNotice(null);
    try {
      const { error } = await supabase.rpc('rbac_create_custom_role', {
        p_id: customName.trim(),
        p_label: customLabel.trim(),
        p_permissions: customPermissions,
      });
      if (error) throw error;
      setCustomName('');
      setCustomLabel('');
      setCustomPermissions([]);
      await refresh();
      setNotice({ type: 'success', text: 'Custom role created. It is now available for assignment.' });
    } catch (error) {
      setNotice({ type: 'error', text: error.message || 'Unable to create the role.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="main-view rbac-page" aria-label="Administrator Management">
      <div className="view-header rbac-page-heading">
        <div className="view-title-container">
          <div className="rbac-eyebrow"><UserRoundCog size={15} /> Community Management / User Management</div>
          <h1>Administrator Management</h1>
          <p className="view-subtitle">Assign roles and control administrator access across RescuAR.</p>
        </div>
        <span className="rbac-top-badge"><ShieldCheck size={15} /> Superadmin access</span>
      </div>

      <div className="rbac-summary-grid">
        <div className="rbac-summary-card"><span className="rbac-summary-icon"><Users size={20} /></span><div><strong>{loading ? '—' : adminCount}</strong><span>Administrator accounts</span></div></div>
        <div className="rbac-summary-card"><span className="rbac-summary-icon"><KeyRound size={20} /></span><div><strong>{loading ? '—' : roles.length}</strong><span>Available roles</span></div></div>
      </div>

      {notice && <div className={`rbac-notice rbac-notice--${notice.type}`} role={notice.type === 'error' ? 'alert' : 'status'}>
        {notice.type === 'error' ? <AlertCircle size={18} /> : <CheckCircle2 size={18} />}
        <span>{notice.text}</span>
        <button type="button" aria-label="Dismiss message" onClick={() => setNotice(null)}>×</button>
      </div>}

      <section className="rbac-panel" aria-labelledby="rbac-create-admin-title">
        <div className="rbac-panel-heading"><div className="rbac-section-icon"><UserPlus size={19} /></div><div><h2 id="rbac-create-admin-title">Create Verified Administrator</h2><p>Create a new Supabase Auth account with a temporary password and an RBAC role. Email ownership must be checked by your organization.</p></div></div>
        <form className="rbac-assign-form" onSubmit={provisionAdministrator}>
          <div className="form-group rbac-grow">
            <label className="form-label" htmlFor="rbac-create-email">New administrator email</label>
            <input id="rbac-create-email" className="form-input" type="email" autoComplete="off" placeholder="operator@example.com" value={newAdminEmail} onChange={(event) => setNewAdminEmail(event.target.value)} required maxLength={254} />
          </div>
          <div className="form-group rbac-role-field">
            <label className="form-label" htmlFor="rbac-create-role">Initial role</label>
            <select className="form-input" id="rbac-create-role" value={newAdminRole} onChange={(event) => setNewAdminRole(event.target.value)}>
              {roles.filter(([id]) => id !== 'superadmin').map(([id, label]) => <option key={id} value={id}>{label}</option>)}
            </select>
          </div>
          <button className="btn-primary rbac-action-btn" type="submit" disabled={busy || !newAdminEmail.trim()}><UserPlus size={16} /> {busy ? 'Creating…' : 'Create Admin'}</button>
        </form>
        <div className="rbac-panel-help"><ShieldCheck size={15}/><span>The backend administratively confirms the email without sending an invitation. This is not proof of mailbox ownership. Share the one-time password securely; the user must change it before accessing the dashboard.</span></div>
        {oneTimeCredential?.source === 'create' && <div className="rbac-one-time-box" role="status">
          <div><strong>New administrator temporary password — display once</strong><p>Copy and deliver securely. The administrator must enter this credential again when setting a new password.</p></div>
          <p className="rbac-one-time-email">Account: {oneTimeCredential.email}</p>
          <div className="rbac-one-time-actions"><input className="form-input" aria-label="New administrator temporary password" readOnly type="text" value={oneTimeCredential.temporaryPassword} autoComplete="off" spellCheck={false} /><button type="button" className="btn-secondary rbac-action-btn" onClick={copyOneTimePassword}><Copy size={15}/> Copy</button><button type="button" className="btn-secondary rbac-action-btn" onClick={() => setOneTimeCredential(null)}>Discard</button></div>
        </div>}

      </section>

      <section className="rbac-panel" aria-labelledby="rbac-assign-title">
        <div className="rbac-panel-heading"><div className="rbac-section-icon"><UserRoundCog size={19} /></div><div><h2 id="rbac-assign-title">Assign an Administrator Role</h2><p>Use an account already registered in Supabase Authentication.</p></div></div>
        <form className="rbac-assign-form" onSubmit={(event) => { event.preventDefault(); void changeAssignment(true); }}>
          <div className="form-group rbac-grow">
            <label className="form-label" htmlFor="rbac-admin-email">Administrator email address</label>
            <input id="rbac-admin-email" type="email" autoComplete="off" className="form-input" placeholder="name@example.com" value={email} onChange={(event) => setEmail(event.target.value)} required />
          </div>
          <div className="form-group rbac-role-field">
            <label className="form-label" htmlFor="rbac-admin-role">Role</label>
            <select id="rbac-admin-role" className="form-input" value={role} onChange={(event) => setRole(event.target.value)}>{roles.map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select>
          </div>
          <button type="submit" className="btn-primary rbac-action-btn" disabled={busy || !email.trim() || (existingPasswordMode === 'temporary' && role === 'superadmin')}><Plus size={16} /> {busy ? 'Saving…' : 'Assign Role'}</button>
        </form>
        <fieldset className="rbac-existing-password-options">
          <legend className="form-label">Password when promoting an existing account</legend>
          <label className="rbac-existing-password-choice"><input type="radio" name="existing-admin-password" value="keep" checked={existingPasswordMode === 'keep'} onChange={() => setExistingPasswordMode('keep')} disabled={busy} /><span>Keep the user's existing password (recommended)</span></label>
          <label className="rbac-existing-password-choice"><input type="radio" name="existing-admin-password" value="temporary" checked={existingPasswordMode === 'temporary'} onChange={() => setExistingPasswordMode('temporary')} disabled={busy} /><span>Replace with generated temporary password; require password change</span></label>
          {existingPasswordMode === 'temporary' && <p className="rbac-existing-password-warning" role="note"><AlertCircle size={16} /> The existing Supabase Auth password will be replaced for this account everywhere it is used, including the mobile app. This option works only for a verified existing Auth user who has no administrator roles yet. Superadmin promotion and resets of existing administrators are blocked.</p>}
        </fieldset>
        <div className="rbac-panel-help"><ShieldCheck size={15} /><span>Keep the existing password by default. Only use temporary-password promotion after independently verifying the account owner. Supabase database permission enforcement remains active.</span></div>
        {oneTimeCredential?.source === 'existing' && <div className="rbac-one-time-box" role="status">
          <div><strong>Temporary administrator password — display once</strong><p>Copy and deliver securely. The administrator must enter it again during mandatory password setup. Discard it after delivery.</p></div>
          <p className="rbac-one-time-email">Account: {oneTimeCredential.email}</p>
          <div className="rbac-one-time-actions"><input className="form-input" aria-label="Promoted administrator temporary password" readOnly type="text" value={oneTimeCredential.temporaryPassword} autoComplete="off" spellCheck={false} /><button type="button" className="btn-secondary rbac-action-btn" onClick={copyOneTimePassword}><Copy size={15}/> Copy</button><button type="button" className="btn-secondary rbac-action-btn" onClick={() => setOneTimeCredential(null)}>Discard</button></div>
        </div>}
      </section>

      <section className="rbac-panel rbac-panel--flush" aria-labelledby="rbac-list-title">
        <div className="rbac-panel-toolbar"><div className="rbac-panel-heading"><div className="rbac-section-icon"><Users size={19} /></div><div><h2 id="rbac-list-title">Assigned Administrators</h2><p>Current role assignments for registered administrators.</p></div></div><button className="btn-secondary rbac-action-btn" type="button" onClick={() => void refresh()} disabled={busy || loading}><RefreshCw size={15} className={loading ? 'rbac-spin' : ''} /> Refresh</button></div>
        <div className="rbac-table-scroll"><table className="rbac-table"><thead><tr><th scope="col">Auth account</th><th scope="col">Assigned role</th><th scope="col">Assigned on</th><th scope="col" className="rbac-table-actions">Action</th></tr></thead><tbody>
          {loading ? <tr><td colSpan={4} className="rbac-empty-state">Loading administrator accounts…</td></tr> : rows.length === 0 ? <tr><td colSpan={4} className="rbac-empty-state">No administrator assignments were returned.</td></tr> : rows.map((row) => <tr key={`${row.user_id}:${row.role_id}`}>
            <td><span className="rbac-account-icon"><Users size={15} /></span><span className="rbac-email">{row.email || 'Unknown account'}</span></td>
            <td><span className={`rbac-role-tag ${row.role_id === 'superadmin' ? 'rbac-role-tag--super' : ''}`}>{row.role_id === 'superadmin' && <ShieldCheck size={13} />}{roleLabels[row.role_id] || row.role_id}</span></td>
            <td className="rbac-muted-cell">{formatDate(row.assigned_at)}</td>
            <td className="rbac-table-actions"><button type="button" className="rbac-revoke-btn" disabled={busy} onClick={() => { if (window.confirm(`Revoke ${row.role_id} from ${row.email}?`)) void changeAssignment(false, row.email, row.role_id); }}><ShieldOff size={14} /> Revoke</button></td>
          </tr>)}
        </tbody></table></div>
      </section>

      <details className="rbac-panel rbac-custom-role">
        <summary><div className="rbac-panel-heading"><div className="rbac-section-icon"><KeyRound size={19} /></div><div><h2>Create Custom Role</h2><p>Configure specific permissions for a specialized administrator.</p></div></div><ChevronDown size={18} className="rbac-summary-chevron" /></summary>
        <form className="rbac-custom-content" onSubmit={createCustomRole}>
          <div className="rbac-form-grid">
            <div className="form-group"><label className="form-label" htmlFor="rbac-custom-id">Role ID</label><input id="rbac-custom-id" className="form-input" placeholder="custom_barangay_coordinator" value={customName} onChange={(event) => setCustomName(event.target.value)} required /></div>
            <div className="form-group"><label className="form-label" htmlFor="rbac-custom-label">Display name</label><input id="rbac-custom-label" className="form-input" placeholder="Barangay Coordinator" value={customLabel} onChange={(event) => setCustomLabel(event.target.value)} required /></div>
          </div>
          <div className="rbac-permissions-heading">Role permissions <span>Select only the actions this administrator needs.</span></div>
          <div className="rbac-permission-grid">{CUSTOM_PERMISSIONS.map(([id, label]) => <label key={id} className="rbac-permission-item"><input type="checkbox" checked={customPermissions.includes(id)} onChange={(event) => setCustomPermissions((previous) => event.target.checked ? [...previous, id] : previous.filter((permission) => permission !== id))} /><span><strong>{label}</strong><small>{id}</small></span></label>)}</div>
          <div className="rbac-panel-help"><ShieldCheck size={15} /><span>Custom roles cannot grant Superadmin privileges. Authorization is still enforced by Supabase.</span></div>
          <button className="btn-primary rbac-action-btn rbac-custom-create" type="submit" disabled={busy || !customLabel.trim() || !customName.trim() || customPermissions.length === 0}><Plus size={16} /> Create Custom Role</button>
        </form>
      </details>

      <p className="rbac-footer-note"><ShieldCheck size={16} /> Superadmin changes are audited. Self-removal and removal of the last Superadmin are blocked by the database.</p>
    </section>
  );
}
