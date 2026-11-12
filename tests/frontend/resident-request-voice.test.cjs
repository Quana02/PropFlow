const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../src/PropFlow.Web.Client/wwwroot/js/resident-request-voice.js'), 'utf8');
const modulePromise = import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

async function browser(run) {
  const keys = ['SpeechRecognition', 'webkitSpeechRecognition', 'isSecureContext', 'setTimeout', 'clearTimeout'];
  const previous = Object.fromEntries(keys.map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  const instances = [], calls = [], timers = new Map();
  class Recognition {
    constructor() { instances.push(this); this.starts = this.stops = this.aborts = 0; }
    start() { this.starts++; }
    stop() { this.stops++; }
    abort() { this.aborts++; }
  }
  try {
    globalThis.SpeechRecognition = Recognition;
    globalThis.webkitSpeechRecognition = undefined;
    globalThis.isSecureContext = true;
    globalThis.setTimeout = callback => { const id = timers.size + 1; timers.set(id, callback); return id; };
    globalThis.clearTimeout = id => timers.delete(id);
    const module = await modulePromise;
    const session = module.create({ invokeMethodAsync: async (...args) => { calls.push(args); } });
    await run({ module, session, instances, calls, timers, flush: () => new Promise(setImmediate) });
    session.dispose();
  } finally {
    for (const [key, descriptor] of Object.entries(previous)) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else delete globalThis[key];
    }
  }
}
const result = (text, final) => Object.assign([{ transcript: text }], { isFinal: final });

test('unsupported and insecure browsers do not open a microphone; prefixed recognition is detected', () => browser(async ({ module, session, instances }) => {
  globalThis.isSecureContext = false;
  assert.equal(module.isSupported(), false);
  assert.equal(session.start(1), false);
  assert.equal(instances.length, 0);
  globalThis.isSecureContext = true;
  globalThis.webkitSpeechRecognition = globalThis.SpeechRecognition;
  globalThis.SpeechRecognition = undefined;
  assert.equal(module.isSupported(), true);
}));

test('Vietnamese recognition distinguishes interim text from cumulative final text without appending duplicate segments', () => browser(async ({ session, instances, calls, flush }) => {
  assert.equal(session.start(10), true);
  const recognition = instances[0];
  assert.equal(recognition.lang, 'vi-VN');
  assert.equal(recognition.continuous, true);
  assert.equal(recognition.interimResults, true);
  recognition.onresult({ results: [result('tôi muốn đăng ký xe', true), result('xe màu', false)] });
  recognition.onresult({ results: [result('tôi muốn đăng ký xe', true), result('xe màu đỏ', true)] });
  await flush();
  assert.deepEqual(calls, [
    ['OnVoiceResult', 10, 'tôi muốn đăng ký xe', 'xe màu'],
    ['OnVoiceResult', 10, 'tôi muốn đăng ký xe xe màu đỏ', '']
  ]);
}));

test('stop allows the final result to reach the form before the completion callback triggers AI review', () => browser(async ({ session, instances, calls, flush }) => {
  session.start(20);
  const recognition = instances[0];
  session.stop();
  assert.equal(recognition.stops, 1);
  recognition.onresult({ results: [result('khách đến sáng mai', true)] });
  recognition.onend();
  await flush();
  assert.deepEqual(calls, [['OnVoiceResult', 20, 'khách đến sáng mai', ''], ['OnVoiceEnded', 20, '']]);
}));

test('silence ends a session, while resuming speech clears the pending silence timer', () => browser(async ({ session, instances, timers }) => {
  session.start(1);
  const recognition = instances[0];
  recognition.onspeechend();
  assert.equal(timers.size, 1);
  recognition.onspeechstart();
  assert.equal(timers.size, 0);
  recognition.onspeechend();
  [...timers.values()][0]();
  assert.equal(recognition.stops, 1);
}));

test('cancelling a dialog aborts recognition and ignores late events from that session', () => browser(async ({ session, instances, calls, flush }) => {
  session.start(1);
  const old = instances[0], lateResult = old.onresult, lateEnd = old.onend;
  session.cancel();
  session.start(2);
  lateResult({ results: [result('nội dung cũ', true)] });
  lateEnd();
  await flush();
  assert.equal(old.aborts, 1);
  assert.deepEqual(calls, []);
}));

test('microphone denial is delivered as an error instead of a successful empty transcript', () => browser(async ({ session, instances, calls, flush }) => {
  session.start(1);
  const recognition = instances[0];
  recognition.onerror({ error: 'not-allowed' });
  recognition.onend();
  await flush();
  assert.deepEqual(calls, [['OnVoiceEnded', 1, 'not-allowed']]);
}));

test('disposal aborts capture, clears timers, and prevents callbacks and future restarts', () => browser(async ({ session, instances, calls, timers, flush }) => {
  session.start(1);
  const recognition = instances[0], lateEnd = recognition.onend;
  recognition.onspeechend();
  session.dispose();
  lateEnd();
  await flush();
  assert.equal(recognition.aborts, 1);
  assert.equal(timers.size, 0);
  assert.equal(session.start(2), false);
  assert.deepEqual(calls, []);
}));

test('a browser stop error releases the form from its listening state', () => browser(async ({ session, instances, calls, flush }) => {
  session.start(3);
  instances[0].stop = () => { throw new Error('already stopped'); };
  session.stop();
  await flush();
  assert.deepEqual(calls, [['OnVoiceEnded', 3, 'unknown']]);
}));
