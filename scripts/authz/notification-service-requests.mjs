import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';

export default async function verifyServiceRequestNotifications({
  fixture, login, openTab, navigate, evaluate, until, socketsReady, api, badge, notifications, persistedNotice, framesFor, click, pressKey, setViewport, snapshot, save
}) {
  const manager = await login('manager');
  const staffA = await login('staff');
  const staffB = await login('staff2');
  const owner = await login('resident2');
  const otherResident = await login('resident');
  const id = fixture.otherRequestId;
  const code = 'PHASE11-REQ-B';
  const residentPath = '/resident/service-requests/' + id;
  const staffPath = '/staff/service-requests/' + code;
  const hub = '/hubs/service-requests';
  const hasRequest = tab => evaluate(tab, 'document.querySelector(".staff-request-page")?.innerText.includes(' + JSON.stringify(code) + ')');
  const checks = {};
  const acceptanceUi = process.env.PROPFLOW_FINAL_ACCEPTANCE_UI === '1';
  async function inspectResponsive(tab, selector, name) {
    const measurements = [];
    for (const width of [390, 768, 1440]) for (const theme of ['light', 'dark']) {
      await setViewport(tab, width);
      await evaluate(tab, `if(document.documentElement.dataset.theme!==${JSON.stringify(theme)})window.propFlowTheme.toggle()`);
      // The application animates theme colors for 700 ms; capture the settled theme.
      await delay(800);
      await evaluate(tab, selector === '.cost-consent'
        ? 'document.querySelector(".cost-consent").scrollIntoView({block:"center",behavior:"instant"})'
        : selector === '.pf-dialog[open]' ? 'undefined' : 'window.scrollTo({top:0,behavior:"instant"})');
      const measured = await evaluate(tab, `(() => {
        const element=document.querySelector(${JSON.stringify(selector)});
        const rect=element?.getBoundingClientRect();
        const style=element && getComputedStyle(element);
        return {theme:document.documentElement.dataset.theme,width:innerWidth,
          pageWidth:document.documentElement.scrollWidth,left:rect?.left,right:rect?.right,
          foreground:style?.color,background:style?.backgroundColor,
          focusInside:element?.contains(document.activeElement)};
      })()`);
      assert.equal(measured.theme, theme);
      assert.ok(measured.pageWidth <= width + 2, `${name}: horizontal overflow at ${width}/${theme}`);
      assert.ok(measured.left >= -1 && measured.right <= width + 1, `${name}: panel outside viewport`);
      if (selector === '.pf-dialog[open]') assert.equal(measured.focusInside, true);
      measurements.push(measured);
      await snapshot(tab, `notification-fe05-acceptance-${name}-${width}-${theme}`);
    }
    await setViewport(tab, 1440);
    return measurements;
  }

  await navigate(manager, '/manager/service-requests');
  await navigate(owner, residentPath);
  await navigate(otherResident, '/resident/service-requests/' + fixture.requestId);
  await socketsReady(otherResident, hub);
  await Promise.all([manager, staffA, staffB, owner].map(tab => socketsReady(tab, hub)));
  await socketsReady(owner);
  await until(() => hasRequest(staffB), 'Previous assignee did not see the seeded request');
  assert.equal(await hasRequest(staffA), false);
  const initialA = await badge(staffA);
  const initialB = await badge(staffB);
  const initialOwner = await badge(owner);
  const initialOther = await badge(otherResident);
  const assignment = await api(manager, '/api/v1/manager/service-requests/' + id + '/assignment',
    { staffUserId: staffA.userId, baseServiceCost: 0 }, {}, 'PUT');
  assert.equal(assignment.status, 200, assignment.data?.code);
  const notice = await persistedNotice(staffA, id);
  assert.equal(notice.actionPath, staffPath);
  assert.equal(notice.title, 'Công việc mới được giao');
  await until(() => framesFor(staffA, notice.id).length === 1, 'Assignee did not receive the assignment frame');
  await until(async () => (await badge(staffA)) === initialA + 1, 'Assignee bell did not increment');
  await until(async () => await hasRequest(staffA) && !await hasRequest(staffB), 'Assignment did not invalidate both staff workspaces');
  for (const tab of [staffA, staffB, manager, owner]) {
    assert.ok(tab.requestUpdates.some(args => args.length === 1 && args[0] === id),
      'Expected ID-only invalidation for an affected workspace');
  }
  assert.equal(otherResident.requestUpdates.some(args => args.includes(id)), false);
  assert.equal(framesFor(staffB, notice.id).length, 0);
  assert.equal(await badge(staffB), initialB);
  assert.equal((await notifications(staffB)).data.filter(item => item.sourceId === id).length, 0);
  assert.equal((await api(staffB, '/api/v1' + staffPath)).status, 404);
  assert.equal((await api(staffB, '/api/v1/notifications/' + notice.id + '/read', {})).status, 404);
  await click(staffA, '.notification-trigger');
  await until(() => evaluate(staffA, 'Boolean(document.querySelector(".notification-item.unread"))'), 'Assignment missing from the assignee bell');
  await snapshot(staffA, 'notification-fe05-assignment');
  await click(staffA, '.notification-item.unread');
  await until(() => evaluate(staffA, 'location.pathname === ' + JSON.stringify(staffPath)), 'Assignment link did not open the staff request');
  checks.assignment = { trigger: 'API-to-browser', correctAssignee: true, formerAssigneeCannotRead: true, linked: true };
  checks.workspace = { trigger: 'API-to-browser', formerListRemoved: true, newListUpdated: true, idOnlyFrame: true, excludedHousehold: true };
  await save(checks);

  const staffApi = '/api/v1/staff/service-requests/' + id;
  const accepted = await api(staffA, staffApi + '/accept', {});
  assert.equal(accepted.status, 200, accepted.data?.code);
  const note = 'Thay van nước fixture FE-05';
  const proposal = { amount: 50000, note };
  const denied = await api(staffB, staffApi + '/additional-cost', proposal);
  assert.equal(denied.status, 404, denied.data?.code);
  assert.equal((await notifications(owner)).data.some(item => item.sourceId === id), false);
  const proposed = await api(staffA, staffApi + '/additional-cost', proposal);
  assert.equal(proposed.status, 200, proposed.data?.code);
  assert.equal(proposed.data.status, 'AWAITING_MANAGER');
  const reviewNotice = await persistedNotice(manager, id);
  assert.match(reviewNotice.title, /chi phí phát sinh chờ duyệt/i);
  assert.equal((await notifications(owner)).data.some(item => item.sourceId === id), false);
  assert.equal((await api(owner, '/api/v1' + residentPath + '/additional-cost/response',
    { proposalId: proposed.data.proposalId, accepted: true })).status, 409);
  assert.equal((await api(staffA, staffApi + '/progress', {detail:'Chưa được cư dân đồng ý'})).status, 409);
  await navigate(manager, '/manager/service-requests/' + id);
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".cost-review-notice button"))'),
    'Manager cost review action did not render');
  await evaluate(manager, 'if(document.documentElement.dataset.theme!=="dark")window.propFlowTheme.toggle()');
  await snapshot(manager, 'notification-fe05-manager-cost-review');
  if (acceptanceUi) await evaluate(manager, 'document.querySelector(".cost-review-notice button").focus()');
  await click(manager, '.cost-review-notice button');
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".pf-dialog__primary"))'),
    'Manager approval confirmation dialog did not open');
  await snapshot(manager, 'notification-fe05-manager-cost-confirmation');
  if (acceptanceUi) {
    checks.managerDialogUi = await inspectResponsive(manager, '.pf-dialog[open]', 'manager-dialog');
    await pressKey(manager, 'Escape');
    await until(() => evaluate(manager, '!document.querySelector(".pf-dialog[open]")'), 'Escape did not cancel approval');
    await save(checks);
    await until(() => evaluate(manager, 'Boolean(document.activeElement.closest(".cost-review-notice"))'), 'Dialog did not restore focus to the review action');
    assert.equal((await notifications(owner)).data.some(item => item.sourceId === id), false,
      'Cancel must not approve or notify the resident');
    await click(manager, '.cost-review-notice button');
    await until(() => evaluate(manager, 'Boolean(document.querySelector(".pf-dialog[open]"))'), 'Approval dialog did not reopen');
    checks.managerCancelPreservedProposal = true;
  }
  await click(manager, '.pf-dialog__primary');
  await until(() => evaluate(manager, 'document.querySelector(".cost-review-notice")?.innerText.includes("Đã gửi báo giá")'),
    'Manager review did not send quote to resident');
  const costNotice = await persistedNotice(owner, id);
  await evaluate(owner, 'if(document.documentElement.dataset.theme!=="light")window.propFlowTheme.toggle()');
  assert.equal(costNotice.actionPath, residentPath);
  assert.match(costNotice.title, /chi phí phát sinh/i);
  assert.ok(costNotice.message.includes(note));
  await until(() => framesFor(owner, costNotice.id).length === 1, 'Owner did not receive the cost proposal frame');
  await until(async () => (await badge(owner)) === initialOwner + 1, 'Owner bell did not increment');
  await until(() => evaluate(owner, 'document.querySelector(".cost-consent")?.innerText.includes(' + JSON.stringify(note) + ')'),
    'Resident workspace did not render the pending consent without a reload');
  assert.equal((await api(otherResident, '/api/v1' + residentPath)).status, 404);
  assert.equal((await notifications(otherResident)).data.some(item => item.sourceId === id), false);
  assert.equal(framesFor(otherResident, costNotice.id).length, 0);
  assert.equal(await badge(otherResident), initialOther);
  if (acceptanceUi) checks.residentConsentUi = await inspectResponsive(owner, '.cost-consent', 'resident-consent');
  await click(owner, '.notification-trigger');
  await until(() => evaluate(owner, 'document.querySelector(".notification-item.unread")?.innerText.includes("chi phí phát sinh")'),
    'Owner bell did not render the proposal');
  await snapshot(owner, 'notification-fe05-cost');
  checks.additionalCost = { trigger: 'API-to-browser', managerReviewRequired:true,managerUiApproval:true,
    residentBeforeReview:409,staffProgressBeforeConsent:409,ownerOnly: true, correctLink: true, pendingConsentUpdated: true, formerStaffDenied: 404 };
  await save(checks);

  if (process.env.PROPFLOW_FE05_B3 !== '1') return;
  const consentPath = '/api/v1' + residentPath + '/additional-cost/response';
  const consent = { proposalId: proposed.data.proposalId, accepted: true };
  assert.equal((await api(otherResident, consentPath, consent)).status, 404);
  assert.equal((await api(owner, consentPath, consent)).status, 200);
  assert.equal((await api(staffA, staffApi + '/progress', { detail: 'Đã kiểm tra đường ống' })).status, 200);
  const result = { workResult: 'Đã sửa van nước và kiểm tra rò rỉ.', additionalCost: proposal.amount, additionalCostNote: note };
  assert.equal((await api(staffA, staffApi + '/complete', result)).status, 200);
  const workButton = label => 'Array.from(document.querySelectorAll(".progress-card button")).some(button => !button.disabled && button.textContent.trim() === ' +
    JSON.stringify(label) + ')';
  await until(() => evaluate(staffA, workButton('Bổ sung ảnh bằng chứng')),
    'Staff did not see the current result waiting for evidence');
  const assignmentPath = '/api/v1/manager/service-requests/' + id + '/assignment';
  const assign = assignee => api(manager, assignmentPath, { staffUserId: assignee.userId }, {}, 'PUT');
  assert.equal((await assign(staffB)).status, 200);
  await until(() => evaluate(staffA, 'Boolean(document.querySelector(".work-page-state.is-error"))'),
    'Former staff can still operate the detail workspace');
  assert.equal((await api(staffA, staffApi + '/progress', { detail: 'Tiến độ từ nhân sự cũ' })).status, 404);
  assert.equal((await api(staffA, staffApi + '/complete', result)).status, 404);
  await navigate(staffB, staffPath);
  await socketsReady(staffB, hub);
  assert.equal((await api(staffB, staffApi + '/accept', {})).status, 200);
  await until(() => evaluate(staffB, workButton('Ghi kết quả và tải ảnh')),
    'New assignee has no work action after accepting the request');
  assert.equal(await evaluate(staffB, workButton('Bổ sung ảnh bằng chứng')), false);
  assert.equal((await assign(staffA)).status, 200);
  await navigate(staffA, staffPath);
  await socketsReady(staffA, hub);
  assert.equal((await api(staffA, staffApi + '/accept', {})).status, 200);
  await until(() => evaluate(staffA, workButton('Ghi kết quả và tải ảnh')),
    'Reassigned staff is prompted for evidence for an older assignment');
  assert.equal(await evaluate(staffA, workButton('Bổ sung ảnh bằng chứng')), false);
  await snapshot(staffA, 'b3-fe05-current-assignment');
  assert.equal((await api(staffA, staffApi + '/complete', result)).status, 200);
  await until(() => evaluate(staffA, workButton('Bổ sung ảnh bằng chứng')),
    'Staff did not see the result for the new assignment');
  checks.b3 = { ownerConsent: 200, otherHousehold: 404, currentProgress: 200, formerProgress: 404,
    formerCompletion: 404, oldResultHiddenFromActions: true, newAssignmentResult: 200 };
  if (acceptanceUi) {
    checks.roleUi = {};
    const admin = await login('admin');
    const accountant = await login('accountant');
    checks.financeDashboard = [];
    await until(() => evaluate(accountant, 'document.querySelector(".finance-metric .metric-value")?.innerText.includes("₫")'), 'Finance dashboard still loading');
    for (const [kind, path, amountField] of [
      ['pending', '/api/v1/payments/pending', 'amount'],
      ['invoices', '/api/v1/billing/invoices/unpaid', 'outstandingAmount'],
      ['overdue', '/api/v1/billing/debts/overdue', 'outstandingAmount'],
      ['confirmed', '/api/v1/payments/confirmed', 'amount']
    ]) {
      const response = await api(accountant, path + '?page=1&pageSize=20');
      assert.equal(response.status, 200);
      const expectedAmount = response.data.items.reduce((sum, item) => sum + item[amountField], 0);
      const metric = await evaluate(accountant, `(() => {const card=document.querySelector('.finance-metric.${kind}');return {
        amount:Number(card.querySelector('.metric-value').textContent.replace(/[^0-9]/g,'')),
        count:Number(card.querySelector('.metric-note').textContent.match(/\\d+/)?.[0])};})()`);
      assert.equal(metric.amount, expectedAmount);
      assert.equal(metric.count, response.data.total);
      checks.financeDashboard.push({kind, total:metric.count, amount:metric.amount, matchesScopedApi:true});
    }
    for (const [role, tab] of Object.entries({ admin, manager, staff: staffA, accountant, resident: owner })) {
      checks.roleUi[role] = await inspectResponsive(tab, 'body', role);
      assert.ok(await evaluate(tab, 'Boolean(document.querySelector(".notification-trigger"))'));
    }
    const publicTab = await openTab();
    checks.publicUi = {};
    for (const route of ['/terms', '/privacy', '/help', '/contact']) {
      await navigate(publicTab, route);
      await until(() => evaluate(publicTab, 'Boolean(document.querySelector(".information-main h1"))'), 'Public route missing: ' + route);
      checks.publicUi[route] = await inspectResponsive(publicTab, '.information-main', route.slice(1));
      assert.ok(await evaluate(publicTab, '["/terms","/privacy","/help","/contact"].every(path=>Array.from(document.querySelectorAll("a")).some(a=>a.pathname===path))'));
    }
    await save(checks);
    await navigate(publicTab, '/help');
    await until(() => evaluate(publicTab, 'Boolean(document.querySelector("#faq-search"))'), 'FAQ search missing');
    await evaluate(publicTab, 'const input=document.querySelector("#faq-search");input.value="quen mat khau";input.dispatchEvent(new Event("input",{bubbles:true}))');
    await until(() => evaluate(publicTab, 'document.querySelector(".help-group summary")?.textContent.includes("Quên mật khẩu")'), 'FAQ accent-insensitive search failed');
    await navigate(publicTab, '/register');
    await until(() => evaluate(publicTab, 'Boolean(document.querySelector(".auth-register-form:not([inert]) #reg-name"))'), 'Register form missing');
    await evaluate(publicTab, 'const input=document.querySelector("#reg-name");input.value="Local acceptance draft";input.dispatchEvent(new Event("change",{bubbles:true}))');
    assert.equal(await evaluate(publicTab, 'document.querySelector(' + JSON.stringify('.auth-register-form a[href="/terms"]') + ')?.target'), '_blank');
    await click(publicTab, '.auth-register-form a[href="/terms"]');
    assert.equal(await evaluate(publicTab, 'document.querySelector("#reg-name").value'), 'Local acceptance draft');
    checks.faqSearchAndRegisterDraft = true;
    checks.publicAnonymous = true;
  }
  await save(checks);
}
