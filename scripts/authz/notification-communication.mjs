import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export default async function verifyCommunicationNotifications({
  fixture, login, navigate, evaluate, until, socketsReady, api, badge, notifications, click, snapshot, save
}) {
  const manager = await login('manager');
  const owner = await login('resident');
  const other = await login('resident2');
  const admin = await login('admin');
  const unlinked = await login('resident3');
  const checks = {};
  const frames = (tab, target) => tab.communicationUpdates.filter(event => event.target === target).map(event => event.args);
  const visible = (tab, selector, marker) => evaluate(tab,
    'document.querySelector(' + JSON.stringify(selector) + ')?.innerText.includes(' + JSON.stringify(marker) + ')');
  const enter = (tab, selector, value) => evaluate(tab,
    '(()=>{const field = document.querySelector(' + JSON.stringify(selector) + ');field.value = ' + JSON.stringify(value) +
    ';field.dispatchEvent(new Event("input",{bubbles:true}));})()');
  const action = async (tab, url, body, expected, method = 'POST') => {
    const result = await api(tab, url, body, {}, method);
    assert.equal(result.status, expected, result.data?.code);
    return result.data;
  };

  await Promise.all([navigate(manager, '/manager/announcements'), navigate(owner, '/resident/announcements'),
    navigate(other, '/resident/announcements'), navigate(unlinked, '/resident/announcements')]);
  await Promise.all([manager, owner, other, unlinked].map(tab => socketsReady(tab, '/hubs/announcements')));
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".announcement-workspace .pf-management-primary"))'),
    'Manager announcement composer unavailable');
  await Promise.all([owner, other].map(tab => until(() => evaluate(tab, 'Boolean(document.querySelector(".announcement-workspace"))'),
    'Resident announcement workspace unavailable')));
  await click(manager, '.announcement-workspace .pf-management-primary');
  await until(() => evaluate(manager, 'Boolean(document.querySelector("#announcement-title"))'), 'Announcement form missing');
  const title = 'Thông cáo fixture realtime';
  const content = 'Nội dung thông cáo chỉ có trong phản hồi HTTP được phép.';
  await enter(manager, '#announcement-title', title);
  await enter(manager, '#announcement-content', content);
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".announcement-workspace form button[type=submit]:enabled"))'),
    'Announcement form did not accept its fields');
  await click(manager, '.announcement-workspace form button[type=submit]');
  for (const tab of [owner, other]) {
    await until(() => frames(tab, 'AnnouncementPublished').some(args =>
      args.length === 1 && args[0] === '00000000-0000-0000-0000-000000000000'), 'Expected a content-free announcement invalidation');
    await until(() => visible(tab, '.announcement-list', title), 'Announcement did not appear without refresh');
    await until(async () => (await badge(tab)) ===
      (await action(tab, '/api/v1/notifications/unread-count', undefined, 200)).count, 'Bell count did not reconcile');
  }
  const listing = await action(manager, '/api/v1/announcements', undefined, 200);
  const published = listing.items.find(item => item.title === title);
  assert.ok(published);
  assert.equal((await action(owner, '/api/v1/announcements/' + published.id, undefined, 200)).content, content);
  await action(admin, '/api/v1/announcements/' + published.id, undefined, 403);
  await action(unlinked, '/api/v1/announcements', undefined, 403);
  await action(unlinked, '/api/v1/announcements/' + published.id, undefined, 403);
  assert.equal(Boolean(await visible(unlinked, '.announcement-list', title)), false);
  assert.ok(frames(unlinked, 'AnnouncementPublished').every(args =>
    args.length === 1 && args[0] === '00000000-0000-0000-0000-000000000000'));
  for (const tab of [owner, other])
    assert.equal((await notifications(tab)).data.some(item => item.sourceId === published.id || item.title === title), false);
  await snapshot(owner, 'notification-fe13-announcement');
  checks.announcement = { trigger: 'UI-to-UI', twoResidents: true, noPrivateFrame: true,
    unrelatedAdmin403: true, unlinkedResident403: true, noPersonalBell: true };
  await save(checks);

  const chat = '/api/v1/support-chat/conversations/' + fixture.conversationId;
  await Promise.all([navigate(manager, '/manager/support-chat'), navigate(owner, '/resident/contact-management'),
    navigate(other, '/resident/contact-management')]);
  await Promise.all([manager, owner, other].map(tab => socketsReady(tab, '/hubs/support-chat')));
  await until(() => evaluate(manager, 'Boolean(document.querySelector(".manager-inbox .inbox-row:enabled"))'), 'Manager chat inbox unavailable');
  await click(manager, '.manager-inbox .inbox-row');
  await until(() => visible(manager, '.support-thread', 'Tin nhắn kiểm thử'), 'Manager did not open the seeded conversation');
  await until(() => visible(owner, '.support-thread', 'Tin nhắn kiểm thử'), 'Resident did not open the seeded conversation');
  await action(other, chat + '/messages', undefined, 404);
  const draft = 'Bản nháp riêng trong Chrome';
  await enter(owner, '#support-message', draft);
  await until(() => frames(manager, 'SupportChatDraft').some(args => args[0]?.conversationId === fixture.conversationId && args[0].text === draft),
    'Assigned manager did not receive the live draft');
  await until(() => visible(manager, '.support-live-draft', draft), 'Manager UI did not render the transient draft');
  const unreadDraft = await action(manager, chat + '/messages', undefined, 200);
  assert.equal(unreadDraft.items.some(message => message.text === draft), false);
  assert.equal(frames(other, 'SupportChatDraft').some(args => args[0]?.conversationId === fixture.conversationId), false);
  await enter(manager, '#support-message', 'Manager đang nhập bản thử');
  await until(() => frames(owner, 'SupportChatTyping').some(args => args[0]?.conversationId === fixture.conversationId && args[0].typing),
    'Resident did not receive assigned manager typing');
  await until(() => visible(owner, '.support-typing', 'Manager đang nhập'), 'Resident UI did not render typing');
  assert.equal(frames(other, 'SupportChatTyping').some(args => args[0]?.conversationId === fixture.conversationId), false);
  await enter(manager, '#support-message', '');
  checks.draftAndTyping = { trigger: 'UI-to-UI', assignedParticipants: true, otherConversationExcluded: true, draftOutsidePersistedMessages: true };
  await save(checks);

  await evaluate(owner, 'window.failFixtureChatPost(' + JSON.stringify(chat + '/messages') + ')');
  await click(owner, '.support-send');
  await until(() => visible(owner, '.support-send-status.failed', 'Chưa gửi được'), 'Chat did not display the injected send error');
  assert.equal(await evaluate(owner, 'document.querySelector("#support-message").value'), draft);
  const afterFailedSend = await action(manager, chat + '/messages', undefined, 200);
  assert.equal(afterFailedSend.items.some(message => message.text === draft), false);
  // Read receipts also emit SupportChatChanged; compare persisted messages instead of attributing every hint to the failed send.
  assert.deepEqual(afterFailedSend.items.map(message => message.id), unreadDraft.items.map(message => message.id));
  checks.draftAndTyping.failedSendKeepsDraft = true;
  await save(checks);

  const managerFramesBeforeSend = frames(manager, 'SupportChatChanged').length;
  await click(owner, '.support-send');
  await until(() => frames(manager, 'SupportChatChanged').slice(managerFramesBeforeSend)
    .some(args => args.length === 1 && args[0] === fixture.conversationId),
    'Manager did not receive the chat invalidation');
  await until(() => visible(manager, '.support-thread', draft), 'Manager did not see the sent chat message without refresh');
  assert.equal(frames(other, 'SupportChatChanged').some(args => args.includes(fixture.conversationId)), false);
  const messages = await action(manager, chat + '/messages', undefined, 200);
  const sent = messages.items.find(message => message.text === draft);
  assert.ok(sent);
  const reply = 'Phản hồi fixture từ Manager';
  const target = '[data-message-id="' + sent.id + '"] + .support-message-tools button[aria-label="Trả lời"]';
  await until(() => evaluate(manager, 'Boolean(document.querySelector(' + JSON.stringify(target) + '))'), 'Reply action missing');
  await click(manager, target);
  await enter(manager, '#support-message', reply);
  await click(manager, '.support-reply-draft button[aria-label="Hủy trả lời"]');
  assert.equal(await evaluate(manager, 'document.querySelector("#support-message").value'), reply);
  await click(manager, target);
  await click(manager, '.support-send');
  await until(() => visible(owner, '.support-thread', reply), 'Resident UI did not receive the reply');
  await until(() => visible(owner, '.support-message-quote', draft), 'Reply did not link to the correct message');
  await snapshot(owner, 'notification-fe13-chat');
  const afterReply = await action(owner, chat + '/messages', undefined, 200);
  const replyRecord = afterReply.items.find(message => message.text === reply);
  assert.ok(replyRecord);
  const receipt = { messageIds: [replyRecord.id], seen: true };
  await action(other, chat + '/messages/receipts', receipt, 404);
  await action(owner, chat + '/messages/receipts', receipt, 200);
  const receiptSelector = '[data-message-id="' + replyRecord.id + '"] .support-receipt';
  await until(() => visible(manager, receiptSelector, 'Đã xem'), 'Sender browser did not receive the read receipt');
  assert.ok((await action(manager, chat + '/messages/' + replyRecord.id, undefined, 200)).seenAt);
  await action(manager, chat + '/messages/' + replyRecord.id + '/recall', {}, 200, 'PUT');
  await until(async () => !await visible(owner, '.support-thread', reply), 'Recalled message text stayed visible to the peer');
  checks.supportChat = { trigger: 'UI-to-UI and API-to-browser', send: true, reply: true, recall: true,
    persisted: true, excludedConversation404: true, invalidationContainsOnlyId: true,
    replyDraftSurvivesCancel: true, readReceiptShownToSender: true };
  await save(checks);

  await action(owner, chat + '/messages/hide', { messageIds: [sent.id] }, 200);
  const sentSelector = '[data-message-id="' + sent.id + '"]';
  await until(async () => !await visible(owner, sentSelector, draft), 'Hidden message remained visible to the owner');
  assert.equal(await visible(manager, sentSelector, draft), true);
  await action(owner, chat + '/messages/' + sent.id, undefined, 404);
  await action(manager, chat + '/messages/' + sent.id, undefined, 200);
  checks.hideForMe = { ownerMessageRemoved: true, peerStillSeesMessage: true };
  await save(checks);

  const dest = '/api/v1/support-chat/conversations/' + fixture.otherConversationId;
  assert.equal((await action(manager, chat + '/close', {}, 200)).status, 'CLOSED');
  await action(manager, '/api/v1/support-chat/availability', { available: true }, 200, 'PUT');
  await until(async () => (await action(other, dest, undefined, 200)).status === 'ACTIVE',
    'Second conversation did not move from waiting to active');
  await action(owner, dest, undefined, 404);
  const request = { destinationId: fixture.otherConversationId, items: [{ messageId: sent.id, clientMessageId: randomUUID() }] };
  await action(other, chat + '/messages/forward', request, 404);
  const beforeForward = frames(other, 'SupportChatChanged').length;
  assert.equal(await action(manager, chat + '/messages/forward', request, 200), 1);
  await until(() => frames(other, 'SupportChatChanged').slice(beforeForward)
    .some(args => args.length === 1 && args[0] === fixture.otherConversationId), 'Forward target received no scoped frame');
  await until(() => visible(other, '.support-thread', draft), 'Forwarded message was not visible to the intended recipient');
  await until(() => visible(other, '.support-message-label', 'Đã chuyển tiếp'), 'Forwarded message label missing');
  assert.equal(frames(owner, 'SupportChatChanged').some(args => args.includes(fixture.otherConversationId)), false);
  assert.equal(await action(manager, chat + '/messages/forward', request, 200), 1);
  const copies = (await action(other, dest + '/messages', undefined, 200)).items
    .filter(message => message.forwarded && message.text === draft);
  assert.equal(copies.length, 1);
  checks.forward = { intendedRecipient: true, otherConversationExcluded: true, noDuplicateOnReplay: true, waitingToActive: true, sourceClosed: true };
  await save(checks);
}
