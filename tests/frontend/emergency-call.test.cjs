const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../src/PropFlow.Web.Client/wwwroot/js/emergency-call.js'), 'utf8');
const modulePromise = import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

async function browser(run) {
  const keys = ['navigator', 'RTCPeerConnection', 'isSecureContext', 'MediaStream'];
  const previous = Object.fromEntries(keys.map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  const peers = [], calls = [];
  const track = { enabled: true, readyState: 'live', stops: 0, stop() { this.stops++; this.readyState = 'ended'; } };
  const stream = { getTracks: () => [track], getAudioTracks: () => [track] };
  class Peer {
    constructor(config) { this.config = config; this.tracks = []; this.ice = []; this.connectionState = 'new'; peers.push(this); }
    addTrack(value) { this.tracks.push(value); }
    async createOffer() { return { type: 'offer', sdp: 'v=0\r\nm=audio 9' }; }
    async createAnswer() { return { type: 'answer', sdp: 'v=0\r\nm=audio 9' }; }
    async setLocalDescription(value) { this.localDescription = value; }
    async setRemoteDescription(value) { this.remoteDescription = value; }
    async addIceCandidate(value) { assert.ok(this.remoteDescription); this.ice.push(value); }
    close() { this.closed = true; }
  }
  try {
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { mediaDevices: { getUserMedia: async constraints => { assert.equal(constraints.video, false); return stream; } } } });
    globalThis.RTCPeerConnection = Peer;
    globalThis.isSecureContext = true;
    globalThis.MediaStream = class { constructor(tracks) { this.tracks = tracks; } };
    const audio = { srcObject: null, play: async () => {}, pause() {} };
    const client = (await modulePromise).create({ invokeMethodAsync: async (...args) => { calls.push(args); } }, audio);
    try { await run({ client, peers, calls, track, stream, audio, flush: () => new Promise(setImmediate) }); }
    finally { client.dispose(); }
  } finally {
    for (const [key, descriptor] of Object.entries(previous)) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else delete globalThis[key];
    }
  }
}

test('resident requests only audio and sends one offer for each call revision', () => browser(async ({ client, peers, calls }) => {
  assert.equal(await client.connect('call', 1, true, [{ urls: ['stun:example.test'] }]), true);
  await client.connect('call', 1, true, []);
  assert.equal(peers.length, 1);
  assert.equal(peers[0].tracks.length, 1);
  assert.equal(calls.filter(x => x[0] === 'OnRtcSignal' && x[3] === 'offer').length, 1);
}));

test('manager buffers ICE before the offer then answers without waiting for a separate acceptance button', () => browser(async ({ client, peers, calls }) => {
  await client.connect('call', 2, false, []);
  await client.receive('call', 2, 'ice', JSON.stringify({ candidate: 'candidate:test' }));
  assert.equal(peers[0].ice.length, 0);
  await client.receive('call', 2, 'offer', JSON.stringify({ type: 'offer', sdp: 'm=audio 9' }));
  assert.equal(peers[0].ice.length, 1);
  assert.equal(calls.filter(x => x[0] === 'OnRtcSignal' && x[3] === 'answer').length, 1);
}));

test('restarting closes the old peer and ignores old revision candidates', () => browser(async ({ client, peers }) => {
  await client.connect('call', 1, true, []);
  await client.connect('call', 2, true, []);
  assert.equal(peers[0].closed, true);
  assert.equal(await client.receive('call', 1, 'ice', '{"candidate":"old"}'), false);
  assert.equal(peers[1].ice.length, 0);
}));

test('muting only changes the live audio track and ending releases the microphone', () => browser(async ({ client, track, audio, peers }) => {
  await client.connect('call', 1, true, []);
  client.mute(true); assert.equal(track.enabled, false);
  client.mute(false); assert.equal(track.enabled, true);
  client.stop();
  assert.equal(track.stops, 1);
  assert.equal(peers[0].closed, true);
  assert.equal(audio.srcObject, null);
}));

test('permission denial is reported and never creates a connected peer', () => browser(async ({ client, calls, peers }) => {
  navigator.mediaDevices.getUserMedia = async () => { throw Object.assign(new Error(), { name: 'NotAllowedError' }); };
  assert.equal(await client.connect('call', 1, true, []), false);
  assert.deepEqual(calls, [['OnMediaError', 'NotAllowedError']]);
  assert.equal(peers.length, 0);
}));

test('a new call starts with its microphone enabled after ending a muted call', () => browser(async ({ client, track }) => {
  await client.connect('first', 1, true, []);
  client.mute(true);
  client.stop();
  const nextTrack = { enabled: false, readyState: 'live', stop() {} };
  const nextStream = { getTracks: () => [nextTrack], getAudioTracks: () => [nextTrack] };
  navigator.mediaDevices.getUserMedia = async () => nextStream;
  assert.equal(await client.connect('second', 1, true, []), true);
  assert.equal(track.readyState, 'ended');
  assert.equal(nextTrack.enabled, true);
}));

test('a microphone granted after disposal is stopped immediately', () => browser(async ({ client, track, stream, peers }) => {
  let grant;
  navigator.mediaDevices.getUserMedia = () => new Promise(resolve => { grant = resolve; });
  const pending = client.prepare();
  client.dispose(); grant(stream);
  assert.equal(await pending, false);
  assert.equal(track.stops, 1);
  assert.equal(peers.length, 0);
}));

test('autoplay denial exposes an explicit audio control instead of claiming playback succeeded', () => browser(async ({ client, peers, calls, audio, stream, flush }) => {
  audio.play = async () => { throw new Error('blocked'); };
  await client.connect('call', 1, false, []);
  peers[0].ontrack({ streams: [stream] }); await flush();
  assert.ok(calls.some(x => x[0] === 'OnRtcState' && x[3] === 'audio-blocked'));
  audio.play = async () => {};
  assert.equal(await client.play('call', 1), true);
}));

test('connecting does not report media connected until the browser confirms its peer state', () => browser(async ({ client, peers, calls, flush }) => {
  await client.connect('call', 1, true, []);
  assert.equal(calls.some(x => x[0] === 'OnRtcState' && x[3] === 'connected'), false);
  peers[0].connectionState = 'connected'; peers[0].onconnectionstatechange(); await flush();
  assert.ok(calls.some(x => x[0] === 'OnRtcState' && x[3] === 'connected'));
}));
