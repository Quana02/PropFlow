import assert from 'node:assert/strict';

export default async function verifyOperationsNotifications({
  fixture, login, navigate, evaluate, until, socketsReady, api, badge, notifications, persistedNotice, framesFor, click, snapshot, save
}) {
  const manager = await login('manager');
  const staffA = await login('staff');
  const staffB = await login('staff2');
  const owner = await login('resident');
  const other = await login('resident2');
  const checks = {};
  const maintenance = '/api/v1/maintenance';
  const maintenanceHub = '/hubs/maintenance';
  const events = (tab, target) => tab.maintenanceUpdates.filter(event => event.target === target);
  const expected = async (tab, path, payload, status) => {
    const result = await api(tab, path, payload);
    assert.equal(result.status, status, result.data?.code);
    return result.data;
  };
  const receive = async (tab, id, initial) => {
    const notice = await persistedNotice(tab, id);
    await until(() => framesFor(tab, notice.id).length === 1, 'Expected exactly one notice frame');
    await until(async () => (await badge(tab)) === initial + 1, 'Expected one unread notification in the bell');
    return notice;
  };

  if (process.env.PROPFLOW_FE07_B4 !== '1') {
    const ownerBefore = await badge(owner);
    const otherBefore = await badge(other);
    const complaint = await expected(owner, '/api/v1/complaints',
      { subject: 'Phản ánh fixture realtime', description: 'Kiểm tra giao nhận khi đóng phản ánh.' }, 201);
    const complaintApi = '/api/v1/complaints/' + complaint.id;
    const complaintPage = '/resident/complaints/' + complaint.id;
    await expected(manager, complaintApi + '/close', {}, 409);
    assert.equal((await notifications(owner)).data.some(item => item.sourceId === complaint.id), false);
    await expected(manager, complaintApi + '/review', {}, 200);
    await expected(manager, complaintApi + '/resolve', { response: 'Ban quản lý đã xử lý phản ánh fixture.' }, 200);
    await navigate(manager, '/manager/complaints/' + complaint.id);
    const closeButton = 'Array.from(document.querySelectorAll("button")).find(button => button.textContent.includes("Đóng phản ánh và thông báo cư dân"))';
    await until(() => evaluate(manager, 'Boolean(' + closeButton + ')'), 'Manager close button missing');
    await evaluate(manager, closeButton + '.click()');
    const closure = await receive(owner, complaint.id, ownerBefore);
    assert.equal(closure.title, 'Phản ánh đã được giải quyết');
    assert.equal(closure.actionPath, complaintPage);
    assert.equal((await expected(owner, complaintApi, undefined, 200)).status, 'CLOSED');
    await expected(other, complaintApi, undefined, 404);
    assert.equal((await notifications(other)).data.some(item => item.sourceId === complaint.id), false);
    assert.equal(framesFor(other, closure.id).length, 0);
    assert.equal(await badge(other), otherBefore);
    await click(owner, '.notification-trigger');
    await until(() => evaluate(owner, 'document.querySelector(".notification-item.unread")?.innerText.includes("Phản ánh đã được giải quyết")'),
    'Complaint closure missing from the recipient bell');
    await snapshot(owner, 'notification-fe06-closed');
    await click(owner, '.notification-item.unread');
    await until(() => evaluate(owner, 'location.pathname === ' + JSON.stringify(complaintPage)), 'Complaint link did not open');
    checks.complaintClosed = { trigger: 'UI-to-UI', oneNotice: true, otherHousehold: 404, rejectedEarlyClose: 409 };
    await save(checks);
  }

  const assetName = 'Thang máy fixture vận hành';
  const equipment = await expected(manager, '/api/v1/equipments', { name: assetName }, 201);
  await navigate(manager, '/equipment');
  await Promise.all([socketsReady(manager, maintenanceHub), socketsReady(manager), socketsReady(owner)]);
  const rowText = (tab, selector, marker) => evaluate(tab,
    'Array.from(document.querySelectorAll(' + JSON.stringify(selector) + ')).find(row => row.innerText.includes(' +
    JSON.stringify(marker) + '))?.innerText ?? ""');
  await until(async () => Boolean(await rowText(manager, 'tr', assetName)), 'Equipment fixture row missing');
  const beforeAssetA = await badge(owner);
  const beforeAssetB = await badge(other);
  const beforeAssetStaff = await badge(staffA);
  const schedule = await expected(manager, maintenance + '/schedules',
    { title: 'Lịch bảo trì fixture', description: 'Lịch kiểm thử asset cập nhật realtime.', equipmentId: equipment.id,
      plannedStartAt: new Date(Date.now() + 2000).toISOString(), plannedEndAt: new Date(Date.now() + 3600000).toISOString() }, 201);
  assert.ok(schedule.id);
  await until(() => events(manager, 'AssetStatusChanged').some(event => event.args.length === 0), 'Asset workspace invalidation missing', 24000);
  await until(async () => (await rowText(manager, 'tr', assetName)).includes('Đang bảo trì'), 'Equipment row did not refresh without F5');
  const assetNotices = await Promise.all([receive(owner, equipment.id, beforeAssetA), receive(other, equipment.id, beforeAssetB)]);
  for (const notice of assetNotices) {
    assert.equal(notice.sourceType, 'ASSET_STATUS');
    assert.ok(notice.message.includes(assetName));
    assert.equal(notice.actionPath, '/resident?tab=notifications');
  }
  assert.equal(await badge(staffA), beforeAssetStaff);
  assert.equal((await notifications(staffA)).data.some(item => item.sourceId === equipment.id), false);
  await click(other, '.notification-trigger');
  const assetItem = 'Array.from(document.querySelectorAll(".notification-item.unread")).find(item => item.innerText.includes(' +
    JSON.stringify(assetName) + '))';
  await until(() => evaluate(other, 'Boolean(' + assetItem + ')'), 'Asset notice missing from the resident bell');
  await evaluate(other, assetItem + '.click()');
  await until(() => evaluate(other, 'location.pathname === "/resident" && new URLSearchParams(location.search).get("tab") === "notifications"'),
    'Asset notice did not open the general notification tab');
  checks.assetStatus = { trigger: 'API schedule-to-browser', emptyInvalidation: true, equipmentRowUpdated: true };
  checks.assetAvailability = { trigger: 'API schedule-to-browser', twoResidentRecipients: true, excludedStaff: true, generalNotificationLink: true };
  await save(checks);

  const workTitle = 'Công việc fixture vận hành riêng tư';
  const work = await expected(manager, maintenance + '/tasks', { title: workTitle, description: 'Chi tiết riêng tư fixture',
    scheduleId: schedule.id, priorityCode: 'HIGH' }, 201);
  const myTask = maintenance + '/my-tasks/' + work.id;
  const assignment = maintenance + '/tasks/' + work.id + '/assignment';
  await Promise.all([navigate(staffA, '/my-maintenance'), navigate(staffB, '/my-maintenance'),
    navigate(manager, '/maintenance/tasks/' + work.id)]);
  await Promise.all([manager, staffA, staffB].map(tab => socketsReady(tab, maintenanceHub)));
  await until(async () => await evaluate(staffA, 'Boolean(document.querySelector(".smt-tab.is-active"))') &&
    await evaluate(staffB, 'Boolean(document.querySelector(".smt-tab.is-active"))'), 'Maintenance workspaces unavailable');
  assert.deepEqual(await evaluate(staffA, 'Array.from(document.querySelectorAll(".smt-tab")).slice(0,2).map(tab => tab.innerText.trim())'),
    ['Tất cả nhiệm vụ', 'Nhiệm vụ của tôi']);
  assert.equal(await evaluate(staffA, 'document.querySelector(".smt-tab.is-active").innerText.trim()'), 'Nhiệm vụ của tôi');
  await expected(manager, assignment, { staffUserId: staffA.userId }, 200);
  await until(async () => Boolean(await rowText(staffA, '.smt-table tbody tr', workTitle)), 'STAFF A did not see the assigned task');
  const starts = [staffA, staffB, manager].map(tab => tab.maintenanceUpdates.length);
  const residentBeforeWork = await badge(owner);
  const otherBeforeWork = await badge(other);
  await expected(staffA, myTask + '/start', {}, 200);
  await until(async () => (await rowText(staffA, '.smt-table tbody tr', workTitle)).includes('Đang thực hiện'),
    'Staff task row did not update after work started');
  for (const [tab, start] of [[staffA, starts[0]], [manager, starts[2]]])
    await until(() => tab.maintenanceUpdates.slice(start).some(event =>
      event.target === 'MaintenanceTaskUpdated' && event.args.length === 1 && event.args[0] === work.id), 'Expected scoped task update');
  assert.equal(staffB.maintenanceUpdates.slice(starts[1]).some(event => event.args.includes(work.id)), false);
  const workNotices = await Promise.all([receive(owner, work.id, residentBeforeWork), receive(other, work.id, otherBeforeWork)]);
  for (const notice of workNotices) {
    assert.equal(notice.sourceType, 'MAINTENANCE');
    assert.ok(notice.message.includes(assetName));
    assert.ok(!notice.message.includes(workTitle));
  }
  await expected(manager, assignment, { staffUserId: staffB.userId }, 200);
  await until(() => events(staffA, 'MaintenanceAssignmentsChanged').some(event => event.args.length === 0),
    'Former assignee did not get an empty revocation invalidation');
  await until(async () => !await rowText(staffA, '.smt-table tbody tr', workTitle) &&
    Boolean(await rowText(staffB, '.smt-table tbody tr', workTitle)), 'Reassignment did not update staff lists');
  await expected(staffA, myTask, undefined, 404);
  await expected(staffA, myTask + '/progress', { content: 'Bị từ chối' }, 403);
  checks.maintenanceStatus = { trigger: 'API-to-browser', twoResidentRecipients: true, workTitleNotDisclosed: true };
  checks.maintenanceTaskUpdate = { trigger: 'API-to-browser', managerAndAssignee: true, unrelatedStaffExcluded: true, rowUpdated: true };
  checks.maintenanceReassignment = { trigger: 'API-to-browser', emptyInvalidation: true, staffListsUpdated: true, formerRead: 404, formerWrite: 403 };
  await save(checks);

  if (process.env.PROPFLOW_FE07_B4 !== '1') return;
  await expected(staffB, myTask + '/progress', { content: 'Tiến độ riêng của Staff B' }, 200);
  const historical = await expected(staffA, maintenance + '/my-history/' + work.id, undefined, 200);
  assert.equal(historical.assignments.length, 1);
  assert.equal(historical.assignments[0].staffUserId, staffA.userId);
  assert.ok(!historical.timeline.some(event => event.detail === 'Tiến độ riêng của Staff B'));
  const link = maintenance + '/tasks/' + work.id + '/service-request';
  await expected(staffB, link, { serviceRequestId: fixture.requestId }, 403);
  const linked = await expected(manager, link, { serviceRequestId: fixture.requestId }, 200);
  assert.equal(linked.sourceServiceRequestId, fixture.requestId);
  await expected(manager, link, { serviceRequestId: fixture.requestId }, 409);
  checks.b4 = { formerHistory: 200, otherIntervalHidden: true, currentProgress: 200, managerLink: 200, staffLink: 403, linkReplay: 409 };
  await save(checks);
}
