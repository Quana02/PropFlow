import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const root = resolve('artifacts/authz');
const origin = 'https://127.0.0.1:47301';
const profile = await mkdtemp(join(root, 'chrome-notifications-'));
const fixture = JSON.parse(await readFile(join(root, 'browser-resources.json'), 'utf8'));
const group = process.env.PROPFLOW_NOTIFICATION_GROUP;
const scenarios = {
  ServiceRequests: 'service-requests',
  Operations: 'operations',
  PropertyOperations: 'property-operations',
  AiAuthorization: 'ai-authorization',
  Finance: 'finance',
  Communication: 'communication',
  ReadState: 'read-state',
  Emergency: 'emergency',
  Reconnect: 'reconnect'
};
const browser = spawn(process.env.PROPFLOW_AUTHZ_CHROME ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  ['--headless', '--no-first-run', '--disable-background-networking', '--ignore-certificate-errors',
    ...(group === 'Emergency' ? ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] : []),
    '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { stdio: 'ignore', windowsHide: true });
const exited = once(browser, 'exit');
console.log('Chrome notification PID', browser.pid);
const evidence = { scenario: 'Maintenance assignment through public HTTP and real Blazor recipient UI', checks: {} };
let ws;
let revocationProbe;

try {
  let port;
  for (let attempt = 0; attempt < 100 && !port; attempt++) {
    try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0].trim(); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (!port) await delay(100);
  }
  assert(port, 'Chrome DevTools did not start');
  const browserTarget = await (await fetch('http://127.0.0.1:' + port + '/json/version')).json();
  ws = new WebSocket(browserTarget.webSocketDebuggerUrl);
  await once(ws, 'open');
  let serial = 0;
  const pending = new Map();
  const tabs = new Map();
  ws.addEventListener('message', ({ data }) => {
    const message = JSON.parse(data);
    pending.get(message.id)?.(message);
    const tab = tabs.get(message.sessionId);
    if (!tab) return;
    const event = message.params;
    if (message.method === 'Network.responseReceived' && event.response.status === 200 &&
        event.response.url === origin + '/api/v1/auth/login') tab.loginResponse = event.requestId;
    if (message.method === 'Network.webSocketCreated') {
      const path = new URL(event.url).pathname;
      if (['/hubs/notifications', '/hubs/service-requests', '/hubs/maintenance', '/hubs/announcements', '/hubs/support-chat', '/hubs/emergency-calls'].includes(path))
        tab.sockets.set(event.requestId, path);
    }
    if (message.method === 'Network.webSocketHandshakeResponseReceived' &&
        tab.sockets.has(event.requestId) && event.response.status === 101) tab.openSockets.add(event.requestId);
    if (message.method === 'Network.webSocketClosed') tab.openSockets.delete(event.requestId);
    if (message.method === 'Network.webSocketFrameReceived' && tab.sockets.has(event.requestId)) {
      for (const fragment of event.response.payloadData.split('\x1e').filter(Boolean)) {
        try {
          const frame = JSON.parse(fragment);
          if (frame.target === 'FinanceNotificationCreated') {
            const [notice] = frame.arguments;
            tab.notices.push({ id: notice.id, observedAt: performance.now() });
          }
          if (frame.target === 'ServiceRequestUpdated') tab.requestUpdates.push(frame.arguments);
          if (frame.target === 'NotificationReadStateChanged') tab.readStateUpdates.push(frame.arguments);
          if (['MaintenanceTaskUpdated', 'MaintenanceAssignmentsChanged', 'AssetStatusChanged'].includes(frame.target))
            tab.maintenanceUpdates.push({ target: frame.target, args: frame.arguments });
          if (['AnnouncementPublished', 'SupportChatChanged', 'SupportChatDraft', 'SupportChatTyping', 'EmergencyChanged', 'EmergencySignal'].includes(frame.target))
            tab.communicationUpdates.push({ target: frame.target, args: frame.arguments });
        } catch { /* Non-JSON control frame. */ }
      }
    }
  });
  const call = (method, params = {}, sessionId) => new Promise((resolveCall, reject) => {
    const id = ++serial;
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(Error('Chrome command timed out: ' + method));
    }, 15000);
    pending.set(id, message => {
      clearTimeout(timeout);
      pending.delete(id);
      if (message.error) reject(Error('Chrome command failed: ' + method));
      else resolveCall(message.result);
    });
    ws.send(JSON.stringify({ method, id, params, sessionId }));
  });
  async function until(read, description, limit = 12000) {
    const expires = performance.now() + limit;
    do {
      if (await read()) return;
      await delay(80);
    } while (performance.now() < expires);
    throw Error(description);
  }
  async function openTab(context) {
    const { targetId } = await call('Target.createTarget', { url: 'about:blank', browserContextId: context });
    const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
    const tab = { context, targetId, sessionId, notices: [], readStateUpdates: [], requestUpdates: [], maintenanceUpdates: [],
      communicationUpdates: [], sockets: new Map(), openSockets: new Set() };
    tabs.set(sessionId, tab);
    await call('Network.enable', {}, sessionId);
    if (group === 'Communication') {
      await call('Page.enable', {}, sessionId);
      await call('Page.addScriptToEvaluateOnNewDocument', { source: `(() => {
        const nativeFetch = window.fetch.bind(window);
        let rejectedPath;
        window.failFixtureChatPost = path => { rejectedPath = path; };
        window.fetch = (resource, options) => {
          const url = new URL(resource instanceof Request ? resource.url : resource, location.href);
          if (url.origin === location.origin && url.pathname === rejectedPath &&
              (options?.method ?? resource?.method)?.toUpperCase() === 'POST') {
            rejectedPath = undefined;
            return Promise.resolve(new Response(JSON.stringify({ status: 503, title: 'Gửi tin tạm thời gián đoạn' }), {
              status: 503, headers: { 'Content-Type': 'application/problem+json' }
            }));
          }
          return nativeFetch(resource, options);
        };
      })()` }, sessionId);
    }
    if (group === 'Reconnect') {
      tab.fixtureClientId = randomUUID();
      await call('Page.enable', {}, sessionId);
      await call('Page.addScriptToEvaluateOnNewDocument', { source: `(() => {
        const nativeSocket = window.WebSocket;
        const nativeFetch = window.fetch.bind(window);
        let pendingList;
        window.holdFixtureNotificationList = () => {
          pendingList = Promise.withResolvers();
        };
        window.releaseFixtureNotificationList = () => {
          pendingList?.resolve();
          pendingList = undefined;
        };
        window.fetch = async (resource, ...options) => {
          const url = new URL(resource instanceof Request ? resource.url : resource, location.href);
          if (url.origin === location.origin && url.pathname === '/api/v1/notifications' && pendingList)
            await pendingList.promise;
          return nativeFetch(resource, ...options);
        };
        window.WebSocket = class extends nativeSocket {
          constructor(address, ...args) {
            const url = new URL(address, location.href);
            if (url.host === location.host && url.pathname.startsWith('/hubs/')) {
              url.searchParams.set('propflow_fixture_client', ${JSON.stringify(tab.fixtureClientId)});
            }
            super(url.href, ...args);
          }
        };
      })()` }, sessionId);
    }
    return tab;
  }
  async function evaluate(tab, expression) {
    const state = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, tab.sessionId);
    if (state.exceptionDetails) throw Error('Chrome evaluation failed');
    return state.result.value;
  }
  function navigate(tab, path) {
    tab.openSockets.clear();
    tab.sockets.clear();
    return call('Page.navigate', { url: path === 'about:blank' ? path : origin + path }, tab.sessionId);
  }
  const badge = tab => evaluate(tab, 'Number(document.querySelector(".notification-trigger")?.getAttribute("aria-label")?.match(/\\d+/)?.[0] ?? 0)');
  const hasDot = tab => evaluate(tab, 'Boolean(document.querySelector(".notification-badge"))');
  const framesFor = (tab, id) => tab.notices.filter(notice => notice.id === id);
  const menuItems = tab => evaluate(tab, 'document.querySelectorAll(".notification-item").length');
  const click = (tab, selector) => evaluate(tab, 'document.querySelector(' + JSON.stringify(selector) + ').click()');
  async function snapshot(tab, name) {
    const { data } = await call('Page.captureScreenshot', { format: 'png' }, tab.sessionId);
    await writeFile(join(root, name + '.png'), Buffer.from(data, 'base64'));
  }
  const socketsReady = (tab, path = '/hubs/notifications') => until(
    () => [...tab.openSockets].some(id => tab.sockets.get(id) === path), 'WebSocket missing for ' + path);
  async function login(role) {
    const { browserContextId } = await call('Target.createBrowserContext');
    const tab = await openTab(browserContextId);
    await navigate(tab, '/login');
    await until(() => evaluate(tab, 'Boolean(document.querySelector("#username"))'), 'Login form missing for ' + role, 50000);
    const credentials = [['username', 'phase11_' + role], ['password', process.env.PHASE11_PASSWORD]];
    await evaluate(tab, 'for(const [id,value] of ' + JSON.stringify(credentials) + '){const el=document.getElementById(id); el.value=value; el.dispatchEvent(new Event("change",{bubbles:true}));}document.querySelector("#password").closest("form").requestSubmit()');
    await until(async () => tab.loginResponse && await evaluate(tab, 'Boolean(document.querySelector(".notification-trigger"))'),
      'Login did not load bell for ' + role);
    const response = await call('Network.getResponseBody', { requestId: tab.loginResponse }, tab.sessionId);
    const account = JSON.parse(response.base64Encoded ? Buffer.from(response.body, 'base64') : response.body);
    assert.ok(account.user.id && account.accessToken);
    tab.userId = account.user.id;
    tab.token = account.accessToken;
    await socketsReady(tab);
    return tab;
  }
  function api(tab, path, payload, headers = {}, method = 'POST') {
    const options = { headers: { ...headers, Authorization: 'Bearer ' + tab.token } };
    if (payload) {
      options.method = method;
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(payload);
    }
    return evaluate(tab, 'fetch(' + JSON.stringify(path) + ',' + JSON.stringify(options) +
      ').then(async r=>({status:r.status,data:await r.json().catch(()=>null)}))');
  }
  async function openRevocationProbe(tab) {
    const negotiate = () => fetch(origin + '/hubs/notifications/negotiate?negotiateVersion=1', {
      method: 'POST', headers: { Authorization: 'Bearer ' + tab.token }, signal: AbortSignal.timeout(5000)
    });
    const negotiated = await negotiate();
    assert.equal(negotiated.status, 200);
    const { connectionToken } = await negotiated.json();
    revocationProbe = new WebSocket(origin.replace('https:', 'wss:') + '/hubs/notifications?' +
      new URLSearchParams({ id: connectionToken, access_token: tab.token }));
    function withTimeout(promise, message) {
      return Promise.race([promise, delay(5000, null, { ref: false }).then(() => { throw Error(message); })]);
    }
    const closed = once(revocationProbe, 'close');
    await withTimeout(once(revocationProbe, 'open'), 'Revocation probe did not connect');
    const handshake = once(revocationProbe, 'message');
    revocationProbe.send('{"protocol":"json","version":1}\x1e');
    assert.deepEqual(JSON.parse((await withTimeout(handshake, 'Notification hub handshake timed out'))[0].data.split('\x1e')[0]), {});
    return {
      async verifyRevoked() {
        await withTimeout(closed, 'Notification probe stayed open after session revocation');
        assert.equal((await negotiate()).status, 401);
      }
    };
  }
  const notifications = tab => api(tab, '/api/v1/notifications');
  async function setOffline(tab, offline) {
    await call('Network.emulateNetworkConditions', {
      offline, latency: 0, downloadThroughput: -1, uploadThroughput: -1
    }, tab.sessionId);
    if (offline) {
      const dropped = await fetch(origin + '/__propflow_fixture/socket/' + tab.fixtureClientId,
        { method: 'DELETE', signal: AbortSignal.timeout(5000) });
      assert.equal(dropped.status, 204, 'Fixture proxy did not find a live tab transport');
      await until(() => tab.openSockets.size === 0, 'Socket did not close while the browser was offline');
    }
  }
  const setViewport = (tab, width) => call('Emulation.setDeviceMetricsOverride', {
    width, height: 844, deviceScaleFactor: 1, mobile: false
  }, tab.sessionId);
  async function persistedNotice(tab, sourceId, excludedId) {
    const excludedIds = new Set(Array.isArray(excludedId) ? excludedId : [excludedId]);
    let match;
    await until(async () => {
      const response = await fetch(origin + '/api/v1/notifications', {
        headers: { Authorization: 'Bearer ' + tab.token }, signal: AbortSignal.timeout(4000)
      });
      assert.equal(response.status, 200);
      match = (await response.json()).find(notice => notice.sourceId === sourceId && !excludedIds.has(notice.id));
      return match !== undefined;
    }, 'Expected notification was not persisted');
    return { ...match, observedAt: performance.now() };
  }
  const save = () => writeFile(join(root, 'final-notification-browser.json'), JSON.stringify(evidence, null, 2));
  if (group) {
    assert.ok(Object.hasOwn(scenarios, group), 'Unknown notification browser group');
    const scenario = scenarios[group];
    const { default: verify } = await import(`./notification-${scenario}.mjs`);
    await verify({
      fixture, login, openTab, navigate, evaluate, until, socketsReady, api, badge, notifications, persistedNotice, framesFor,
      click, snapshot, setOffline, setViewport, openRevocationProbe,
      save: checks => writeFile(join(root, `notification-${scenario}.json`), JSON.stringify(checks, null, 2))
    });
    console.log('Chrome notification checks recorded for', group);
  } else {

  const manager = await login('manager');
  let staffA = await login('staff');
  const staffB = await login('staff2');
  const staffATab2 = await openTab(staffA.context);
  await navigate(staffATab2, '/my-maintenance');
  await socketsReady(staffATab2);
  const initialA = await badge(staffA);
  const initialB = await badge(staffB);
  const initialManager = await badge(manager);
  const taskId = fixture.otherTaskId;
  assert.ok(taskId);
  const path = '/api/v1/maintenance/tasks/' + taskId + '/assignment';
  const assignAt = performance.now();
  assert.equal((await api(manager, path, { staffUserId: staffA.userId })).status, 200);
  const assignSucceededAt = performance.now();
  const { id: noticeId, observedAt: assignApiObservedAt } = await persistedNotice(staffA, taskId);
  await until(() => framesFor(staffA, noticeId).length === 1 && framesFor(staffATab2, noticeId).length === 1,
    'STAFF A tabs did not receive the exact assignment on WebSocket');
  await until(async () => (await badge(staffA)) === initialA + 1 && (await badge(staffATab2)) === initialA + 1,
    'STAFF A notification badges did not update');
  assert.equal(await hasDot(staffA), true);
  const assignBadgeObservedAt = performance.now();
  assert.equal(framesFor(staffB, noticeId).length, 0);
  assert.equal(framesFor(manager, noticeId).length, 0);
  assert.equal((await notifications(staffB)).data.some(item => item.sourceId === taskId), false);
  assert.equal(await badge(staffB), initialB);
  assert.equal(await badge(manager), initialManager);
  assert.equal((await api(staffB, '/api/v1/notifications/' + noticeId + '/read', {})).status, 404);
  assert.equal((await api(staffB, '/api/v1/maintenance/my-tasks/' + taskId)).status, 404);
  evidence.checks.firstAssignment = { twoTabs: true, excludedStaffAndManager: true, foreignMarkRead: 404,
    foreignTask: 404, actionToFrameMs: Math.round(framesFor(staffA, noticeId)[0].observedAt - assignAt),
    successToFirstObservedApiMs: Math.round(assignApiObservedAt - assignSucceededAt),
    successToFrameMs: Math.round(framesFor(staffA, noticeId)[0].observedAt - assignSucceededAt),
    successToBothBadgesMs: Math.round(assignBadgeObservedAt - assignSucceededAt) };
  await save();

  await click(staffA, '.notification-trigger');
  await until(async () => (await menuItems(staffA)) === initialA + 1, 'STAFF A bell did not show the assignment');
  assert.equal(await hasDot(staffA), false);
  assert.equal(await badge(staffA), initialA + 1);
  await snapshot(staffA, 'final-staff-assignment-bell');
  const readStartedAt = performance.now();
  await click(staffA, '.notification-item.unread');
  await until(() => evaluate(staffA, 'location.pathname==="/my-maintenance" && location.search.includes("taskId=")'),
    'Clicking a notification did not open the protected route');
  await until(async () => (await badge(staffA)) === initialA, 'Reading the notification did not clear its unread count');
  assert.equal((await notifications(staffA)).data.find(notice => notice.id === noticeId).isRead, true);
  evidence.checks.linkAndRead = true;
  await until(async () => (await badge(staffATab2)) === initialA,
    'STAFF A second tab did not reconcile unread count through the existing pulse', 75000);
  evidence.checks.readReconcilesAcrossTabsMs = Math.round(performance.now() - readStartedAt);
  await save();

  assert.equal((await api(manager, path, { staffUserId: staffB.userId })).status, 200);
  const { id: reassignmentId } = await persistedNotice(staffB, taskId);
  await until(() => framesFor(staffB, reassignmentId).length === 1, 'STAFF B did not receive reassignment');
  assert.equal(framesFor(staffA, reassignmentId).length, 0);
  assert.equal(framesFor(manager, reassignmentId).length, 0);
  assert.equal((await notifications(staffA)).data.filter(item => item.sourceId === taskId).length, 1);
  assert.equal((await api(staffA, '/api/v1/maintenance/my-tasks/' + taskId + '/start', {})).status, 403);
  evidence.checks.reassignment = { newRecipient: true, oldRecipientWrite: 403 };

  const staffAContext = staffA.context;
  assert.equal((await call('Target.closeTarget', { targetId: staffA.targetId })).success, true);
  assert.equal((await call('Target.closeTarget', { targetId: staffATab2.targetId })).success, true);
  await until(async () => (await call('Target.getTargets')).targetInfos.every(target => target.browserContextId !== staffAContext),
    'STAFF A still has a browser target');
  const offline = await api(manager, path, { staffUserId: staffA.userId });
  assert.equal(offline.status, 200);
  const offlineNotice = await persistedNotice(staffA, taskId, noticeId);
  assert.equal(offlineNotice.isRead, false);
  const disconnectedToken = staffA.token;
  staffA = await openTab(staffAContext);
  staffA.token = disconnectedToken;
  await navigate(staffA, '/my-maintenance');
  await socketsReady(staffA);
  await until(() => hasDot(staffA), 'A notification received while offline did not restore the dot');
  await click(staffA, '.notification-trigger');
  await until(async () => {
    const records = (await notifications(staffA)).data;
    return records.filter(notice => notice.id === offlineNotice.id && !notice.isRead).length === 1 &&
      (await badge(staffA)) === initialA + 1;
  }, 'STAFF A did not recover one persisted notification on return');
  assert.equal((await notifications(staffB)).data.filter(notice => notice.sourceId === taskId).length, 1);
  await click(staffA, '.notification-read-all');
  await until(async () => (await badge(staffA)) === 0, 'Read-all did not clear the badge');
  assert.ok((await notifications(staffA)).data.every(notice => notice.isRead));
  evidence.checks.offlineReturnAndReadAll = true;
  await save();

  const resident = await login('resident');
  const residentBefore = await badge(resident);
  const managerBefore = await badge(manager);
  const submittedAt = performance.now();
  const submitted = await api(resident, '/api/v1/resident/service-requests', {
    description: 'Kiểm tra vòi nước ở căn hộ fixture.'
  });
  assert.equal(submitted.status, 201);
  const submissionSucceededAt = performance.now();
  const requestId = submitted.data.id;
  const { id: submissionId, observedAt: submissionApiObservedAt } = await persistedNotice(manager, requestId);
  await until(() => framesFor(manager, submissionId).length === 1, 'Manager did not receive the service request on WebSocket');
  await until(async () => (await badge(manager)) === managerBefore + 1, 'Manager service request badge did not update');
  const submissionBadgeObservedAt = performance.now();
  assert.equal(await badge(resident), residentBefore);
  assert.equal(framesFor(resident, submissionId).length, 0);
  assert.equal(framesFor(staffA, submissionId).length, 0);
  assert.equal(framesFor(staffB, submissionId).length, 0);
  assert.equal((await notifications(staffB)).data.some(notice => notice.sourceId === requestId), false);
  await click(manager, '.notification-trigger');
  await until(() => evaluate(manager, 'document.querySelector(".notification-item.unread")?.textContent.includes("Yêu cầu dịch vụ mới")'),
    'Manager bell did not show the new service request');
  await snapshot(manager, 'final-manager-service-request-bell');
  await click(manager, '.notification-item.unread');
  await until(() => evaluate(manager, 'location.pathname === ' + JSON.stringify('/manager/service-requests/' + requestId)),
    'Service request notice did not open the manager route');
  evidence.checks.serviceRequestSubmission = { recipientManager: true, excludedResidentAndStaff: true,
    link: true, actionToFrameMs: Math.round(framesFor(manager, submissionId)[0].observedAt - submittedAt),
    successToFirstObservedApiMs: Math.round(submissionApiObservedAt - submissionSucceededAt),
    successToFrameMs: Math.round(framesFor(manager, submissionId)[0].observedAt - submissionSucceededAt),
    successToBadgeMs: Math.round(submissionBadgeObservedAt - submissionSucceededAt) };
  await save();

  const accountant = await login('accountant');
  const otherResident = await login('resident2');
  async function stableBadge(tab) {
    let count;
    await until(async () => {
      count = (await api(tab, '/api/v1/notifications/unread-count')).data.count;
      return (await badge(tab)) === count;
    }, 'Notification badge did not reconcile with unread API');
    return count;
  }
  const householdBefore = await stableBadge(resident);
  const otherHouseholdBefore = await stableBadge(otherResident);
  const paymentAt = performance.now();
  const collected = await api(accountant, '/api/v1/payments/collect', {
    invoiceId: fixture.invoiceId, amount: 150000, paymentMethodCode: 'CASH'
  }, { 'Idempotency-Key': 'notification-browser-collection-fixture' });
  assert.equal(collected.status, 200);
  assert.equal(collected.data.status, 'CONFIRMED');
  const paymentSucceededAt = performance.now();
  const { id: paymentNoticeId, observedAt: paymentApiObservedAt } = await persistedNotice(resident, collected.data.id);
  await until(() => framesFor(resident, paymentNoticeId).length === 1, 'Household did not receive payment notification on WebSocket');
  assert.equal(framesFor(otherResident, paymentNoticeId).length, 0);
  await until(async () => (await badge(resident)) === householdBefore + 1, 'Household badge did not show one payment');
  const paymentBadgeObservedAt = performance.now();
  assert.equal(await badge(otherResident), otherHouseholdBefore);
  assert.equal((await notifications(otherResident)).data.some(notice => notice.sourceId === collected.data.id), false);
  await click(resident, '.notification-trigger');
  const paymentItem = 'Array.from(document.querySelectorAll(".notification-item.unread")).find(item=>item.textContent.includes("Thanh toán hóa đơn thành công"))';
  await until(() => evaluate(resident, 'Boolean(' + paymentItem + ')'), 'Household bell did not show confirmation');
  await snapshot(resident, 'final-resident-payment-bell');
  await evaluate(resident, paymentItem + '.click()');
  await until(() => evaluate(resident, 'location.pathname === "/resident/billing" && new URLSearchParams(location.search).get("invoice") === ' +
    JSON.stringify(fixture.invoiceId)), 'Payment notification did not open its invoice');
  evidence.checks.paymentConfirmation = { recipientHousehold: true, excludedOtherResident: true, link: true,
    actionToFrameMs: Math.round(framesFor(resident, paymentNoticeId)[0].observedAt - paymentAt),
    successToFirstObservedApiMs: Math.round(paymentApiObservedAt - paymentSucceededAt),
    successToFrameMs: Math.round(framesFor(resident, paymentNoticeId)[0].observedAt - paymentSucceededAt),
    successToBadgeMs: Math.round(paymentBadgeObservedAt - paymentSucceededAt) };
  await save();

  const independentStaffB = await login('staff2');
  await navigate(staffB, '/account');
  await until(() => evaluate(staffB, 'Boolean(document.querySelector(".logout-button"))'), 'Logout button missing');
  const probe = await openRevocationProbe(staffB);
  await click(staffB, '.logout-button');
  await until(() => evaluate(staffB, 'location.pathname === "/login"'), 'STAFF B did not return to login');
  await until(async () => (await api(staffB, '/api/v1/notifications')).status === 401,
    'STAFF B old bearer still reads notifications after logout');
  await probe.verifyRevoked();
  assert.equal((await notifications(staffA)).status, 200);
  assert.equal((await notifications(independentStaffB)).status, 200);
  const nextAssignment = '/api/v1/maintenance/tasks/' + fixture.taskId + '/assignment';
  assert.equal((await api(manager, nextAssignment, { staffUserId: independentStaffB.userId })).status, 200);
  const { id: nextNoticeId } = await persistedNotice(independentStaffB, fixture.taskId);
  await until(() => framesFor(independentStaffB, nextNoticeId).length === 1, 'Independent session did not receive a new private event');
  assert.equal(framesFor(staffB, nextNoticeId).length, 0);
  evidence.checks.revokedNotificationSession = { oldBearer: 401, socketClosed: true, otherStaff: 200,
    independentSameAccount: 200, independentReceivesNewEvent: true, revokedReceivesNewEvent: false };
  await save();
  console.log('Chrome notification checks recorded');
  }
} finally {
  revocationProbe?.close();
  ws?.close();
  if (browser.exitCode === null && browser.signalCode === null) browser.kill();
  await exited;
  await rm(profile, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 });
  console.log('Chrome notification stopped', browser.pid);
}
