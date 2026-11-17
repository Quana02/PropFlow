import assert from 'node:assert/strict';

export default async function verifyNotificationReadState({
  fixture, login, openTab, navigate, evaluate, until, socketsReady, api, badge, notifications, persistedNotice, framesFor, click, openRevocationProbe, save
}) {
  const manager = await login('manager');
  const staff = await login('staff');
  const peer = await openTab(staff.context);
  await navigate(peer, '/my-maintenance');
  await socketsReady(peer);
  const device = await login('staff');
  const unrelated = await login('staff2');
  const recipients = [staff, peer, device];
  const checks = {};
  const taskPath = id => '/api/v1/maintenance/tasks/' + id + '/assignment';
  const assign = async (id, user) =>
    assert.equal((await api(manager, taskPath(id), { staffUserId: user.userId })).status, 200);
  async function assigned(id, excludeId) {
    const previous = await Promise.all(recipients.map(badge));
    await assign(id, staff);
    const notice = await persistedNotice(staff, id, excludeId);
    await until(async () => recipients.every(tab => framesFor(tab, notice.id).length === 1) &&
      (await Promise.all(recipients.map(badge))).every((value, i) => value === previous[i] + 1),
    'Every active session must show the unread notice');
    return { notice, previous };
  }

  const { notice, previous } = await assigned(fixture.otherTaskId);
  assert.equal((await api(unrelated, '/api/v1/notifications/' + notice.id + '/read', {})).status, 404);
  assert.equal(recipients.every(tab => tab.readStateUpdates.length === 0), true);
  const beforeRead = performance.now();
  await click(staff, '.notification-trigger');
  await until(() => evaluate(staff, 'Boolean(document.querySelector(".notification-item.unread"))'), 'Notification missing from bell');
  await click(staff, '.notification-item.unread');
  await until(async () => (await Promise.all(recipients.map(badge)))
    .every((value, i) => value === previous[i]), 'Other tab and independent device did not synchronize unread count within 3 seconds', 3000);
  checks.markReadMs = Math.round(performance.now() - beforeRead);
  assert.ok(recipients.every(tab => tab.readStateUpdates.length === 1 && tab.readStateUpdates[0].length === 0));
  assert.equal(unrelated.readStateUpdates.length, 0);
  assert.equal((await notifications(staff)).data.find(item => item.id === notice.id).isRead, true);
  assert.equal((await api(staff, '/api/v1/resident/notifications/' + notice.id + '/read', {})).status, 200);
  assert.equal(recipients.every(tab => tab.readStateUpdates.length === 1), true);
  await save(checks);

  await assign(fixture.taskId, unrelated);
  const next = await assigned(fixture.taskId);
  assert.notEqual(next.notice.id, notice.id);
  const beforeAll = performance.now();
  await click(device, '.notification-trigger');
  await until(() => evaluate(device, 'Boolean(document.querySelector(".notification-read-all"))'), 'Read-all action missing');
  await click(device, '.notification-read-all');
  await until(async () => (await Promise.all(recipients.map(badge))).every(value => value === 0),
    'Other tab and independent device did not synchronize read-all within 3 seconds', 3000);
  checks.markAllReadMs = Math.round(performance.now() - beforeAll);
  assert.ok(recipients.every(tab => tab.readStateUpdates.length === 2));
  assert.equal(unrelated.readStateUpdates.length, 0);
  const items = (await notifications(staff)).data;
  assert.equal(items.filter(item => [notice.id, next.notice.id].includes(item.id)).length, 2);
  assert.ok(items.every(item => item.isRead));
  assert.equal((await api(staff, '/api/v1/resident/notifications/read-all', {})).data.updated, 0);
  checks.sameSessionTwoTabs = true;
  checks.independentSession = true;
  checks.unrelatedAccountExcluded = true;
  checks.noDuplicateInvalidationOnReplay = true;
  await save(checks);

  await navigate(device, '/account');
  await until(() => evaluate(device, 'Boolean(document.querySelector(".logout-button"))'), 'Device logout action missing');
  const probe = await openRevocationProbe(device);
  await click(device, '.logout-button');
  await until(async () => (await api(device, '/api/v1/notifications')).status === 401,
    'Revoked device still has notification access');
  await probe.verifyRevoked();
  await assign(fixture.taskId, unrelated);
  await assign(fixture.taskId, staff);
  const last = await persistedNotice(staff, fixture.taskId, next.notice.id);
  await until(async () => framesFor(peer, last.id).length === 1 && (await badge(peer)) === 1, 'Active tab stopped receiving notices');
  assert.equal((await api(staff, '/api/v1/notifications/' + last.id + '/read', {})).status, 200);
  await until(async () => peer.readStateUpdates.length === 3 && (await badge(peer)) === 0, 'Active tab did not receive post-revocation read state', 3000);
  assert.equal(device.readStateUpdates.length, 2);
  assert.equal(framesFor(device, last.id).length, 0);
  checks.revokedSessionExcluded = true;
  await save(checks);

  await assign(fixture.otherTaskId, unrelated);
  await assign(fixture.otherTaskId, staff);
  const seed = await persistedNotice(staff, fixture.otherTaskId, notice.id);
  await until(async () => (await badge(peer)) === 1 && framesFor(peer, seed.id).length === 1,
    'Expected an unread item before racing read-all against the next delivery');
  await assign(fixture.taskId, unrelated);
  const [readAll] = await Promise.all([
    api(staff, '/api/v1/notifications/read-all', {}),
    assign(fixture.taskId, staff)
  ]);
  assert.equal(readAll.status, 200);
  assert.ok(readAll.data.updated >= 1);
  const concurrentNotice = await persistedNotice(staff, fixture.taskId, [next.notice.id, last.id]);
  await until(async () => {
    const state = await api(staff, '/api/v1/notifications/unread-count');
    assert.equal(state.status, 200);
    const counts = await Promise.all([badge(staff), badge(peer)]);
    return framesFor(staff, concurrentNotice.id).length === 1 &&
      framesFor(peer, concurrentNotice.id).length === 1 && peer.readStateUpdates.length === 4 &&
      counts.every(count => count === state.data.count);
  }, 'Read-all and concurrent delivery did not converge to persisted unread count');
  const concurrentRows = (await notifications(staff)).data;
  assert.equal(concurrentRows.find(item => item.id === seed.id)?.isRead, true);
  assert.equal(concurrentRows.filter(item => item.id === concurrentNotice.id).length, 1);
  assert.equal(device.readStateUpdates.length, 2);
  checks.concurrentReadAll = { newRecordPreserved: true, noDuplicate: true, activeBadgesMatchApi: true };
  await save(checks);
}
