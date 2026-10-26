import React, { useState, useEffect } from 'react';
import App from './App';
import Auth from './components/Auth';
import { supabase } from './supabaseClient';
import { loadAccess, preferredView } from './rbac/access';

export default function AuthWrapper() {
  const [session, setSession] = useState(null);
  const [loading, setLoading] = useState(true);
  const [accessAttempt, setAccessAttempt] = useState(0);
  const [accessState, setAccessState] = useState(null);
  const [signOutError, setSignOutError] = useState('');
  const userId = session?.user?.id;

  useEffect(() => {
    // Get initial session
    let active = true;
    supabase.auth.getSession()
      .then(({ data, error }) => {
        if (!active) return;
        if (error) {
          console.error('Unable to restore administrator session:', error);
          setSession(null);
        } else {
          setSession(data?.session ?? null);
        }
        setLoading(false);
      })
      .catch((error) => {
        if (!active) return;
        console.error('Unable to restore administrator session:', error);
        setLoading(false);
      });

    // Listen for auth changes
    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, session) => {
      if (!active) return;
      setSession(session);
      if (!session) setAccessState(null);
      setLoading(false);
    });

    return () => {
      active = false;
      subscription.unsubscribe();
    };
  }, []);

  useEffect(() => {
    if (!userId) return undefined;

    let cancelled = false;
    loadAccess(supabase).then((access) => {
      if (!cancelled) {
        setAccessState({ userId, attempt: accessAttempt, access, error: '' });
      }
    }).catch(() => {
      if (!cancelled) {
        setAccessState({
          userId, attempt: accessAttempt, access: null,
          error: 'Unable to load your administrator permissions. Retry or ask an administrator to check your account access.',
        });
      }
    });

    return () => { cancelled = true; };
  }, [userId, accessAttempt]);

  const handleSignOut = async () => {
    setSignOutError('');
    try {
      // Sign out on this device only. A global token-revocation call can fail
      // with 403 and should not be necessary for an ordinary UI sign-out.
      const { error } = await supabase.auth.signOut({ scope: 'local' });
      if (error) throw error;
    } catch (error) {
      console.error('Unable to sign out:', error);
      setSignOutError('Sign out failed. Please retry.');
    }
  };

  if (loading) {
    return <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>Loading...</div>;
  }

  if (!session) {
    return <Auth />;
  }

  if (!accessState || accessState.userId !== userId || accessState.attempt !== accessAttempt) {
    return <div className="main-view" role="status">Loading administrator access...</div>;
  }

  if (accessState.error || !preferredView(accessState.access)) {
    return (
      <section className="main-view">
        <h1>Administrator access unavailable</h1>
        <p role="alert">
          {accessState.error || 'No administrator pages are assigned to your account. Ask an administrator to check your role permissions.'}
        </p>
        <div style={{ display: 'flex', gap: '12px' }}>
          <button type="button" className="banner-action-btn secondary" onClick={() => setAccessAttempt((attempt) => attempt + 1)}>Retry</button>
          <button type="button" className="banner-action-btn secondary" onClick={handleSignOut}>Sign out</button>
        </div>
        {signOutError && <p role="alert">{signOutError}</p>}
      </section>
    );
  }

  return <App key={userId} session={session} access={accessState.access} />;
}
