import assert from 'node:assert/strict';

export default async function verifyPropertyOperations({
  fixture, login, navigate, evaluate, until, click, api, persistedNotice, framesFor, save
}) {
  const manager = await login('manager');
  const staff = await login('staff');
  const accountant = await login('accountant');
  const owner = await login('resident');
  const other = await login('resident2');
  const checks = {};
  const encode = JSON.stringify;
  const expected = async (tab, path, status, body, method = 'POST') => {
    const result = await api(tab, '/api/v1/' + path, body, {}, method);
    assert.equal(result.status, status, path + ' ' + (result.data?.code ?? ''));
    return result.data;
  };
  const element = (selector, value) =>
    'Array.from(document.querySelectorAll(' + encode(selector) + ')).find(e => e.textContent.trim().includes(' + encode(value) + '))';
  const chooseButton = async (tab, text, selector = 'button') => {
    const button = element(selector, text);
    await until(() => evaluate(tab, 'Boolean(' + button + ' && !' + button + '.disabled)'), 'Button missing: ' + text);
    await evaluate(tab, button + '.click()');
  };
  const fill = (tab, selector, value) => evaluate(tab,
    '(()=>{const el=document.querySelector(' + encode(selector) + ');el.value=' + encode(value) +
    ';el.dispatchEvent(new Event("change",{bubbles:true}));})()');
  const visible = (tab, selector, text) =>
    until(() => evaluate(tab, 'Boolean(' + element(selector, text) + ')'), 'Expected visible text: ' + text);

  const resident = 'residents/' + fixture.residentId;
  const profile = await expected(manager, resident, 200);
  const residentName = 'Cư dân Chrome B6';
  for (const reader of [staff, accountant]) {
    await navigate(reader, '/' + resident);
    await visible(reader, 'h1', profile.fullName);
    assert.equal(await evaluate(reader, 'Boolean(document.querySelector(".add-residency, .hero-actions .secondary-action"))'), false);
  }
  await navigate(manager, '/' + resident);
  await chooseButton(manager, 'Chỉnh sửa', '.hero-actions button');
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".resident-form-grid input"))'), 'Resident editor missing');
  await fill(manager, '.resident-form-grid label:nth-child(1) input', residentName);
  await fill(manager, '.resident-form-grid label:nth-child(2) input', profile.dateOfBirth ?? '1990-01-01');
  await fill(manager, '.resident-form-grid label:nth-child(4) input', profile.nationality ?? 'VN');
  await fill(manager, '.resident-form-grid label:nth-child(5) input', profile.identityType ?? 'CCCD');
  await fill(manager, '.resident-form-grid label:nth-child(6) input', profile.identityNumber ?? '079900000123');
  await fill(manager, '.resident-form-grid input[type="tel"]', profile.phoneNumber ?? '0901234567');
  await chooseButton(manager, 'Lưu thay đổi', '.resident-modal-footer button');
  try {
    await visible(manager, 'h1', residentName);
  } catch (error) {
    const formError = await evaluate(manager, 'document.querySelector(".resident-modal .resident-notice.error, .resident-modal .field-error")?.textContent');
    throw new Error(formError || error.message);
  }
  assert.equal((await expected(manager, resident, 200)).fullName, residentName);

  const apartment = 'apartments/' + fixture.apartmentId;
  const currentHome = await expected(manager, apartment, 200);
  const targetHome = await expected(manager, 'apartments', 201, {
    unitNumber: 'B6-CHROME', floorNumber: 1, apartmentUnitTypeId: currentHome.apartmentUnitTypeId,
    usableAreaM2: 65, bedroomCount: 2, bathroomCount: 1
  });
  const targetApartment = 'apartments/' + targetHome.id;
  await expected(manager, targetApartment + '/owners', 204, { ownerResidentId: fixture.residentId });
  await navigate(manager, '/' + targetApartment);
  await visible(manager, 'h1', 'B6-CHROME');
  await click(manager, '.end-owner');
  await chooseButton(manager, 'Kết thúc quyền sở hữu', '[role="dialog"] button');
  await until(async () => (await expected(manager, targetApartment, 200)).owners.length === 0, 'Owner still current');
  const startDate = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date());
  const source = profile.residencies.find(item => item.apartmentUnitId === fixture.apartmentId);
  await expected(staff, resident + '/residencies/move', 403, { sourceResidencyId: source.id, target: {
    apartmentUnitId: targetHome.id, householdRole: 'HOUSEHOLD_HEAD', residencyType: 'TENANT', startDate
  }});
  await expected(manager, resident + '/residencies/move', 200, { sourceResidencyId: source.id, target: {
    apartmentUnitId: targetHome.id, householdRole: 'HOUSEHOLD_HEAD', residencyType: 'TENANT', startDate
  }});
  await navigate(owner, '/resident/apartment');
  await visible(owner, '.apartment-title-line h2', 'B6-CHROME');
  assert.equal(await evaluate(owner, 'Boolean(' + element('.apartment-title-line h2', 'PHASE11-A1') + ')'), false);
  checks.residentAndApartment = {
    uiToApi: ['profile edit', 'end ownership'],
    apiToBrowser: ['move', 'resident sees new apartment'],
    readOnlyRoles: ['STAFF', 'ACCOUNTANT']
  };
  await save(checks);

  const equipment = await expected(manager, 'equipments', 201, { name: 'Máy bơm Chrome B6' });
  const equipmentPath = 'equipments/' + equipment.id;
  await expected(staff, equipmentPath + '/status', 403, { status: 1 }, 'PATCH');
  await expected(manager, equipmentPath, 200, { name: 'Máy bơm Chrome B6 đã cập nhật' }, 'PUT');
  const row = element('tr', 'Máy bơm Chrome B6 đã cập nhật');
  await navigate(manager, '/equipment');
  await visible(manager, 'tr', 'Máy bơm Chrome B6 đã cập nhật');
  await expected(manager, 'maintenance/tasks', 201, {
    equipmentId: equipment.id, title: 'Bảo trì Chrome B6', description: 'Dữ liệu tổng hợp',
    priorityCode: 'HIGH', plannedStartAt: new Date(Date.now() + 60_000).toISOString(),
    dueAt: new Date(Date.now() + 3_600_000).toISOString(), isEmergency: true
  });
  await navigate(manager, '/equipment');
  await until(() => evaluate(manager, row + '?.querySelector(' + encode('[aria-label="Đổi trạng thái vận hành"]') + ')?.disabled === true'),
    'Status control still enabled on equipment under maintenance');
  await expected(manager, equipmentPath + '/status', 409, { status: 0 }, 'PATCH');
  checks.assets = { apiToBrowser: ['equipment update', 'maintenance locks status control'], directStaffWrite: 403, directMaintenanceOverride: 409 };
  await save(checks);

  const subject = 'Phản ánh Chrome B6';
  await navigate(owner, '/resident/complaints');
  await chooseButton(owner, 'Gửi phản ánh mới');
  await until(() => evaluate(owner, 'Boolean(document.querySelector(' + encode('input[placeholder^="Ví dụ: Tiếng ồn"]') + '))'), 'Complaint editor missing');
  await fill(owner, 'input[placeholder^="Ví dụ: Tiếng ồn"]', subject);
  await fill(owner, 'textarea[placeholder^="Mô tả sự việc"]', 'Dữ liệu tổng hợp cho kiểm thử.');
  await chooseButton(owner, 'Gửi phản ánh', 'form button[type="submit"]');
  let complaint;
  await until(async () => {
    const page = await expected(owner, 'complaints?search=' + encodeURIComponent(subject), 200);
    complaint = page.items.find(item => item.subject === subject);
    return Boolean(complaint);
  }, 'Complaint did not persist');
  const complaintApi = 'complaints/' + complaint.id;
  const managerPage = '/manager/' + complaintApi;
  await navigate(manager, managerPage);
  await chooseButton(manager, 'Tiếp nhận và xem xét');
  await until(async () => (await expected(manager, complaintApi, 200)).status === 'UNDER_REVIEW', 'Review did not persist');
  const assigned = await expected(manager, complaintApi + '/followups', 200,
    { staffUserId: staff.userId, title: 'Kiểm tra Chrome B6', instruction: 'Nội bộ B6' });
  const followup = assigned.followups[0];
  await expected(other, complaintApi, 404);
  await navigate(staff, '/staff/' + complaintApi);
  await visible(staff, 'h3', followup.title);
  await fill(staff, 'label textarea', 'Kết quả nội bộ B6');
  await chooseButton(staff, 'Hoàn thành công việc');
  await until(async () => (await expected(manager, complaintApi, 200)).status === 'UNDER_REVIEW', 'Follow-up did not complete');
  await navigate(manager, managerPage);
  await until(() => evaluate(manager, 'Boolean(document.querySelector(' + encode('label textarea[rows="5"]') + '))'), 'Resolution form missing');
  await fill(manager, 'label textarea[rows="5"]', 'Ban quản lý đã giải quyết.');
  await chooseButton(manager, 'Giải quyết phản ánh', 'form button[type="submit"]');
  await chooseButton(manager, 'Đóng phản ánh và thông báo cư dân');
  const notice = await persistedNotice(owner, complaint.id);
  await until(() => framesFor(owner, notice.id).length === 1, 'Reporter did not receive closure event');
  assert.equal(framesFor(other, notice.id).length, 0);
  assert.equal((await expected(owner, complaintApi, 200)).status, 'CLOSED');
  checks.complaints = { uiToUi: ['submit', 'review', 'staff completes', 'resolve', 'close', 'notice to reporter'],
    apiToBrowser: ['staff assignment'], otherHousehold: 404 };
  await save(checks);
}
