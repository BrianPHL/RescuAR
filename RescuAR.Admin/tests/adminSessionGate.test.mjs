import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createAdminSessionGate } from '../src/services/adminSessionGate.js';

const session = (id = 'admin-a', token = 'token-a') => ({ user: { id }, access_token: token });
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
const deferred = () => {
  let resolve;
  const promise = new Promise((done) => { resolve = done; });
  return { promise, resolve };
};

function setup(initial = Promise.resolve({ data: { session: session() } })) {
  const states = [];
  const requests = [];
  let callback;
  let inCallback = false;
  let unsubscribed = false;
  const client = {
    auth: {
      getSession: () => initial,
      onAuthStateChange: (listener) => {
        callback = listener;
        return { data: { subscription: { unsubscribe() { unsubscribed = true; } } } };
      },
    },
    rpc(name) {
      assert.equal(inCallback, false, 'RPC must run outside the Auth callback lock');
      assert.equal(name, 'web_admin_access');
      const request = deferred();
      requests.push(request);
      return request.promise;
    },
  };
  const gate = createAdminSessionGate(client, (state) => states.push(state));
  gate.start();
  return {
    gate, client, states, requests, get unsubscribed() { return unsubscribed; },
    emit(next, event = 'SIGNED_IN') {
      inCallback = true;
      callback(event, next);
      inCallback = false;
    },
  };
}

test('restored sessions and early focus stay blocked until the backend confirms membership', async () => {
  const snapshot = deferred();
  const fixture = setup(snapshot.promise);
  fixture.gate.refresh(true);
  assert.equal(fixture.states.length, 0);
  snapshot.resolve({ data: { session: session() } });
  await tick();
  await tick();
  assert.equal(fixture.states.at(-1).status, 'checking');
  fixture.requests[0].resolve({ data: true });
  await tick();
  assert.equal(fixture.states.at(-1).status, 'authorized');
  assert.equal(fixture.states.at(-1).session.user.id, 'admin-a');
  fixture.gate.dispose();
});

test('an initial session-read failure can be retried without accepting an unverified session', async () => {
  const fixture = setup(Promise.resolve({ error: { message: 'Storage temporarily unavailable' } }));
  await tick();
  assert.equal(fixture.states.at(-1).status, 'error');
  fixture.client.auth.getSession = async () => ({ data: { session: session() } });
  fixture.gate.refresh();
  assert.equal(fixture.states.at(-1).status, 'checking');
  await tick();
  await tick();
  fixture.requests[0].resolve({ data: true });
  await tick();
  assert.equal(fixture.states.at(-1).status, 'authorized');
  fixture.gate.dispose();
});

test('nonmembers, RPC failures and unexpected membership responses cannot mount the app', async () => {
  for (const [response, expected] of [
    [{ data: false }, 'denied'], [{ data: null }, 'error'],
    [{ data: [{ allowed: true }] }, 'error'], [{ data: true, error: { message: 'failure' } }, 'error'],
  ]) {
    const fixture = setup();
    await tick();
    await tick();
    fixture.requests[0].resolve(response);
    await tick();
    assert.equal(fixture.states.at(-1).status, expected);
    assert.equal(fixture.states.some((state) => state.status === 'authorized'), false);
    fixture.gate.dispose();
  }
});

test('sign-out immediately closes access and ignores an in-flight membership success', async () => {
  const fixture = setup();
  await tick();
  await tick();
  fixture.emit(null, 'SIGNED_OUT');
  fixture.requests[0].resolve({ data: true });
  await tick();
  assert.deepEqual(fixture.states.at(-1), { status: 'signed-out', session: null });
  fixture.gate.dispose();
});

test('changing accounts invalidates pending verification for the previous account', async () => {
  const fixture = setup();
  await tick();
  await tick();
  fixture.emit(session('admin-b', 'token-b'));
  assert.equal(fixture.states.at(-1).status, 'checking');
  await tick();
  fixture.requests[0].resolve({ data: true });
  await tick();
  assert.equal(fixture.states.at(-1).status, 'checking');
  fixture.requests[1].resolve({ data: true });
  await tick();
  assert.equal(fixture.states.at(-1).session.user.id, 'admin-b');
  fixture.gate.dispose();
});

test('a late initial snapshot cannot replace a more recent Auth event', async () => {
  const snapshot = deferred();
  const fixture = setup(snapshot.promise);
  fixture.emit(session('admin-b', 'token-b'));
  assert.equal(fixture.requests.length, 0, 'RPC is deferred until after the callback');
  snapshot.resolve({ data: { session: session('admin-a') } });
  await tick();
  assert.equal(fixture.requests.length, 1);
  fixture.requests[0].resolve({ data: true });
  await tick();
  assert.equal(fixture.states.at(-1).session.user.id, 'admin-b');
  fixture.gate.dispose();
});

test('same-account token refresh keeps the screen stable but closes it if membership is revoked', async () => {
  const fixture = setup();
  await tick();
  await tick();
  fixture.requests[0].resolve({ data: true });
  await tick();
  const count = fixture.states.length;
  fixture.emit(session('admin-a', 'new-token'), 'TOKEN_REFRESHED');
  await tick();
  assert.equal(fixture.states.length, count, 'no temporary app unmount for the verified account');
  fixture.requests[1].resolve({ data: false });
  await tick();
  assert.equal(fixture.states.at(-1).status, 'denied');
  fixture.gate.dispose();
});

test('disposed gates unsubscribe and cannot publish pending verification results', async () => {
  const fixture = setup();
  await tick();
  await tick();
  fixture.gate.dispose();
  const count = fixture.states.length;
  fixture.requests[0].resolve({ data: true });
  fixture.emit(session('admin-b'));
  await tick();
  assert.equal(fixture.states.length, count);
  assert.equal(fixture.unsubscribed, true);
});
