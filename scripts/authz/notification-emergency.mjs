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
  await until(() => evaluate(owner, '(document.querySelector(".emergency-overlay .call-error")?.innerText.includes("không còn hoạt động tại phiên này") || ' +
    'document.querySelector(".emergency-overlay .call-status")?.innerText === "Cuộc gọi đã kết thúc") && ' +
    '!document.querySelector(".emergency-overlay .end-call")'), 'Resident did not see the call become inactive');
  await until(() => evaluate(owner, 'document.querySelector(".emergency-overlay")?.previousElementSibling?.srcObject === null'),
    'Resident audio element still holds the ended call stream');
  assert.equal(signals(unselected).some(value => value?.callId === offer.callId), false);
  checks.retryAndEnd = { newRevision: true, managerUiEnded: true, ownerUiInactive: true, releasedAudioElement: true };
  await save(checks);

  const mediaState = tab => evaluate(tab, 'window.pf04MediaState()');
  await until(async () => (await mediaState(owner)).livePeers === 0 && (await mediaState(owner)).liveTracks === 0,
    'Ended resident call retained a peer connection or microphone track');
  await click(owner, '.dismiss-call');
  await click(selectedManager, '.dismiss-call');
  await click(owner, '.emergency-service-button');
  let secondSelected, secondOffer;
  await until(() => {
    for (const candidate of [firstManager, secondManager]) {
      const incoming = signals(candidate).find(signal => signal.kind === 'offer' && signal.callId !== offer.callId);
      if (incoming) { secondSelected = candidate; secondOffer = incoming; return true; }
    }
    return false;
  }, 'A new active call did not reach a selected manager', 20000);
  try {
    await until(async () => (await mediaState(owner)).connectedPeers > 0 &&
      (await mediaState(secondSelected)).connectedPeers > 0, 'Local fake-microphone WebRTC did not connect before revocation', 25000);
  } catch (error) {
    checks.mediaBeforeRevocation = { owner: await mediaState(owner), manager: await mediaState(secondSelected),
      ownerStatus: await status(owner), managerStatus: await status(secondSelected) };
    await save(checks);
    throw error;
  }
  assert.ok((await mediaState(owner)).liveTracks > 0);
  const startedRevoke = performance.now();
  assert.equal(await evaluate(owner, 'Boolean(document.querySelector(".profile-logout"))'), true);
  await click(owner, '.profile-logout');
  await until(() => evaluate(owner, 'location.pathname === "/login"'), 'Logout did not leave protected call UI');
  await until(async () => (await mediaState(owner)).livePeers === 0 && (await mediaState(owner)).liveTracks === 0,
    'Logout retained a local peer connection or live microphone track');
  const clientCleanupMs = Math.round(performance.now() - startedRevoke);
  assert.ok(clientCleanupMs <= 3000, 'Local client cleanup exceeded the 3-second engineering target');
  await until(() => evaluate(secondSelected, '!document.querySelector(".emergency-overlay")?.previousElementSibling?.srcObject'),
    'The cooperating peer retained remote audio after session revocation', 10000);
  await until(async () => (await mediaState(secondSelected)).livePeers === 0 && (await mediaState(secondSelected)).liveTracks === 0,
    'The cooperating peer retained its peer connection or microphone after session revocation', 10000);
  const peerCleanupMs = Math.round(performance.now() - startedRevoke);
  assert.ok(peerCleanupMs <= 3000, 'Cooperating peer cleanup exceeded the 3-second engineering target');
  assert.equal((await api(owner, '/api/v1/auth/me')).status, 401);
  assert.equal((await api(owner, '/hubs/emergency-calls/negotiate?negotiateVersion=1', {})).status, 401);
  assert.equal((await api(otherResident, '/api/v1/auth/me')).status, 200);
  assert.equal((await api(secondSelected, '/api/v1/auth/me')).status, 200);
  checks.revokedActiveMedia = {
    signalingAndLocalMediaConnectedBeforeRevoke: true, logoutFromUi: true, localPeerClosed: true,
    microphoneTracksEnded: true, cooperatingPeerAudioReleased: true, oldBearer401: true, oldReconnect401: true,
    unrelatedSessionStillValid: true, cooperatingPeerConnectionAndMicrophoneReleased: true, clientCleanupMs, peerCleanupMs,
    scope: 'Local fake microphone and direct ICE only; no live TURN or Internet routing claim'
  };
  await save(checks);
}
