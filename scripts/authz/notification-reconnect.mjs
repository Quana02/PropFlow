import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export default async function verifyNotificationReconnect({
  fixture, login, navigate, evaluate, until, socketsReady, api, badge, notifications, persistedNotice, framesFor,
  setOffline, setViewport, click, snapshot, save
}) {
  const manager = await login('manager');
  const staff = await login('staff');
  const resident = await login('resident');
  const accountant = await login('accountant');
  const checks = {};
  const navTime = tab => evaluate(tab, 'performance.timeOrigin');
  const staffPath = '/api/v1/maintenance/tasks/' + fixture.otherTaskId + '/assignment';
  const staffList = tab => evaluate(tab, 'document.querySelector(".smt-table tbody")?.innerText.includes("Công việc kiểm thử B")');
  const pay = async () => {
    const result = await api(accountant, '/api/v1/payments/collect',
      { invoiceId: fixture.invoiceId, amount: 10000, paymentMethodCode: 'CASH' }, { 'Idempotency-Key': randomUUID() });
    assert.equal(result.status, 200, result.data?.code);
    return result.data.id;
  };
  await navigate(staff, '/my-maintenance');
  await Promise.all([socketsReady(staff), socketsReady(staff, '/hubs/maintenance')]);
  await until(() => evaluate(staff, 'Boolean(document.querySelector(".smt-table tbody"))'), 'Maintenance list missing');
  assert.equal(await staffList(staff), false);
  assert.equal((await notifications(staff)).data.length, 0);
  await evaluate(staff, 'window.holdFixtureNotificationList()');
  await click(staff, '.notification-trigger');
  await until(() => evaluate(staff, 'document.querySelector(".notification-state[role=status]")?.textContent.includes("Đang tải thông báo")'),
    'Bell did not show Vietnamese loading state');
  await evaluate(staff, 'window.releaseFixtureNotificationList()');
  await until(() => evaluate(staff, 'document.querySelector(".notification-state")?.textContent.includes("Bạn chưa có thông báo")'),
    'Bell did not show Vietnamese empty state');
  await click(staff, '.notification-close');
  checks.bellStates = { loading: true, empty: true };
  await save(checks);
  await until(async () => {
    const items = (await notifications(resident)).data.filter(item => item.sourceId === fixture.invoiceId);
    return items.some(item => item.title === 'Hóa đơn phí dịch vụ mới') &&
      items.some(item => item.title === 'Thông báo công nợ quá hạn') && (await badge(resident)) >= 2;
  }, 'Seeded invoice reconciliation has not completed before the offline test', 42000);
  const initialCounts = [await badge(staff), await badge(resident)];
  const origins = [await navTime(staff), await navTime(resident)];
  await Promise.all([setOffline(staff, true), setOffline(resident, true)]);
  await click(staff, '.notification-trigger');
  await until(() => evaluate(staff, 'document.querySelector(".notification-error[role=alert]")?.innerText.includes("Thử lại")'),
    'Bell did not show error and retry while offline');
  await click(staff, '.notification-close');
  checks.bellStates.errorAndRetry = true;
  await save(checks);
  const assignment = await api(manager, staffPath, { staffUserId: staff.userId });
  assert.equal(assignment.status, 200, assignment.data?.code);
  const paymentId = await pay();
  const targets = [staff, resident];
  const offlineNotices = await Promise.all([persistedNotice(staff, fixture.otherTaskId), persistedNotice(resident, paymentId)]);
  for (const [i, tab] of targets.entries()) {
    assert.equal(framesFor(tab, offlineNotices[i].id).length, 0);
    assert.equal(await badge(tab), initialCounts[i]);
  }
  await Promise.all(targets.map(tab => setOffline(tab, false)));
  await Promise.all([...targets.map(tab => socketsReady(tab)), socketsReady(staff, '/hubs/maintenance')]);
  for (const [i, tab] of targets.entries()) {
    await until(async () => (await badge(tab)) === initialCounts[i] + 1, 'Persisted notice did not reconcile after reconnect', 18000);
    assert.equal(await navTime(tab), origins[i]);
    const records = (await notifications(tab)).data;
    assert.equal(records.filter(item => item.id === offlineNotices[i].id && !item.isRead).length, 1);
  }
  await until(() => staffList(staff), 'Maintenance workspace did not recover after reconnect', 18000);
  checks.recovered = { internalShell: true, residentPortal: true, sameDocuments: true, maintenanceListRefreshed: true };
  await save(checks);

  const nextPayment = await persistedNotice(resident, await pay());
  await until(() => framesFor(resident, nextPayment.id).length === 1, 'Next payment did not arrive via the reconnected socket');
  await until(async () => (await badge(resident)) === initialCounts[1] + 2, 'Badge did not include both payments');
  assert.equal(resident.openSockets.size > 0, true);
  const notificationConnections = [...resident.openSockets].filter(id => resident.sockets.get(id) === '/hubs/notifications');
  assert.equal(notificationConnections.length, 1);
  checks.nextEvent = { oneFrame: true, oneNotificationSocket: true, badgeReconciled: true };
  await save(checks);

  const ui = [];
  for (const [name, tab] of [['internal', staff], ['resident', resident]]) {
    await click(tab, '.notification-trigger');
    await until(() => evaluate(tab, 'Boolean(document.querySelector(".notification-item time[datetime]"))'), 'Bell timestamp missing');
    for (const width of [390, 768, 1440]) {
      await setViewport(tab, width);
      for (const mode of ['light', 'dark']) {
        await evaluate(tab, 'if(document.documentElement.dataset.theme !== ' + JSON.stringify(mode) + ') window.propFlowTheme.toggle()');
        const measurement = await evaluate(tab, `(() => {
          const panel = document.querySelector(".notification-panel");
          const rect = panel.getBoundingClientRect();
          const shell = getComputedStyle(panel);
          return { fits: rect.left >= -1 && rect.top >= -1 && rect.right <= innerWidth + 1 && rect.bottom <= innerHeight + 1,
            scroll: getComputedStyle(panel.querySelector(".notification-feed")).overflowY,
            surface: shell.backgroundColor, text: shell.color };
        })()`);
        assert.ok(measurement.fits, name + ' panel overflows at ' + width + ' px');
        assert.equal(measurement.scroll, 'auto');
        if (width === 390) await snapshot(tab, 'notification-panel-' + name + '-' + mode);
        ui.push({ shell: name, width, mode, ...measurement });
      }
    }
    await click(tab, '.notification-close');
  }
  checks.bellUi = ui;
  await save(checks);

  const secondStaffSession = await login('staff');
  const retainedUnread = await badge(staff);
  await setOffline(staff, true);
  const read = await api(secondStaffSession, '/api/v1/notifications/' + offlineNotices[0].id + '/read', {});
  assert.equal(read.status, 200, read.data?.code);
  assert.equal(await badge(staff), retainedUnread);
  await setOffline(staff, false);
  await socketsReady(staff);
  await until(async () => (await badge(staff)) === retainedUnread - 1,
    'Missed read-state invalidation did not reconcile after reconnect');
  assert.equal(await navTime(staff), origins[0]);
  checks.missedReadState = { sameDocument: true, authoritativeBadge: true, independentWriterSession: true };
  await save(checks);

  const chat = '/api/v1/support-chat/conversations/' + fixture.conversationId + '/messages';
  await Promise.all([navigate(manager, '/manager/support-chat'), navigate(resident, '/resident/contact-management')]);
  await Promise.all([socketsReady(manager, '/hubs/support-chat'), socketsReady(resident, '/hubs/support-chat')]);
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".manager-inbox .inbox-row:enabled"))'), 'Manager chat inbox unavailable');
  await click(manager, '.manager-inbox .inbox-row');
  const hasChat = text => evaluate(manager, 'document.querySelector(".support-thread")?.innerText.includes(' + JSON.stringify(text) + ')');
  await until(() => hasChat('Tin nhắn kiểm thử'), 'Manager chat conversation did not load');
  const chatOrigin = await navTime(manager);
  await setOffline(manager, true);
  const sent = await api(resident, chat, { clientMessageId: randomUUID(), text: 'Tin nhắn đến khi Manager ngắt kết nối fixture' });
  assert.equal(sent.status, 200, sent.data?.code);
  assert.equal(await hasChat(sent.data.text), false);
  await setOffline(manager, false);
  await socketsReady(manager, '/hubs/support-chat');
  await until(() => hasChat(sent.data.text), 'Chat did not reconcile unread message after reconnect', 18000);
  assert.equal(await navTime(manager), chatOrigin);
  const nextChat = await api(resident, chat, { clientMessageId: randomUUID(), text: 'Tin nhắn trực tiếp sau khi kết nối lại fixture' });
  assert.equal(nextChat.status, 200, nextChat.data?.code);
  await until(() => hasChat(nextChat.data.text), 'Next chat message did not arrive after reconnect');
  assert.equal(await evaluate(manager, 'document.querySelectorAll(' +
    JSON.stringify('[data-message-id="' + sent.data.id + '"]') + ').length'), 1);
  checks.supportChat = { sameDocument: true, missedMessageRecovered: true, nextMessageRealtime: true, noDuplicate: true };
  await save(checks);

  const staffDirectory = await api(manager, '/api/v1/maintenance/tasks/assignable-staff');
  assert.equal(staffDirectory.status, 200);
  const staff2 = staffDirectory.data.find(item => item.username === 'phase11_staff2');
  assert.ok(staff2?.userId);
  const offlineAssignment = await api(manager, '/api/v1/maintenance/tasks/' + fixture.taskId + '/assignment',
    { staffUserId: staff2.userId });
  assert.equal(offlineAssignment.status, 200, offlineAssignment.data?.code);
  const previouslySignedOut = await login('staff2');
  const offlineAssignmentNotice = await persistedNotice(previouslySignedOut, fixture.taskId);
  await until(async () => (await badge(previouslySignedOut)) === 1,
    'New login did not load the notification created while the recipient was signed out');
  assert.equal(framesFor(previouslySignedOut, offlineAssignmentNotice.id).length, 0,
    'Expected persisted catch-up before the recipient had a subscription');
  assert.equal((await notifications(previouslySignedOut)).data.filter(item => item.id === offlineAssignmentNotice.id && !item.isRead).length, 1);
  checks.signedOutRecipient = { recoveredOnLogin: true, oneUnreadRecord: true };
  await save(checks);
}
