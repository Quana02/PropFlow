const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../src/PropFlow.Web.Client/wwwroot/js/facility-live-voice.js'), 'utf8')
  .replace('import.meta.url', JSON.stringify('https://propflow.test/js/facility-live-voice.js'));
let index = 0;
async function browser(run) {
  const names = ['isSecureContext', 'navigator', 'document', 'AudioContext', 'AudioWorkletNode', 'WebSocket', 'addEventListener', 'removeEventListener'];
  const previous = Object.fromEntries(names.map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  const calls = [], sockets = [], captures = [], nodes = [], contexts = [], document = new EventTarget(), events = new EventTarget();
  document.visibilityState = 'visible'; const track = { stops: 0, stop() { this.stops++; } }, media = { getTracks: () => [track] };
  const response = { suggestions: [], mode: 'NO_MATCH', message: 'Chưa có lịch phù hợp' };
  let toolResult = async () => response;
  class Socket {
    constructor(url) { this.url = url; this.readyState = 1; this.bufferedAmount = 0; this.sent = []; sockets.push(this); queueMicrotask(() => this.onopen?.()); }
    send(json) { const data = JSON.parse(json); this.sent.push(data); if (data.setup) queueMicrotask(() => this.receive({ setupComplete: {} })); }
    receive(data) { this.onmessage?.({ data: typeof data === 'string' ? data : JSON.stringify(data) }); }
    close() { this.readyState = 3; }
  }
  class Context {
    constructor(options) { this.sampleRate = options.sampleRate; this.state = 'running'; this.currentTime = 0; this.destination = {}; this.audioWorklet = { addModule: async () => {} }; contexts.push(this); }
    async resume() {} async close() { this.state = 'closed'; }
    createMediaStreamSource() { return { connect() {}, disconnect() {} }; }
    createGain() { return { gain: { value: 1 }, connect() {}, disconnect() {} }; }
    createBuffer(channels, count, rate) { return { duration: count / rate, getChannelData: () => new Float32Array(count) }; }
    createBufferSource() { const node = { stops: 0, connect() {}, start(at) { this.at = at; }, stop() { this.stops++; } }; nodes.push(node); return node; }
  }
  class Capture { constructor() { this.port = {}; captures.push(this); } connect() {} disconnect() {} }
  let control;
  try {
    const values = { isSecureContext: true, navigator: { mediaDevices: { getUserMedia: async () => media } }, document, AudioContext: Context, AudioWorkletNode: Capture, WebSocket: Socket,
      addEventListener: events.addEventListener.bind(events), removeEventListener: events.removeEventListener.bind(events) };
    for (const [key, value] of Object.entries(values)) Object.defineProperty(globalThis, key, { value, configurable: true, writable: true });
    const module = await import('data:text/javascript;base64,' + Buffer.from(source + '\n//test' + index++).toString('base64'));
    control = module.create({ invokeMethodAsync: async (...args) => { calls.push(args); if (['OnLiveTool', 'OnLiveSelection', 'OnLiveDocuments', 'OnLivePrepareRequest', 'OnLiveUpdateRequest', 'OnLiveSubmitRequest'].includes(args[0])) return toolResult(); } });
    const session = { token: 'auth_tokens/test', webSocketUrl: 'wss://generativelanguage.googleapis.com/ws/test', setup: { model: 'models/live', tools: [] }, expiresAt: new Date(Date.now() + 600000).toISOString() };
    await run({ module, control, calls, sockets, captures, nodes, contexts, track, document, events, session, media, setTool: handler => toolResult = handler, flush: () => new Promise(setImmediate) });
  } finally { control?.dispose(); for (const [key, value] of Object.entries(previous)) if (value) Object.defineProperty(globalThis, key, value); else delete globalThis[key]; }
}
const chunk = { inlineData: { data: Buffer.from([0, 0, 0xff, 0x7f, 0, 0x80, 0, 0]).toString('base64'), mimeType: 'audio/pcm;rate=24000' } };

test('an untranscribed noise burst followed by a fan cannot keep renewing resident idle activity for two minutes', () => browser(async f => {
  await f.control.start(f.session, 6);
  const frame = amplitude => f.captures[0].port.onmessage({ data: new Float32Array(1024).fill(amplitude) });
  for (let i = 0; i < 24; i++) frame(.002);
  // A chair scrape, followed by a louder steady fan after microphone auto-gain changes.
  for (let i = 0; i < 4; i++) frame(.08);
  for (let i = 0; i < 1875; i++) frame(.03);
  const activity = f.calls.filter(x => x[0] === 'OnLiveSpeechActivity');
  assert.equal(activity.length, 0, 'Noise without any recognized words must not renew the idle deadline; received ' + activity.length + ' activity pulses');
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveUserTurn').length, 0);
}));

test('only recognized words notify resident interaction; local audio energy is not an idle heartbeat', () => browser(async f => {
  await f.control.start(f.session, 6);
  const frame = amplitude => f.captures[0].port.onmessage({ data: new Float32Array(1024).fill(amplitude) });
  for (let i = 0; i < 40; i++) frame(.002);
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveSpeechActivity').length, 0);
  for (let i = 0; i < 20; i++) frame(.08);
  const activity = f.calls.filter(x => x[0] === 'OnLiveSpeechActivity');
  assert.equal(activity.length, 0);
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveUserTurn').length, 0);
  f.sockets[0].receive({ serverContent: { inputTranscription: { text: 'Tôi vẫn đang nói' } } }); await f.flush();
  assert.ok(f.calls.some(x => x[0] === 'OnLiveUserTurn' && x[3] === 'Tôi vẫn đang nói'));
}));

test('quiet background after transcribed speech flushes the audio turn promptly instead of streaming noise forever', () => browser(async f => {
  await f.control.start(f.session, 1);
  const frame = value => f.captures[0].port.onmessage({ data: new Float32Array(1024).fill(value) });
  for (let i = 0; i < 12; i++) frame(.002);
  for (let i = 0; i < 5; i++) frame(.08);
  f.sockets[0].receive({ serverContent: { inputTranscription: { text: 'Chuyển quạt, PC và tủ ra ngoài' } } }); await f.flush();
  for (let i = 0; i < 6; i++) frame(.002);
  assert.equal(f.sockets[0].sent.filter(x => x.realtimeInput?.audioStreamEnd).length, 1);
  const count = f.sockets[0].sent.length;
  for (let i = 0; i < 12; i++) frame(.002);
  assert.equal(f.sockets[0].sent.length, count);
  frame(.08);
  assert.ok(f.sockets[0].sent.at(-1).realtimeInput?.audio);
  assert.equal(f.track.stops, 0);
}));
test('speech ending is bounded in audio time without waiting for remote transcription; small pauses and steady noise are preserved', () => browser(async f => {
  const detector = f.module.createSpeechEndDetector();
  const quiet = new Float32Array(1024).fill(.002), voice = new Float32Array(1024).fill(.08);
  for (let i = 0; i < 24; i++) assert.equal(detector.push(quiet, 16000).flush, false);
  assert.equal(detector.push(voice, 16000).flush, false);
  for (let i = 0; i < 4; i++) assert.equal(detector.push(quiet, 16000).flush, false); // One click is not a speech burst.
  for (let i = 0; i < 5; i++) detector.push(voice, 16000);
  for (let i = 0; i < 4; i++) assert.equal(detector.push(quiet, 16000).flush, false); // 256 ms pause.
  assert.equal(detector.push(voice, 16000).flush, false); // The resident continues listing items.
  for (let i = 0; i < 4; i++) assert.equal(detector.push(quiet, 16000).flush, false);
  assert.equal(detector.push(quiet, 16000).flush, true); // 320 ms after the final item.
  const noisy = f.module.createSpeechEndDetector();
  for (let i = 0; i < 80; i++) assert.equal(noisy.push(new Float32Array(1024).fill(.02), 16000).flush, false);
}));

test('request and document tools use separate callbacks and confirmation receives actual input before a tool call', () => browser(async f => {
  await f.control.start(f.session, 8); f.setTool(async () => ({ ok: true }));
  const id = '083485b5-2dcc-45cb-bae4-92fec9b89625';
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'docs', name: 'find_documents', args: { question: 'Nội quy làm đơn' } }] } }); await f.flush();
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'draft', name: 'prepare_request', args: { description: 'Vòi nước trong bếp bị rò liên tục.' } }] } }); await f.flush();
  f.sockets[0].receive({ serverContent: { inputTranscription: { text: 'Gửi đơn này' } }, toolCall: { functionCalls: [{ id: 'submit', name: 'submit_request', args: { draftId: id } }] } }); await f.flush();
  assert.ok(f.calls.some(x => x[0] === 'OnLiveDocuments')); assert.ok(f.calls.some(x => x[0] === 'OnLivePrepareRequest'));
  const inputAt = f.calls.findIndex(x => x[0] === 'OnLiveUserTurn' && x[3] === 'Gửi đơn này');
  const submitAt = f.calls.findIndex(x => x[0] === 'OnLiveSubmitRequest');
  assert.ok(inputAt >= 0 && inputAt < submitAt);
  assert.deepEqual(f.calls[submitAt], ['OnLiveSubmitRequest', 8, id]);
  f.sockets[0].receive({ serverContent: { turnComplete: true } }); await f.flush();
  f.control.sendText('Chưa gửi');
  assert.ok(f.calls.some(x => x[0] === 'OnLiveUserTurn' && x[2] === 2 && x[3] === 'Chưa gửi'));
}));

test('semantic selection displays the chosen subset and can clear cards without another lookup', () => browser(async f => {
  await f.control.start(f.session, 5);
  f.setTool(async () => ({ displayed: { suggestions: [] } }));
  const id = '083485b5-2dcc-45cb-bae4-92fec9b89625';
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'select', name: 'show_facilities', args: { lookupId: 'lookup-1', facilityIds: [id] } }] } }); await f.flush();
  assert.deepEqual(f.calls.find(x => x[0] === 'OnLiveSelection'), ['OnLiveSelection', 5, 'lookup-1', [id]]);
  assert.deepEqual(f.sockets[0].sent.at(-1).toolResponse.functionResponses[0].response, { displayed: { suggestions: [] } });
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'clear', name: 'show_facilities', args: { lookupId: 'lookup-1', facilityIds: [] } }] } }); await f.flush();
  assert.deepEqual(f.calls.filter(x => x[0] === 'OnLiveSelection').at(-1)[3], []);
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveTool').length, 0);
}));
test('selection rejects malformed and oversized tool arguments before invoking the UI', () => browser(async f => {
  await f.control.start(f.session, 1);
  for (const [index, args] of [{ facilityIds: [] }, { lookupId: 'x', facilityIds: 'bad' }, { lookupId: 'x', facilityIds: [7] }, { lookupId: 'x', facilityIds: ['1', '2', '3', '4'] }].entries()) {
    f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'invalid-' + index, name: 'show_facilities', args }] } }); await f.flush();
    assert.ok(f.sockets[0].sent.at(-1).toolResponse.functionResponses[0].response.error);
  }
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveSelection').length, 0);
}));

test('explicit ending tool waits for the entire goodbye audio before closing capture and notifying navigation', () => browser(async f => {
  await f.control.start(f.session, 7);
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'bye', name: 'end_conversation', args: {} }] } }); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveTool').length, 0);
  assert.equal(f.sockets[0].sent.at(-1).toolResponse.functionResponses[0].response.ending, true);
  f.sockets[0].receive({ serverContent: { outputTranscription: { text: 'Tạm biệt bạn, hẹn gặp lại!' }, modelTurn: { parts: [chunk, chunk] }, turnComplete: true } }); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 0); assert.equal(f.track.stops, 0);
  f.nodes[0].onended(); assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 0);
  f.nodes[1].onended(); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 1); assert.equal(f.track.stops, 1); assert.equal(f.sockets[0].readyState, 3);
}));
test('a goodbye phrase from the assistant, hello, or ok never closes the session without an explicit ending tool', () => browser(async f => {
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ serverContent: { inputTranscription: { text: 'Xin chào, ok' }, outputTranscription: { text: 'Hẹn gặp lại bạn sau!' }, modelTurn: { parts: [chunk] }, turnComplete: true } }); await f.flush();
  f.nodes[0].onended(); await f.flush();
  assert.equal(f.track.stops, 0); assert.equal(f.sockets[0].readyState, 1); assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 0);
}));
test('muted goodbye completes on turn end and stale events cannot navigate twice', () => browser(async f => {
  await f.control.start(f.session, 1, false); const late = f.sockets[0].onmessage;
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'bye', name: 'end_conversation' }] } }); await f.flush();
  f.sockets[0].receive({ serverContent: { modelTurn: { parts: [chunk] }, turnComplete: true } }); await f.flush();
  assert.equal(f.nodes.length, 0); assert.equal(f.track.stops, 1); assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 1);
  late({ data: JSON.stringify({ serverContent: { turnComplete: true } }) }); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 1);
}));
test('resident interrupting the farewell cancels the pending exit and continues the same conversation', () => browser(async f => {
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'bye', name: 'end_conversation' }] } }); await f.flush();
  f.sockets[0].receive({ serverContent: { modelTurn: { parts: [chunk] }, turnComplete: true } }); await f.flush();
  f.control.interrupt();
  f.sockets[0].receive({ serverContent: { interrupted: true, inputTranscription: { text: 'Khoan đã, tôi muốn hỏi tiếp' } } }); await f.flush();
  assert.equal(f.track.stops, 0); assert.equal(f.sockets[0].readyState, 1); assert.equal(f.calls.filter(x => x[0] === 'OnLiveEnded').length, 0);
}));
test('ending cancels an in-flight lookup and network failure still releases the session and requests dashboard navigation', () => browser(async f => {
  let resolve; f.setTool(() => new Promise(r => resolve = r));
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'search', name: 'find_facilities', args: { need: 'Tìm chỗ đọc sách' } }] } }); await f.flush();
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'bye', name: 'end_conversation' }] } }); await f.flush();
  assert.ok(f.calls.some(x=>x[0]==='OnLiveToolCancelled'&&x[2]==='search'));
  const responses=f.sockets[0].sent.filter(x=>x.toolResponse).length;
  resolve({ mode: 'LOOKUP' }); await f.flush();
  assert.equal(f.sockets[0].sent.filter(x=>x.toolResponse).length,responses);
  f.sockets[0].onerror(); await f.flush();
  assert.equal(f.track.stops,1);assert.equal(f.sockets[0].readyState,3);assert.equal(f.calls.filter(x=>x[0]==='OnLiveEnded').length,1);
}));

test('secure live setup uses a constrained ephemeral token, native Vietnamese audio and one greeting', () => browser(async f => {
  assert.equal(await f.control.start(f.session, 7), true);
  assert.match(f.sockets[0].url, /access_token=auth_tokens%2Ftest/);
  assert.deepEqual(f.sockets[0].sent[0], { setup: f.session.setup });
  assert.equal(f.sockets[0].sent.filter(x => x.clientContent).length, 1);
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveTool').length, 0);
  assert.equal(f.contexts[0].sampleRate, 16000);
}));
test('microphone streams PCM while assistant audio is playing so speech can interrupt naturally', () => browser(async f => {
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ serverContent: { modelTurn: { parts: [chunk] } } }); await f.flush();
  assert.equal(f.nodes.length, 1);
  f.captures[0].port.onmessage({ data: new Float32Array([0, .5, -1, 1]) });
  assert.equal(f.sockets[0].sent.at(-1).realtimeInput.audio.mimeType, 'audio/pcm;rate=16000');
  assert.equal(f.track.stops, 0);
  f.sockets[0].receive({ serverContent: { interrupted: true } }); await f.flush();
  assert.equal(f.nodes[0].stops, 1);
  assert.deepEqual(f.calls.at(-1), ['OnLiveState', 1, 'listening']);
}));
test('audio chunks are scheduled in order and listening waits for playback completion', () => browser(async f => {
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ serverContent: { modelTurn: { parts: [chunk, chunk] }, turnComplete: true } }); await f.flush();
  assert.ok(f.nodes[1].at > f.nodes[0].at);
  assert.equal(f.calls.filter(x=>x[0]==='OnLiveState'&&x[2]==='speaking').length,1);
  assert.equal(f.calls.at(-1)[2], 'speaking');
  f.nodes[0].onended(); f.nodes[1].onended();
  assert.equal(f.calls.at(-1)[2], 'listening');
}));
test('only explicit valid tool calls reach PropFlow and duplicate call IDs do not repeat searches', () => browser(async f => {
  await f.control.start(f.session, 1);
  const call = { id: 'call-1', name: 'find_facilities', args: { need: 'Tôi muốn đọc sách hôm nay' } };
  f.sockets[0].receive({ toolCall: { functionCalls: [call, call] } }); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveTool').length, 1);
  assert.equal(f.sockets[0].sent.at(-1).toolResponse.functionResponses[0].response.mode, 'NO_MATCH');
  f.sockets[0].receive({ toolCall: { functionCalls: [{ ...call, id: 'bad', name: 'pay_invoice' }] } }); await f.flush();
  assert.equal(f.calls.filter(x => x[0] === 'OnLiveTool').length, 1);
}));
test('interruption cancels pending lookup and discards its late response', () => browser(async f => {
  let resolve; f.setTool(() => new Promise(r => resolve = r));
  await f.control.start(f.session, 1);
  f.sockets[0].receive({ toolCall: { functionCalls: [{ id: 'pending', name: 'find_facilities', args: { need: 'Tìm chỗ bơi' } }] } }); await f.flush();
  f.sockets[0].receive({ toolCallCancellation: { ids: ['pending'] } }); await f.flush();
  resolve({ mode: 'GEMINI' }); await f.flush();
  assert.equal(f.sockets[0].sent.filter(x => x.toolResponse).length, 0);
  assert.ok(f.calls.some(x => x[0] === 'OnLiveToolCancelled'));
}));
test('switching browser tabs keeps the live microphone, audio and connection active', () => browser(async f => {
  await f.control.start(f.session, 2);
  f.document.visibilityState = 'hidden'; f.document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(f.track.stops, 0); assert.equal(f.contexts[0].state, 'running'); assert.equal(f.sockets[0].readyState, 1);
  f.sockets[0].receive({ serverContent: { modelTurn: { parts: [chunk] } } }); await f.flush();
  assert.equal(f.nodes.length, 1); assert.ok(!f.calls.some(x => x[0] === 'OnLiveState' && x[2] === 'paused'));
}));
test('leaving the page releases microphone, audio context and socket; stale messages cannot restart', () => browser(async f => {
  await f.control.start(f.session, 2); const late = f.sockets[0].onmessage;
  f.events.dispatchEvent(new Event('pagehide'));
  assert.equal(f.track.stops, 1); assert.equal(f.contexts[0].state, 'closed'); assert.equal(f.sockets[0].readyState, 3);
  late({ data: JSON.stringify({ serverContent: { modelTurn: { parts: [chunk] } } }) }); await f.flush();
  assert.equal(f.nodes.length, 0);
}));
test('permission denial and disposal during permission prompt never leave a microphone running', () => browser(async f => {
  navigator.mediaDevices.getUserMedia = async () => { const error = Error('denied'); error.name = 'NotAllowedError'; throw error; };
  assert.equal(await f.control.start(f.session, 1), false); assert.equal(f.calls.at(-1)[2], 'not-allowed');
  let grant; navigator.mediaDevices.getUserMedia = () => new Promise(r => grant = r);
  const pending = f.control.start(f.session, 2); f.control.dispose(); grant(f.media);
  assert.equal(await pending, false); assert.equal(f.track.stops, 1); assert.equal(f.sockets.length, 0);
}));
test('mute preserves text and microphone; transcripts accumulate and reset on subsequent turns', () => browser(async f => {
  await f.control.start(f.session, 1); f.control.setMuted(true);
  f.sockets[0].receive({ serverContent: { outputTranscription: { text: 'Mình nghe ' }, modelTurn: { parts: [chunk] } } });
  f.sockets[0].receive({ serverContent: { outputTranscription: { text: 'rõ nhé' }, turnComplete: true } }); await f.flush();
  assert.equal(f.nodes.length, 0); assert.equal(f.track.stops, 0);
  assert.ok(f.calls.some(x => x[0] === 'OnLiveTranscript' && x[3] === 'Mình nghe rõ nhé'));
  assert.equal(f.control.sendText('Alo alo'), true);
  assert.equal(f.sockets[0].sent.at(-1).clientContent.turns[0].parts[0].text, 'Alo alo');
}));
test('invalid hosts, expired tokens, insecure contexts and broken server messages fail safely', () => browser(async f => {
  assert.equal(await f.control.start({ ...f.session, webSocketUrl: 'wss://attacker.test/' }, 1), false);
  assert.equal(await f.control.start({ ...f.session, expiresAt: new Date(0).toISOString() }, 2), false);
  assert.equal(f.sockets.length, 0);
  await f.control.start(f.session, 3); f.sockets[0].receive('not-json'); await f.flush();
  assert.equal(f.track.stops, 1); assert.equal(f.calls.at(-1)[2], 'provider-error');
  globalThis.isSecureContext = false; assert.equal(await f.control.start(f.session, 4), false);
}));
test('PCM conversion downsamples 48kHz to signed little-endian 16kHz and clips extreme samples', () => browser(async f => {
  const pcm = f.module.pcm16(new Float32Array([2, 2, 2, -2, -2, -2, 0, 0, 0]), 48000), view = new DataView(pcm.buffer);
  assert.equal(pcm.length, 6); assert.equal(view.getInt16(0, true), 32767); assert.equal(view.getInt16(2, true), -32768); assert.equal(view.getInt16(4, true), 0);
}));
