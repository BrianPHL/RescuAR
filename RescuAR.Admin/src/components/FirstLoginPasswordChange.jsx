import React, { useState } from 'react';
import { AlertCircle, CheckCircle2, KeyRound, LockKeyhole } from 'lucide-react';
import BrandLogo from './BrandLogo';
import { supabase } from '../supabaseClient';
import './FirstLoginPasswordChange.css';

export default function FirstLoginPasswordChange({ email, onComplete, onSignOut, signOutError }) {
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const valid = password.length >= 12 && password.length <= 128 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password) && /[^a-zA-Z0-9]/.test(password) && password === confirmation;

  const submit = async (event) => {
    event.preventDefault();
    if (busy || !valid) return;
    setBusy(true);
    setMessage('');
    try {
      const { data, error } = await supabase.functions.invoke('rbac-admin-provision', {
        body: { action: 'complete_first_login', newPassword: password },
      });
      if (error) {
        let reason = error.message || 'Unable to update your password.';
        try { const details = await error.context?.json?.(); if (details?.error) reason = details.error; } catch { /* ignore */ }
        throw new Error(reason);
      }
      if (data?.updated !== true) throw new Error('Password update was not confirmed.');
      setPassword(''); setConfirmation('');
      onComplete?.();
    } catch (error) {
      setMessage(error.message || 'Password update failed.');
    } finally {
      setBusy(false);
    }
  };

  return <main className="rbac-first-login-shell">
    <section className="rbac-first-login-card" aria-label="First login password change">
      <div className="rbac-first-login-brand"><BrandLogo variant="auth" /></div>
      <div className="rbac-first-login-icon"><LockKeyhole size={24} /></div>
      <h1>Set your administrator password</h1>
      <p>You're signed in as <strong>{email}</strong>. To protect your account, replace the temporary password before accessing RescuAR Admin.</p>
      <form onSubmit={submit} className="rbac-first-login-form">
        <label htmlFor="first-admin-new-password">New password</label>
        <input id="first-admin-new-password" type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} required minLength={12} maxLength={128} />
        <label htmlFor="first-admin-confirm-password">Confirm new password</label>
        <input id="first-admin-confirm-password" type="password" autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} required minLength={12} maxLength={128} />
        <p className="rbac-first-login-hint"><KeyRound size={15}/> At least 12 characters, including uppercase, lowercase, number, and symbol.</p>
        {message && <p className="rbac-first-login-error" role="alert"><AlertCircle size={16}/>{message}</p>}
        <button className="rbac-first-login-submit" type="submit" disabled={!valid || busy}>{busy ? 'Updating password…' : <><CheckCircle2 size={17}/> Save Password & Continue</>}</button>
      </form>
      <button className="rbac-first-login-signout" type="button" onClick={onSignOut} disabled={busy}>Sign out instead</button>
      {signOutError && <p className="rbac-first-login-error" role="alert">{signOutError}</p>}
    </section>
  </main>;
}
