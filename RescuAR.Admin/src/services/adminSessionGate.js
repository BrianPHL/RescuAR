// Auth callbacks stay synchronous; RPCs run after the Auth lock is released.
export function createAdminSessionGate(client, onState) {
  let active = true;
  let revision = 0;
  let currentSession = null;
  let sessionKnown = false;
  let state = { status: 'checking', session: null };
  let subscription;

  const publish = (next) => {
    if (active) {
      state = next;
      onState(next);
    }
  };

  function check(session, preserveAccess = false) {
    if (!active) return;
    const ticket = ++revision;
    sessionKnown = true;
    currentSession = session;
    if (!session?.user?.id || !session.access_token) {
      publish({ status: 'signed-out', session: null });
      return;
    }
    const sameVerifiedUser = preserveAccess && state.status === 'authorized'
      && state.session.user.id === session.user.id;
    if (!sameVerifiedUser) publish({ status: 'checking', session: null });
    // Do not invoke a Supabase API from inside onAuthStateChange.
    setTimeout(async () => {
      if (!active || ticket !== revision) return;
      try {
        const { data, error } = await client.rpc('web_admin_access');
        if (!active || ticket !== revision) return;
        if (error || typeof data !== 'boolean') throw new Error('Access verification failed');
        publish(data ? { status: 'authorized', session } : { status: 'denied', session: null });
      } catch {
        if (active && ticket === revision) publish({ status: 'error', session: null });
      }
    }, 0);
  }

  function readSession(ticket) {
    Promise.resolve().then(() => client.auth.getSession()).then(({ data, error }) => {
      if (!active || revision !== ticket) return;
      if (error) publish({ status: 'error', session: null });
      else check(data?.session);
    }).catch(() => {
      if (active && revision === ticket) publish({ status: 'error', session: null });
    });
  }

  return {
    start() {
      const initialRevision = revision;
      const result = client.auth.onAuthStateChange((_event, session) => check(session, true));
      subscription = result.data.subscription;
      readSession(initialRevision);
    },
    refresh(preserveAccess = false) {
      if (sessionKnown) check(currentSession, preserveAccess);
      else if (state.status === 'error') {
        publish({ status: 'checking', session: null });
        readSession(revision);
      }
    },
    dispose() {
      active = false;
      revision++;
      subscription?.unsubscribe();
    },
  };
}
