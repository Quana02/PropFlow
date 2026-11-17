import assert from 'node:assert/strict';

export default async function verifyEmergencyNotifications({ login, evaluate, until, socketsReady, api, click, snapshot, save }) {
  const firstManager = await login('manager');
  const secondManager = await login('manager');
  const owner = await login('resident');
  const otherResident = await login('resident2');
  const hub = '/hubs/emergency-calls';
  const changes = tab => tab.communicationUpdates.filter(event => event.target === 'EmergencyChanged');
  const signals = tab => tab.communicationUpdates.filter(event => event.target === 'EmergencySignal').map(event => event.args[0]);
  const status = tab => evaluate(tab, 'document.querySelector(".emergency-overlay .call-status")?.innerText');
  await Promise.all([firstManager, secondManager, owner, otherResident].map(tab => socketsReady(tab, hub)));
  await until(() => evaluate(owner, 'Boolean(document.querySelector(".emergency-service-button:enabled"))'), 'Emergency action missing');
  await click(owner, '.emergency-service-button');
  let selectedManager;
  await until(async () => {
    const open = await Promise.all([firstManager, secondManager].map(tab => evaluate(tab,
      'Boolean(document.querySelector(".emergency-overlay .call-controls"))')));
    if (open.filter(Boolean).length !== 1) return false;
    selectedManager = open[0] ? firstManager : secondManager;
    return true;
  }, 'Exactly one manager connection must show the incoming call', 20000);
  const unselected = selectedManager === firstManager ? secondManager : firstManager;
  await until(() => signals(selectedManager).some(value => value?.kind === 'offer') &&
    signals(owner).some(value => value?.kind === 'answer'), 'Selected browser pair did not exchange offer and answer', 20000);
  const offer = signals(selectedManager).find(value => value.kind === 'offer');
  assert.ok(offer.callId);
  assert.ok(signals(owner).some(value => value.callId === offer.callId && value.kind === 'answer'));
  assert.equal(signals(unselected).some(value => value?.callId === offer.callId), false);
  assert.equal(signals(otherResident).some(value => value?.callId === offer.callId), false);
  assert.ok(changes(selectedManager).some(event => event.args.length === 0));
  assert.equal(changes(otherResident).length, 0);
  assert.equal((await api(otherResident, '/api/v1/emergency-calls/' + offer.callId + '/end', {})).status, 404);
  await snapshot(selectedManager, 'notification-fe13-emergency');
  const checks = { incoming: { trigger: 'UI-to-UI', selectedConnectionOnly: true, outsiderEnd404: true, emptyInvalidation: true },
    signaling: { offerAndAnswer: true, excludedOtherConnectionAndHousehold: true, syntheticMicrophone: true } };
  await save(checks);

  await click(owner, '.retry-call');
  await until(() => signals(selectedManager).some(value => value.kind === 'offer' && value.callId === offer.callId &&
    value.revision > offer.revision), 'Retry did not advance the signaling revision', 20000);
  await until(() => evaluate(selectedManager, 'Boolean(document.querySelector(".emergency-overlay .end-call:enabled"))'),
    'Manager end-call action did not become ready');
  await click(selectedManager, '.end-call');
  await until(async () => (await status(selectedManager)) === 'Cuộc gọi đã kết thúc', 'Manager did not get the confirmed end status');
  await until(() => evaluate(owner, 'document.querySelector(".emergency-overlay .call-error")?.innerText.includes("không còn hoạt động tại phiên này") && ' +
    '!document.querySelector(".emergency-overlay .call-controls")'), 'Resident did not see the call become inactive');
  await until(() => evaluate(owner, 'document.querySelector(".emergency-overlay")?.previousElementSibling?.srcObject === null'),
    'Resident audio element still holds the ended call stream');
  assert.equal(signals(unselected).some(value => value?.callId === offer.callId), false);
  checks.retryAndEnd = { newRevision: true, managerUiEnded: true, ownerUiInactive: true, releasedAudioElement: true };
  await save(checks);
}
