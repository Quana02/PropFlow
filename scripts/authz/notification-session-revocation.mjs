import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export default async function verifySessionRevocation({ login, openTab, navigate, evaluate, until, socketsReady, api, save }) {
  const admin = await login('admin');
  const unaffected = await login('manager');
  const checks = {};
  for (const cause of ['disable', 'role-change', 'password-change']) {
    const target = await login('staff');
    await navigate(target, '/my-maintenance');
    const secondTab = await openTab(target.context);
    await navigate(secondTab, '/my-maintenance');
    await socketsReady(secondTab);
    const independent = await login('staff');
    await navigate(independent, '/my-maintenance');
    const affected = [target, secondTab, independent];
    for (const tab of affected) {
      await socketsReady(tab);
      await socketsReady(tab, '/hubs/maintenance');
    }
    assert.equal((await api(target, '/api/v1/auth/me')).status, 200);
    const csrf = (await api(target, '/api/v1/auth/csrf')).data.token;
    const replacement = 'PF04 ' + randomUUID() + ' secured';
    for (const tab of affected) { tab.authDeniedAt = undefined; tab.notificationClosedAt = undefined; }
    const began = performance.now();
    let result;
    if (cause === 'password-change') {
      result = await api(target, '/api/v1/auth/password/change', {
        currentPassword: process.env.PHASE11_PASSWORD, newPassword: replacement
      }, { 'X-CSRF-TOKEN': csrf });
    } else {
      result = await api(admin, '/api/v1/administration/internal-accounts/' + target.userId +
        (cause === 'disable' ? '/status' : '/role'), cause === 'disable' ? { status: 'DISABLED' } : { role: 'ACCOUNTANT' }, {}, 'PUT');
    }
    assert.equal(result.status, 204);
    const responseMs = Math.round(performance.now() - began);
    await until(async () => {
      for (const tab of affected)
        if (!await evaluate(tab, 'location.pathname === "/login" && Boolean(document.querySelector("#username"))')) return false;
      return true;
    }, 'Revocation did not remove protected UI in both same-SID tabs and the independent affected session', 12000);
    const clientMs = Math.round(performance.now() - began);
    const clientDeniedMs = affected.map(tab => Math.round(tab.authDeniedAt - began));
    const notificationClosedMs = affected.map(tab => Math.round(tab.notificationClosedAt - began));
    assert.ok(clientDeniedMs.every(value => Number.isFinite(value) && value >= 0), 'Missing observed 401 for an affected client');
    assert.ok(clientMs <= 3000, 'Local client revocation exceeded the 3-second engineering target');
    for (const tab of [target, independent]) assert.equal((await api(tab, '/api/v1/auth/me')).status, 401);
    assert.equal((await api(unaffected, '/api/v1/auth/me')).status, 200);
    if (cause !== 'password-change') {
      const restored = await api(admin, '/api/v1/administration/internal-accounts/' + target.userId +
        (cause === 'disable' ? '/status' : '/role'), cause === 'disable' ? { status: 'ACTIVE' } : { role: 'STAFF' }, {}, 'PUT');
      assert.equal(restored.status, 204);
      assert.equal((await api(target, '/api/v1/auth/me')).status, 401);
    }
    const fresh = await login('staff', cause === 'password-change' ? replacement : process.env.PHASE11_PASSWORD);
    assert.equal((await api(fresh, '/api/v1/auth/me')).status, 200);
    await navigate(fresh, '/my-maintenance');
    await socketsReady(fresh, '/hubs/maintenance');
    const oldRefresh = await evaluate(target, `fetch('/api/v1/auth/csrf').then(r=>r.json()).then(csrf=>
      fetch('/api/v1/auth/refresh',{method:'POST',headers:{'X-CSRF-TOKEN':csrf.token}})).then(r=>r.status)`);
    assert.equal(oldRefresh, 401);
    assert.equal((await api(target, '/api/v1/auth/me')).status, 401);
    checks[cause] = { sameSidTabs: 2, independentSessionAlsoRevoked: true, oldBearer401: true,
      oldRefresh401: true, unaffectedAccount200: true, restoredAccessDoesNotReviveOldSession: true,
      newLoginAndWebSocketWork: true, responseMs, clientMs, clientDeniedMs, notificationClosedMs,
      phases: { T3: 'first observed me/refresh401 per affected browser tab', T4: 'all protected tabs show login' } };
    await save(checks);
  }
}
