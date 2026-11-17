import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';

const origin = 'https://127.0.0.1:47301';
const users = Number(process.env.PROPFLOW_AUTHZ_LOAD_USERS);
assert(Number.isInteger(users) && users >= 1 && users <= 200);
const password = process.env.PHASE11_PASSWORD;
const prefix = '/api/v1/auth/';
const rt = performance;
const sockets = [];

async function call(path, { method = 'GET', headers, body } = {}) {
  return fetch(origin + path, { method, headers, body, redirect: 'manual', signal: AbortSignal.timeout(15_000) });
}

async function session(username) {
  const jar = new Map();
  const save = response => {
    for (const header of response.headers.getSetCookie()) {
      const cookie = header.split(';')[0];
      const name = cookie.slice(0, cookie.indexOf('='));
      if (cookie.endsWith('=')) jar.delete(name);
      else jar.set(name, cookie);
    }
  };
  const cookie = () => [...jar.values()].join('; ');
  const csrf = await call(prefix + 'csrf');
  assert.equal(csrf.status, 200);
  save(csrf);
  const csrfToken = (await csrf.json()).token;
  const csrfCookie = csrf.headers.getSetCookie().find(value => value.startsWith('__Secure-PropFlow.Csrf='));
  assert.match(csrfCookie, /; secure/i);
  assert.match(csrfCookie, /; httponly/i);
  assert.match(csrfCookie, /; samesite=strict/i);

  const post = async (action, body, validCsrf = true) => {
    const headers = { cookie: cookie(), 'content-type': 'application/json' };
    if (validCsrf) headers['X-CSRF-TOKEN'] = csrfToken;
    const response = await call(prefix + action, { method: 'POST', body: JSON.stringify(body), headers });
    save(response);
    return response;
  };
  const login = await post('login', { username, password });
  assert.equal(login.status, 200);
  const refreshCookie = login.headers.getSetCookie().find(value => value.startsWith('__Secure-PropFlow.Refresh='));
  assert.match(refreshCookie, /; secure/i);
  assert.match(refreshCookie, /; httponly/i);
  assert.match(refreshCookie, /; path=\/api\/v1\/auth/i);
  return { post, jwt: (await login.json()).accessToken };
}

const bearer = jwt => ({ Authorization: 'Bearer ' + jwt });
const hubEndpoint = '/hubs/maintenance';
const negotiateEndpoint = hubEndpoint + '/negotiate?negotiateVersion=1';

async function checkLongPolling() {
  const actor = await session('phase11_staff');
  const headers = bearer(actor.jwt);
  const negotiation = await call(negotiateEndpoint, { method: 'POST', headers });
  assert.equal(negotiation.status, 200);
  const details = await negotiation.json();
  assert.ok(details.availableTransports.some(transport => transport.transport === 'LongPolling'));
  const transport = hubEndpoint + '?' + new URLSearchParams({ id: details.connectionToken });
  const open = await call(transport, { headers });
  assert.equal(open.status, 200);
  await open.text();

  const send = async payload => {
    const response = await call(transport, {
      method: 'POST',
      headers: { ...headers, 'content-type': 'text/plain;charset=UTF-8' },
      body: JSON.stringify(payload) + '\x1e'
    });
    assert.equal(response.status, 200);
  };
  const read = async () => {
    const response = await call(transport, { headers });
    assert.equal(response.status, 200);
    return (await response.text()).split('\x1e').filter(Boolean).map(record => JSON.parse(record));
  };
  await send({ protocol: 'json', version: 1 });
  const [handshake] = await read();
  assert.deepEqual(handshake, {});
  await send({ type: 1, invocationId: 'poll-audit', target: 'JoinMaintenanceUpdates', arguments: [] });
  const completed = (await read()).find(message => message.type === 3);
  assert.equal(completed?.invocationId, 'poll-audit');
  assert.equal(completed.error, undefined);
  assert.equal((await actor.post('logout', {})).status, 204);
  const revoked = await call(transport, { headers });
  assert.equal(revoked.status, 401);
  return { authenticatedInvocation: true, revokedPollStatus: revoked.status };
}

async function startSocket(jwt) {
  const negotiate = await call(negotiateEndpoint, { method: 'POST', headers: bearer(jwt) });
  assert.equal(negotiate.status, 200);
  const { connectionToken } = await negotiate.json();
  const socket = new WebSocket(origin.replace('https:', 'wss:') + hubEndpoint + '?' +
    new URLSearchParams({ id: connectionToken, access_token: jwt }));
  sockets.push(socket);
  const pending = new Map();
  let invocation = 0;
  const timeout = () => AbortSignal.timeout(12_000);
  const closed = new Promise(resolve => socket.addEventListener('close', () => resolve(rt.now()), { once: true }));
  socket.addEventListener('message', event => {
    for (const record of event.data.split('\x1e').filter(Boolean)) {
      const message = JSON.parse(record);
      if (message.type === 3) pending.get(message.invocationId)?.(message.error);
      else if (!message.type) pending.get('handshake')?.(message.error);
    }
  });
  function exchange(id, message) {
    return new Promise((resolve, reject) => {
      const signal = timeout();
      const abort = () => { pending.delete(id); reject(new Error('SignalR response timeout')); };
      signal.addEventListener('abort', abort, { once: true });
      pending.set(id, error => {
        pending.delete(id);
        signal.removeEventListener('abort', abort);
        if (error) reject(new Error('SignalR rejected invocation')); else resolve();
      });
      socket.send(JSON.stringify(message) + '\x1e');
    });
  }
  await new Promise((resolve, reject) => {
    const signal = timeout();
    const abort = () => reject(new Error('WebSocket upgrade timeout'));
    signal.addEventListener('abort', abort, { once: true });
    socket.addEventListener('open', () => { signal.removeEventListener('abort', abort); resolve(); }, { once: true });
    socket.addEventListener('error', () => reject(new Error('WebSocket upgrade failed')), { once: true });
  });
  await exchange('handshake', { protocol: 'json', version: 1 });
  return { closed, invoke: () => {
    const id = String(++invocation);
    return exchange(id, { type: 1, invocationId: id, target: 'JoinMaintenanceUpdates', arguments: [] });
  } };
}

function summary(samples) {
  const sorted = samples.slice().sort((a, b) => a - b);
  const at = percentile => Number(sorted[Math.ceil(percentile * sorted.length) - 1].toFixed(2));
  return { samples: sorted.length, p50Ms: at(.50), p95Ms: at(.95), p99Ms: at(.99), maxMs: at(1) };
}

try {
  const shell = await call('/my-maintenance');
  assert.equal(shell.status, 200);
  assert.match(await shell.text(), /_framework\/blazor\.web\.js/);
  assert.equal((await call('/_framework/blazor.web.js')).status, 200);
  assert.equal((await call('/api/v1/auth/me')).status, 401);
  assert.equal((await call('/api/v1/audit-probe-not-found')).status, 404);
  assert.equal((await call(negotiateEndpoint, { method: 'POST' })).status, 401);
  const longPolling = await checkLongPolling();

  const actors = [];
  for (let index = 1; index <= users; index++)
    actors.push(await session('phase11_load' + String(index).padStart(3, '0')));
  const valid = actors[0];
  assert.equal((await valid.post('refresh', {}, false)).status, 400);
  const rotation = await valid.post('refresh', {});
  assert.equal(rotation.status, 200);
  const rotated = (await rotation.json()).accessToken;
  assert.equal((await call('/api/v1/administration/internal-accounts?role=STAFF', { headers: bearer(rotated) })).status, 403);
  const connected = await Promise.all(actors.map(actor => startSocket(actor.jwt)));
  const http = [];
  const hub = [];
  const start = rt.now();
  await Promise.all(actors.map(async (actor, index) => {
    for (let attempt = 0; attempt < 10; attempt++) {
      const httpStart = rt.now();
      const result = await call(prefix + 'me', { headers: bearer(actor.jwt) });
      assert.equal(result.status, 200);
      await result.arrayBuffer();
      http.push(rt.now() - httpStart);
      const hubStart = rt.now();
      await connected[index].invoke();
      hub.push(rt.now() - hubStart);
    }
  }));
  const seconds = (rt.now() - start) / 1000;
  const revocations = [];
  for (const [index, actor] of actors.entries()) {
    const revokeStart = rt.now();
    assert.equal((await actor.post('logout', {})).status, 204);
    const closeAt = await Promise.race([connected[index].closed, delay(2_000, null, { ref: false })]);
    assert.ok(closeAt >= revokeStart, 'Socket must close after its own session is revoked');
    revocations.push(closeAt - revokeStart);
    assert.equal((await call(prefix + 'me', { headers: bearer(actor.jwt) })).status, 401);
    if (connected[index + 1]) await connected[index + 1].invoke();
  }
  assert.equal((await call(prefix + 'me', { headers: bearer(rotated) })).status, 401);
  assert.equal((await call(negotiateEndpoint,
    { method: 'POST', headers: bearer(rotated) })).status, 401);
  await writeFile(`artifacts/authz/hardening-12-proxy-load-${users}.json`, JSON.stringify({
    users, logicalSessions: users, webSockets: users, http: summary(http), hub: summary(hub), failures: 0,
    measurementSeconds: Number(seconds.toFixed(3)),
    mixedOperationsPerSecond: Number(((http.length + hub.length) / seconds).toFixed(2)),
    logoutToCloseMs: Number(revocations[0].toFixed(2)),
    revocations: summary(revocations),
    longPolling,
    topology: 'HTTPS same-origin, loopback proxy, published Release'
  }, null, 2));
} finally {
  for (const socket of sockets) socket.close();
}
