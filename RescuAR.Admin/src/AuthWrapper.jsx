import React, { useState, useEffect, useRef, useCallback } from 'react';
import App from './App';
import Auth from './components/Auth';
import { supabase } from './supabaseClient';
import { createAdminSessionGate } from './services/adminSessionGate';
import './components/AuditLogs.css';

export default function AuthWrapper() {
  const [access, setAccess] = useState({ status: 'checking', session: null });
  const [signOutError, setSignOutError] = useState('');
  const [signingOut, setSigningOut] = useState(false);
  const gateRef = useRef(null);
  const recheck = useCallback(() => gateRef.current?.refresh(), []);

  useEffect(() => {
    const gate = createAdminSessionGate(supabase, setAccess);
    gateRef.current = gate;
    gate.start();
    const onFocus = () => gate.refresh(true);
    window.addEventListener('focus', onFocus);
    return () => {
      window.removeEventListener('focus', onFocus);
      gate.dispose();
      gateRef.current = null;
    };
  }, []);

  const signOut = async () => {
    setSigningOut(true);
    setSignOutError('');
    try {
      const { error } = await supabase.auth.signOut();
      if (error) throw error;
      recheck();
    } catch {
      setSignOutError('Could not sign out. Please try again.');
    } finally {
      setSigningOut(false);
    }
  };

  if (access.status === 'signed-out') return <Auth />;
  if (access.status === 'authorized') return <App key={access.session.user.id} session={access.session} onAccessDenied={recheck} />;
  return (
    <main className="admin-access-screen">
      <section className="stations-card admin-access-card">
        {access.status === 'checking' ? <p role="status">Checking administrator access…</p> : <>
          <h1>{access.status === 'denied' ? 'Administrator access required' : 'Unable to verify access'}</h1>
          <p role="alert">{access.status === 'denied'
            ? 'This account does not have administrator access. Sign out to use another account.'
            : 'We could not verify your access. Please try again.'}</p>
          {signOutError && <p role="alert">{signOutError}</p>}
          <div className="admin-access-actions">
            <button className="btn-refresh" onClick={recheck} disabled={signingOut}>Try again</button>
            <button className="btn-refresh" onClick={signOut} disabled={signingOut}>{signingOut ? 'Signing out…' : 'Sign out'}</button>
          </div>
        </>}
      </section>
    </main>
  );
}
