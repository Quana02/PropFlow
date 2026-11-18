const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const recognition = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/resident-request-voice.js'), 'utf8');
const dependency = 'data:text/javascript;base64,' + Buffer.from(recognition).toString('base64');
const source = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/facility-assistant-voice.js'), 'utf8')
  .replace("'./resident-request-voice.js?v=20261112-1'", JSON.stringify(dependency));
let fixtureId = 0;
const wav = Buffer.from('RIFF-test-WAVE').toString('base64');

test('bundled greetings are valid Vietnamese WAV assets and the welcome transcript matches the page', () => {
  const folder=path.join(root,'src/PropFlow.Web.Client/wwwroot/audio/facility');
  const metadata=JSON.parse(fs.readFileSync(path.join(folder,'greetings.json'),'utf8').replace(/^\uFEFF/,''));
  assert.equal(metadata.language,'vi-VN');assert.equal(metadata.recordings.length,2);
  for(const item of metadata.recordings){
    assert.equal(path.basename(item.file),item.file);
    const bytes=fs.readFileSync(path.join(folder,item.file));
    assert.equal(bytes.toString('ascii',0,4),'RIFF');assert.equal(bytes.toString('ascii',8,12),'WAVE');
    assert.equal(bytes.readUInt32LE(24),24000);assert.equal(bytes.readUInt16LE(22),1);
    assert.equal(bytes.length,item.bytes);assert.ok(bytes.length>44&&bytes.length<2_000_000);
    assert.equal(require('node:crypto').createHash('sha256').update(bytes).digest('hex').toUpperCase(),item.sha256);
  }
  const page=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/Features/Resident/Portal/Pages/ResidentFacilities.razor.cs'),'utf8');
  assert.equal(page.match(/const string Welcome = "([^"]+)"/)[1],metadata.recordings.find(x=>x.kind==='WELCOME').text);
});

async function browser(run) {
  const keys = ['SpeechRecognition', 'webkitSpeechRecognition', 'isSecureContext', 'Audio', 'speechSynthesis', 'document', 'addEventListener', 'removeEventListener', 'fetch'];
  const previous = Object.fromEntries(keys.map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  const originalCreate = URL.createObjectURL, originalRevoke = URL.revokeObjectURL;
  const microphones = [], players = [], calls = [], revoked = [], events = new EventTarget(), document = new EventTarget();
  document.visibilityState = 'visible'; document.baseURI = 'https://facility.test/'; let foreignVoiceCalls = 0;
  const file = Buffer.alloc(48); file.write('RIFF'); file.write('WAVE', 8);
  class Recognition { constructor() { this.aborts = 0; this.stops = 0; microphones.push(this); } start() {} stop() { this.stops++; } abort() { this.aborts++; } }
  class Audio {
    constructor(url) { this.src = url; this.pauses = 0; players.push(this); }
    async play() { this.onplay?.(); }
    pause() { this.pauses++; }
    removeAttribute(name) { if (name === 'src') this.src = ''; }
    load() {}
  }
  let session;
  try {
    Object.assign(globalThis, { SpeechRecognition: Recognition, webkitSpeechRecognition: undefined, isSecureContext: true, Audio, document,
      fetch: async () => ({ ok:true, arrayBuffer:async () => file.buffer.slice(file.byteOffset,file.byteOffset+file.byteLength) }),
      speechSynthesis: { getVoices: () => [{ lang: 'en-US' }, { lang: 'ja-JP' }], speak: () => { foreignVoiceCalls++; } },
      addEventListener: events.addEventListener.bind(events), removeEventListener: events.removeEventListener.bind(events) });
    URL.createObjectURL = () => 'blob:test-audio'; URL.revokeObjectURL = url => revoked.push(url);
    const module = await import('data:text/javascript;base64,' + Buffer.from(source + '\n// fixture ' + (++fixtureId)).toString('base64'));
    session = module.create({ invokeMethodAsync: async (...args) => { calls.push(args); } });
    await run({ module, session, microphones, players, calls, revoked, document, events, Audio, foreignVoiceCalls: () => foreignVoiceCalls });
  } finally {
    session?.dispose(); URL.createObjectURL = originalCreate; URL.revokeObjectURL = originalRevoke;
    for (const [key, descriptor] of Object.entries(previous)) { if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key]; }
  }
}
test('Vietnamese server audio plays on a machine with only English/Japanese voices; no device speech is used', () => browser(async ({ module, session, players, microphones, calls, foreignVoiceCalls }) => {
  assert.deepEqual(module.capabilities(), { recognition: true, speech: true });
  assert.equal(await session.play(wav, 'audio/wav', 1), true);
  assert.equal(players.length, 1); assert.equal(microphones.length, 0); assert.equal(foreignVoiceCalls(), 0);
  assert.deepEqual(calls.at(-1), ['OnAssistantSpeech', 1, 'speaking']);
}));
test('pressing microphone interrupts audio, revokes its URL and starts Vietnamese recognition', () => browser(async ({ session, players, microphones, revoked }) => {
  await session.play(wav, 'audio/wav', 1); const retired = players[0];
  assert.equal(session.start(2), true); assert.equal(retired.pauses, 1); assert.equal(retired.onended, null);
  assert.equal(revoked.length, 1); assert.equal(microphones[0].lang, 'vi-VN');
}));
test('completion releases audio and returns assistant to idle', () => browser(async ({ session, players, calls, revoked }) => {
  await session.play(wav, 'audio/wav', 5); players[0].onended();
  assert.deepEqual(calls.at(-1), ['OnAssistantSpeech', 5, 'idle']); assert.equal(revoked.length, 1);
}));
test('autoplay blocking reports replay action without requesting a foreign fallback voice', () => browser(async ({ session, Audio, calls, foreignVoiceCalls }) => {
  Audio.prototype.play = async () => { const error = Error('Blocked'); error.name = 'NotAllowedError'; throw error; };
  assert.equal(await session.play(wav, 'audio/wav', 4), false);
  assert.deepEqual(calls.at(-1), ['OnAssistantSpeech', 4, 'playback-blocked']); assert.equal(foreignVoiceCalls(), 0);
}));
test('backgrounding stops both playback and microphone and reports paused state', () => browser(async ({ session, document, microphones, calls }) => {
  session.start(7); document.visibilityState = 'hidden'; document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(microphones[0].aborts, 1); assert.deepEqual(calls.at(-1), ['OnAssistantSpeech', 7, 'paused']);
  assert.equal(await session.play(wav, 'audio/wav', 8), false);
}));
test('invalid or non-audio payload fails safely without playing arbitrary content', () => browser(async ({ session, players, calls }) => {
  assert.equal(await session.play('not-an-audio', 'text/html', 3), false); assert.equal(players.length, 0);
  assert.deepEqual(calls.at(-1), ['OnAssistantSpeech', 3, 'unavailable']);
}));
test('mute and disposal release playback; late callbacks never restart speech', () => browser(async ({ session, players, events, calls, revoked }) => {
  await session.play(wav, 'audio/wav', 9); const current = players[0]; session.stopSpeaking();
  assert.equal(current.pauses, 1); assert.equal(current.onended, null); assert.equal(revoked.length, 1);
  const count = calls.length; session.dispose(); events.dispatchEvent(new Event('pagehide'));
  assert.equal(await session.play(wav, 'audio/wav', 10), false); assert.equal(calls.length, count);
}));

test('dashboard preloads the fixed greeting without sound or microphone and opening reuses that download', () => browser(async ({ module, session, microphones, players }) => {
  let downloads = 0;
  const get = globalThis.fetch;
  globalThis.fetch = async (url, options) => { downloads++; assert.match(url,/\/audio\/facility\/welcome-vi\.wav/);assert.equal(options.cache,'force-cache');return get(); };
  module.preloadGreetings(); module.preloadGreetings();
  assert.equal(players.length,0); assert.equal(microphones.length,0);
  assert.equal(await session.playGreeting('WELCOME',11),true);
  assert.equal(downloads,1);assert.equal(players.length,1);
  assert.equal(await session.playGreeting('WELCOME',12),true);assert.equal(downloads,1);
}));
test('leaving during a greeting download does not play it later', () => browser(async ({ session, players }) => {
  let complete;const get=globalThis.fetch;
  globalThis.fetch=()=>new Promise(resolve=>{complete=resolve;});
  const pending=session.playGreeting('WELCOME',13);session.dispose();complete(await get());
  assert.equal(await pending,false);assert.equal(players.length,0);
}));
test('a failed greeting download can be retried and cannot switch to a foreign voice', () => browser(async ({ session, players, foreignVoiceCalls }) => {
  const get=globalThis.fetch;globalThis.fetch=async()=>({ok:false});
  assert.equal(await session.playGreeting('WELCOME',14),false);assert.equal(players.length,0);
  globalThis.fetch=get;assert.equal(await session.playGreeting('WELCOME',15),true);assert.equal(foreignVoiceCalls(),0);
}));
