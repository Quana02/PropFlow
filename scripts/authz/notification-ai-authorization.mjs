import assert from 'node:assert/strict';

export default async function verifyAiAuthorization({ fixture, login, navigate, evaluate, until, click, api, save }) {
  const manager = await login('manager');
  const admin = await login('admin');
  const staff = await login('staff');
  const resident = await login('resident');
  const checks = {};
  const call = async (tab, path, status, body, method = 'POST') => {
    const response = await api(tab, '/api/v1/' + path, body, {}, method);
    assert.equal(response.status, status, path + ' ' + (response.data?.code ?? ''));
    return response.data;
  };
  const bySelector = async (tab, selector) => {
    const match = 'document.querySelector(' + JSON.stringify(selector) + ')';
    await until(() => evaluate(tab, 'Boolean(' + match + ' && !' + match + '.disabled)'), 'Thiếu nút ' + selector);
    await click(tab, selector);
  };
  const fill = (tab, selector, value, event = 'change') => evaluate(tab,
    '(()=>{const el=document.querySelector(' + JSON.stringify(selector) + ');el.value=' + JSON.stringify(value) +
    ';el.dispatchEvent(new Event(' + JSON.stringify(event) + ',{bubbles:true}));})()');
  const request = 'manager/service-requests/' + fixture.requestId;
  const classification = request + '/classification';
  const recommendation = request + '/recommendation';
  await navigate(manager, '/' + request);
  await bySelector(manager, '.ai-analyze');
  await until(async () => (await call(manager, classification, 200)).canReview, 'Classification giả chưa có kết quả');
  const suggested = (await call(manager, classification, 200)).suggestion;
  await call(staff, classification + '/review', 403,
    { suggestionId: suggested.id, categoryId: suggested.suggestedCategoryId }, 'PUT');
  await bySelector(manager, '.category-fact .inline-actions button:first-child');
  await until(async () => (await call(manager, classification, 200)).suggestion.reviewDecision === 'CONFIRMED',
    'Manager chưa xác nhận classification trong UI');
  // The accepted category changes the recommendation's input hash.
  await navigate(manager, '/' + request);
  await bySelector(manager, '.ai-analyze');
  await until(async () => (await call(manager, recommendation, 200)).canReview, 'Recommendation giả chưa có kết quả');
  const advice = (await call(manager, recommendation, 200)).suggestion;
  await call(admin, recommendation, 403, {});
  const approved = await call(manager, recommendation + '/review', 200, { recommendationId: advice.id, ...advice.proposed }, 'PUT');
  assert.equal(approved.suggestion.reviewDecision, 'CONFIRMED');
  await navigate(manager, '/' + request);
  await until(() => evaluate(manager, 'document.querySelector(".handling-section .confirmed-tag")?.textContent === "Đã xác nhận"'),
    'UI Manager chưa tải phương án đã duyệt');
  checks.serviceRequests = { uiToApi: ['generate', 'confirm classification'], apiToBrowser: ['review recommendation'],
    deniedStaffReview: 403, deniedAdminGenerate: 403 };
  await save(checks);

  const knowledge = 'knowledge/documents';
  const title = 'QuycanhChrome';
  const content = 'QuycanhChrome mở cửa lúc 7 giờ.';
  await navigate(manager, '/' + knowledge);
  await bySelector(manager, '.pf-management-primary');
  await until(() => evaluate(manager, 'Boolean(document.querySelector("#doc-title"))'), 'Form tài liệu chưa mở');
  await fill(manager, '#doc-title', title);
  await fill(manager, '#doc-content', content);
  await bySelector(manager, '.knowledge-editor button[type="submit"]');
  let document;
  await until(async () => {
    document = (await call(manager, knowledge + '?search=' + title, 200)).items.find(item => item.title === title);
    return Boolean(document);
  }, 'Bản nháp chưa được lưu từ UI');
  const documentPath = knowledge + '/' + document.id;
  await call(staff, documentPath, 404);
  const change = { expectedToken: document.token };
  await call(staff, documentPath + '/publication', 403, change);
  await call(admin, documentPath + '/publication', 200, change);
  await navigate(staff, '/' + knowledge);
  await until(() => evaluate(staff, 'Array.from(document.querySelectorAll(".knowledge-card h2")).some(x=>x.textContent==="QuycanhChrome")'),
    'STAFF chưa thấy tài liệu đã xuất bản');
  assert.equal(await evaluate(staff, 'Boolean(document.querySelector(".pf-management-primary"))'), false);
  const published = await call(admin, documentPath, 200);
  await call(admin, documentPath, 200,
    { title, content: 'QuycanhChrome bản nháp riêng.', audience: 'INTERNAL', expectedToken: published.document.token }, 'PUT');
  const staffView = await call(staff, documentPath, 200);
  assert.equal(staffView.revision.content, content);
  assert.equal(staffView.history.length, 1);

  await navigate(resident, '/resident');
  await bySelector(resident, '.rag-launch');
  async function ask() {
    await until(() => evaluate(resident, 'Boolean(document.querySelector("#rag-question") && !document.querySelector("#rag-question").disabled)'),
      'Trợ lý chưa nhận câu hỏi');
    await fill(resident, '#rag-question', 'QuycanhChrome mở cửa lúc nào?', 'input');
    await bySelector(resident, '.rag-input button[type="submit"]');
  }
  await ask();
  await until(() => evaluate(resident, 'document.querySelector(".rag-answer small")?.textContent === "AI có dẫn nguồn"'),
    'Chatbot chưa gọi Gemini giả');
  assert.equal(await evaluate(resident, 'document.querySelector(".rag-citation blockquote")?.textContent'), content);
  const latest = await call(admin, documentPath, 200);
  await call(admin, documentPath + '/unpublish', 200, { expectedToken: latest.document.token });
  await call(staff, documentPath, 404);
  await ask();
  await until(() => evaluate(resident,
    'Array.from(document.querySelectorAll(".rag-answer")).at(-1)?.textContent.includes("chưa tìm thấy tài liệu")'),
    'Chatbot vẫn trả nguồn sau khi ngừng xuất bản');
  await call(resident, 'resident/chatbot/sources/' + document.id + '/1', 404);
  checks.knowledge = { uiToApi: ['manager creates draft'], apiToBrowser: ['admin publishes and withdraws', 'staff reads published revision'],
    staffDraftAccess: 404, staffPublish: 403 };
  checks.chatbot = { uiToUi: ['fake Gemini answer', 'new question after withdrawal'], withdrawnSource: 404 };
  await save(checks);
}
