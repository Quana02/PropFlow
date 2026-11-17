import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export default async function verifyFinanceNotifications({
  fixture, login, evaluate, until, api, badge, notifications, framesFor, click, snapshot, save
}) {
  const accountant = await login('accountant');
  const manager = await login('manager');
  const owner = await login('resident');
  const other = await login('resident2');
  const checks = {};
  const invoiceId = fixture.invoiceId;
  const residentInvoice = '/resident/billing?invoice=' + invoiceId;
  const invoiceApi = '/api/v1/resident/billing/invoices/' + invoiceId + '/print';
  const expected = async (tab, path, payload, status, headers) => {
    const result = await api(tab, path, payload, headers);
    assert.equal(result.status, status, result.data?.code);
    return result.data;
  };
  const records = async tab => (await notifications(tab)).data;
  const waitForNotice = async (tab, before, predicate, deadline = 12000) => {
    let found;
    await until(async () => {
      found = (await records(tab)).find(notice => !before.has(notice.id) && predicate(notice));
      return Boolean(found);
    }, 'Expected finance notification was not persisted', deadline);
    await until(() => framesFor(tab, found.id).length === 1, 'Expected exactly one finance frame');
    await until(async () => (await badge(tab)) === (await expected(tab, '/api/v1/notifications/unread-count', undefined, 200)).count,
      'Finance bell did not reconcile the authoritative count');
    return found;
  };
  const ids = async tab => new Set((await records(tab)).map(item => item.id));
  const invoiceNotices = async tab => (await records(tab)).filter(item => item.sourceId === invoiceId);
  const assertExcluded = async notice => {
    assert.equal(framesFor(other, notice.id).length, 0);
    assert.equal((await records(other)).some(item => item.sourceId === notice.sourceId), false);
  };

  const contact = 'contact-phase11@example.invalid';
  const bill = await expected(accountant, '/api/v1/billing/cycle/invoices/' + invoiceId, undefined, 200);
  assert.ok(!JSON.stringify(bill).includes(contact));
  await expected(other, invoiceApi, undefined, 404);
  const beforeSend = await ids(owner);
  const sendKey = { 'Idempotency-Key': randomUUID() };
  const sendPath = '/api/v1/billing/cycle/invoices/' + invoiceId + '/send';
  assert.equal((await expected(accountant, sendPath, {}, 200, sendKey)).recipients, 1);
  const sent = await waitForNotice(owner, beforeSend, item => item.sourceId === invoiceId && item.title === 'Hóa đơn đã phát hành');
  assert.equal(sent.actionPath, residentInvoice);
  await assertExcluded(sent);
  await expected(accountant, sendPath, {}, 200, sendKey);
  assert.equal((await invoiceNotices(owner)).filter(item => item.title === sent.title && !beforeSend.has(item.id)).length, 1);
  checks.invoiceSend = { trigger: 'API-to-browser', oneRecipient: true, repeatDidNotDuplicate: true, accountantDetailOmitsFixtureEmail: true };
  await save(checks);

  const reminderPath = '/api/v1/billing/cycle/reminders';
  const reminder = { invoiceIds: [invoiceId], requestId: randomUUID() };
  const beforeReminder = await ids(owner);
  assert.equal((await expected(accountant, reminderPath, reminder, 200)).recipients, 1);
  const reminded = await waitForNotice(owner, beforeReminder, item => item.sourceId === invoiceId && item.title === 'Thông báo công nợ quá hạn');
  assert.equal(reminded.actionPath, residentInvoice);
  await assertExcluded(reminded);
  const reminderCount = (await invoiceNotices(owner)).filter(item => item.title === reminded.title).length;
  await expected(accountant, reminderPath, reminder, 200);
  assert.equal((await invoiceNotices(owner)).filter(item => item.title === reminded.title).length, reminderCount);
  checks.manualReminder = { trigger: 'API-to-browser', householdOnly: true, repeatDidNotDuplicate: true };
  await save(checks);

  const beforePending = await ids(owner);
  const claim = await expected(owner, '/api/v1/resident/payments', {
    invoiceId, amount: 250000, paymentMethodCode: 'BANK_TRANSFER', referenceNumber: 'FIXTURE-CLAIM'
  }, 200, { 'Idempotency-Key': randomUUID() });
  assert.equal(claim.status, 'PENDING');
  const pending = await waitForNotice(owner, beforePending, item => item.sourceId === claim.id);
  assert.equal(pending.title, 'Thanh toán đang chờ xác minh');
  assert.equal(pending.actionPath, residentInvoice);
  await assertExcluded(pending);
  checks.paymentPending = { trigger: 'API-to-browser', correctState: true, householdOnly: true };
  await save(checks);

  const beforeRejection = await ids(owner);
  const reason = 'Mã giao dịch fixture không khớp';
  const reject = '/api/v1/payments/' + claim.id + '/reject';
  await expected(manager, reject, { reason }, 403, { 'Idempotency-Key': randomUUID() });
  const rejectKey = { 'Idempotency-Key': randomUUID() };
  assert.equal((await expected(accountant, reject, { reason }, 200, rejectKey)).status, 'REJECTED');
  const rejected = await waitForNotice(owner, beforeRejection, item => item.sourceId === claim.id);
  assert.equal(rejected.title, 'Khoản thanh toán chưa được chấp nhận');
  assert.ok(rejected.message.includes(reason));
  assert.equal(rejected.actionPath, residentInvoice);
  await assertExcluded(rejected);
  await expected(accountant, reject, { reason }, 200, rejectKey);
  assert.equal((await records(owner)).filter(item => item.sourceId === claim.id && item.title === rejected.title).length, 1);
  await click(owner, '.notification-trigger');
  const rejectedItem = 'Array.from(document.querySelectorAll(".notification-item.unread")).find(item => item.innerText.includes(' +
    JSON.stringify(reason) + '))';
  await until(() => evaluate(owner, 'Boolean(' + rejectedItem + ')'), 'Rejection not shown in the bell');
  await snapshot(owner, 'notification-fe09-rejected');
  await evaluate(owner, rejectedItem + '.click()');
  await until(() => evaluate(owner, 'location.pathname + location.search === ' + JSON.stringify(residentInvoice)),
    'Payment rejection did not open the linked invoice');
  checks.paymentRejected = { trigger: 'API-to-browser', reasonShown: true, householdOnly: true, wrongRole: 403, link: true, repeatDidNotDuplicate: true };
  await save(checks);

  const beforePricing = await Promise.all([ids(owner), ids(other)]);
  const localMonth = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit' })
    .format(new Date()) + '-01';
  const rate = { unitRate: 10000, effectiveFrom: localMonth };
  await expected(accountant, '/api/v1/billing/cycle/rates', rate, 403);
  await expected(manager, '/api/v1/billing/cycle/rates', rate, 200);
  const tabs = [owner, other];
  const priceNotices = await Promise.all(tabs.map((tab, i) =>
    waitForNotice(tab, beforePricing[i], item => item.sourceType === 'PRICING')));
  for (const notice of priceNotices) {
    assert.ok(notice.message.includes('10.000'));
    assert.equal(notice.actionPath, '/resident?tab=notifications');
    assert.equal(framesFor(accountant, notice.id).length, 0);
    assert.equal(framesFor(manager, notice.id).length, 0);
  }
  checks.serviceRateChanged = { trigger: 'API-to-browser', twoResidentRecipients: true, accountantDenied: 403, noPrivateResourceLink: true };
  await save(checks);

  const issued = await Promise.all(tabs.map((tab, i) =>
    waitForNotice(tab, beforePricing[i], item => item.sourceType === 'INVOICE' &&
      ![fixture.invoiceId, fixture.otherInvoiceId].includes(item.sourceId) && item.title === 'Hóa đơn phí dịch vụ mới')));
  assert.notEqual(issued[0].sourceId, issued[1].sourceId);
  for (const [i, notice] of issued.entries()) {
    assert.equal(notice.actionPath, '/resident/billing?invoice=' + notice.sourceId);
    const doc = '/api/v1/resident/billing/invoices/' + notice.sourceId + '/print';
    await expected(tabs[i], doc, undefined, 200);
    await expected(tabs[1 - i], doc, undefined, 404);
    assert.equal(framesFor(tabs[1 - i], notice.id).length, 0);
  }
  checks.monthlyServiceInvoice = { trigger: 'API-to-browser', distinctHouseholdInvoices: true, ownLink200: true, crossHousehold404: true };
  await save(checks);

  const beforeRent = await Promise.all(tabs.map(ids));
  await expected(manager, '/api/v1/billing/rental-pricing', {
    apartmentUnitId: fixture.apartmentId, basis: 'MONTHLY', unitRate: 5100000, effectiveFrom: localMonth
  }, 200);
  const rent = await waitForNotice(owner, beforeRent[0], item => item.title === 'Thông báo đơn giá thuê căn hộ');
  assert.ok(rent.message.includes('5.100.000'));
  assert.equal(rent.actionPath, '/resident?tab=notifications');
  const rentInvoice = await waitForNotice(owner, beforeRent[0], item => item.title === 'Hóa đơn tiền thuê tháng mới', 42000);
  assert.equal(rentInvoice.actionPath, '/resident/billing?invoice=' + rentInvoice.sourceId);
  await assertExcluded(rentInvoice);
  await expected(other, '/api/v1/resident/billing/invoices/' + rentInvoice.sourceId + '/print', undefined, 404);
  checks.rentalInvoiceAndPrice = { trigger: 'API worker-to-browser', correctRentNotice: true, exactInvoiceHousehold: true };
  await save(checks);

  await expected(accountant, '/api/v1/payments/collect',
    { invoiceId, amount: 700000, paymentMethodCode: 'CASH' }, 200, { 'Idempotency-Key': randomUUID() });
  const beforePaidReminder = (await invoiceNotices(owner)).filter(item => item.title === reminded.title).length;
  const paidReminder = await expected(accountant, reminderPath, { invoiceIds: [invoiceId], requestId: randomUUID() }, 200);
  assert.equal(paidReminder.recipients, 0);
  assert.equal((await invoiceNotices(owner)).filter(item => item.title === reminded.title).length, beforePaidReminder);
  checks.paidInvoiceExcluded = { trigger: 'API-to-browser', recipients: 0, noExtraReminder: true };
  await save(checks);

  const beforeApproval = await ids(owner);
  const approvePath = '/api/v1/manager/service-requests/' + fixture.requestId + '/resolution/approve';
  const approval = { approvedAdditionalCost: 0, note: 'Nghiệm thu kết quả và ảnh fixture' };
  await expected(accountant, approvePath, approval, 403);
  const approved = await expected(manager, approvePath, approval, 200);
  assert.equal(approved.status, 'RESOLVED');
  assert.ok(approved.serviceInvoiceId);
  const requestInvoice = await waitForNotice(owner, beforeApproval, item => item.sourceId === approved.serviceInvoiceId &&
    item.title === 'Hóa đơn yêu cầu dịch vụ mới', 42000);
  assert.ok(requestInvoice.message.includes('50.000'));
  const requestInvoicePath = '/resident/billing?invoice=' + approved.serviceInvoiceId;
  assert.equal(requestInvoice.actionPath, requestInvoicePath);
  const requestPrint = '/api/v1/resident/billing/invoices/' + approved.serviceInvoiceId + '/print';
  await expected(owner, requestPrint, undefined, 200);
  await expected(other, requestPrint, undefined, 404);
  await assertExcluded(requestInvoice);
  await click(owner, '.notification-trigger');
  const requestInvoiceItem = 'Array.from(document.querySelectorAll(".notification-item")).find(item => item.innerText.includes("Hóa đơn yêu cầu dịch vụ mới"))';
  await until(() => evaluate(owner, 'Boolean(' + requestInvoiceItem + ')'), 'Request invoice missing from the recipient bell');
  await evaluate(owner, requestInvoiceItem + '.click()');
  await until(() => evaluate(owner, 'location.pathname + location.search === ' + JSON.stringify(requestInvoicePath)),
    'Request invoice notification did not open the linked invoice');
  checks.serviceRequestInvoice = { managerApproval: true, ownerOnly: true, incorrectRolesDenied: true, clickedInvoiceLink: true };
  await save(checks);
}
