// Exercise the real WASM page, timers and dialog lifecycle. All APIs and the Live peer are test doubles;
// no application database, user browser profile or external AI credentials are used.
const fs = require('node:fs'), path = require('node:path'), os = require('node:os'), net = require('node:net');
const { spawn, execFile } = require('node:child_process'), { once } = require('node:events');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), configuration = process.env.PROPFLOW_TEST_CONFIGURATION || 'LiveVerification';
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-idle-runtime-'));
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p => p && fs.existsSync(p));
let host, browser, socket, port, debugPort, sequence = 0, hostOutput = '';
const pending = new Map();
const freePort = async () => { const s = net.createServer(); s.listen(0, '127.0.0.1'); await once(s, 'listening'); const p = s.address().port; await new Promise(resolve => s.close(resolve)); return p; };
function send(method, params = {}, sessionId) {
  return new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(Error('Browser command timed out: ' + method)); }, 20000);
    pending.set(id, { resolve: value => { clearTimeout(timer); resolve(value); }, reject: error => { clearTimeout(timer); reject(error); } });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
async function stop(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === 'win32') await new Promise(resolve => execFile('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true }, resolve));
  else child.kill('SIGTERM');
  if (child.exitCode === null) await Promise.race([once(child, 'exit'), wait(5000)]);
}
const installFakes = `(() => {
  const originalFetch = window.fetch.bind(window), id = '083485b5-2dcc-45cb-bae4-92fec9b89625';
  const probe = window.idleRuntimeProbe = { requests: [], stopped: 0, states: [] };
  const expiresAt = new Date(Date.now() + 3600000).toISOString();
  window.fetch = async (input, init) => {
    const url = new URL(typeof input === 'string' ? input : input.url, location.href), route = url.pathname;
    if (route === '/appsettings.json' || route.startsWith('/appsettings.')) return new Response(JSON.stringify({ Api: { BaseUrl: 'https://idle-test.invalid/' } }), { headers: { 'Content-Type': 'application/json' } });
    if (!route.startsWith('/api/') && !route.startsWith('/hubs/')) return originalFetch(input, init);
    probe.requests.push(route);
    let status = 200, value;
    if (route.endsWith('/auth/csrf')) value = { token: 'test-csrf' };
    else if (route.endsWith('/auth/refresh')) value = { accessToken: 'test-only.invalid.signature', expiresAt,
      user: { id, username: 'idle-test', displayName: 'Idle test resident', role: 'RESIDENT', status: 'ACTIVE', emailVerified: true, permissions: [] } };
    else if (route.endsWith('/residents/me')) value = { id, residentCode: 'TEST-IDLE', fullName: 'Idle test resident', status: 'ACTIVE', hasAccount: true, ownerships: [], residencies: [] };
    else if (route.endsWith('/facility-recommendations/live')) value = { token: 'auth_tokens/test-only', webSocketUrl: 'wss://generativelanguage.googleapis.com/ws/test', setup: { model: 'models/test-only' }, expiresAt: new Date(Date.now() + 600000).toISOString() };
    else if (route.endsWith('/facility-recommendations')) value = { today: '2026-11-23', facilities: [{ id, name: 'Test library', type: 'LIBRARY', location: 'Test floor', description: 'Test only', audience: 'ALL', childPolicy: 'GUARDIAN_REQUIRED', hours: [], closedDates: [], responsibleName: 'Test staff', contactDetails: 'Test only' }] };
    else if (route.includes('billing/invoices')) value = [];
    else if (route.includes('/notifications')) value = { items: [], totalCount: 0, unreadCount: 0, pageIndex: 1, pageSize: 20 };
    else { status = 503; value = { title: 'Not part of this isolated test' }; }
    return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  };
  const NativeSocket = window.WebSocket;
  window.WebSocket = class extends EventTarget {
    static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3;
    constructor(url, protocols) {
      super(); if (!url.startsWith('wss://generativelanguage.googleapis.com/')) return new NativeSocket(url, protocols);
      this.readyState = 1; this.bufferedAmount = 0; probe.peer = this; setTimeout(() => this.onopen?.(), 0);
    }
    send(text) { const data = JSON.parse(text);
      if (data.setup) setTimeout(() => this.onmessage?.({ data: JSON.stringify({ setupComplete: {} }) }), 0);
      if (data.clientContent) setTimeout(() => this.onmessage?.({ data: JSON.stringify({ serverContent: { outputTranscription: { text: 'Xin chào! Hôm nay bạn muốn làm gì?' }, turnComplete: true } }) }), 0);
    }
    close() { this.readyState = 3; }
  };
  navigator.mediaDevices.getUserMedia = async () => {
    const context = new AudioContext(), destination = context.createMediaStreamDestination();
    probe.inputContext = context;
    for (const track of destination.stream.getTracks()) { const original = track.stop.bind(track); track.stop = () => { probe.stopped++; clearInterval(probe.noise); original(); void context.close(); }; }
    return destination.stream;
  };
  const NativeCapture = window.AudioWorkletNode;
  window.AudioWorkletNode = class extends NativeCapture { constructor(...args) { super(...args); probe.capture = this; } };
})()`;
(async () => {
  try {
    assert.ok(browserPath, 'Chromium is required');
    port = await freePort();
    host = spawn('dotnet', ['run', '--project', 'src/PropFlow.Web/PropFlow.Web.csproj', '--no-build', '-c', configuration, '--no-launch-profile', '--', '--urls', 'http://127.0.0.1:' + port],
      { cwd: root, windowsHide: true, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', MSBUILDDISABLENODEREUSE: '1' }, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout.on('data', x => { hostOutput += x; }); host.stderr.on('data', x => { hostOutput += x; });
    console.log(JSON.stringify({ phase: 'host', hostPid: host.pid, port }));
    for (let i = 0; i < 300 && !hostOutput.includes('Now listening on:'); i++) { if (host.exitCode !== null) throw Error('Test host exited'); await wait(100); }
    assert.ok(hostOutput.includes('Now listening on:'), 'Test host did not start');
    browser = spawn(browserPath, ['--headless=new', '--mute-audio', '--ignore-certificate-errors', '--autoplay-policy=no-user-gesture-required', '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    const activePort = path.join(profile, 'DevToolsActivePort');
    for (let i = 0; i < 300 && !fs.existsSync(activePort); i++) await wait(50);
    const active = fs.readFileSync(activePort, 'utf8').trim().split(/\r?\n/); debugPort = Number(active[0]);
    console.log(JSON.stringify({ phase: 'browser', browserPid: browser.pid, debugPort }));
    socket = new WebSocket('ws://127.0.0.1:' + debugPort + active[1]); await once(socket, 'open');
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data), callback = pending.get(message.id);
      if (callback) { pending.delete(message.id); message.error ? callback.reject(Error(message.error.message)) : callback.resolve(message.result); }
    });
    const target = await send('Target.createTarget', { url: 'about:blank' });
    const session = (await send('Target.attachToTarget', { targetId: target.targetId, flatten: true })).sessionId;
    await send('Page.enable', {}, session); await send('Runtime.enable', {}, session);
    const errors = [];
    socket.addEventListener('message', event => { const m = JSON.parse(event.data); if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.text); });
    const evaluate = async expression => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }, session); if (r.exceptionDetails) throw Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
    await send('Page.addScriptToEvaluateOnNewDocument', { source: installFakes }, session);
    await send('Page.navigate', { url: 'http://127.0.0.1:' + port + '/resident/facilities' }, session);
    let listening = false;
    for (let i = 0; i < 600; i++) { listening = await evaluate("document.querySelector('.assistant-status')?.innerText.includes('Mình đang nghe bạn') === true"); if (listening) break; await wait(100); }
    if (!listening) console.log(JSON.stringify({ phase: 'entry-failure', text: await evaluate('document.body.innerText.slice(-2200)'), errors }));
    assert.ok(listening, 'Real WASM page must enter the listening state');
    console.log(JSON.stringify({ phase: 'listening' }));
    if (process.argv.includes('--noise')) await evaluate(`(() => {
      const capture = idleRuntimeProbe.capture, frame = amplitude => capture.port.onmessage?.({data:new Float32Array(1024).fill(amplitude)});
      for(let i=0;i<24;i++)frame(.002);for(let i=0;i<4;i++)frame(.08);
      idleRuntimeProbe.noise = setInterval(()=>frame(.03),64);
    })()`);
    const realtime = process.argv.includes('--realtime');
    async function advance(seconds) {
      if (realtime) { for (let i = 0; i < seconds; i++) await wait(1000); }
      else {
        await send('Emulation.setVirtualTimePolicy', { policy: 'advance', budget: seconds * 1000 }, session);
        await wait(1200);
      }
    }
    await advance(61);
    const warning = await evaluate("({open:!!document.querySelector('dialog[open] .assistant-idle-countdown'), seconds:document.querySelector('.assistant-idle-countdown strong')?.innerText, text:document.querySelector('.assistant-status')?.innerText})");
    console.log(JSON.stringify({ phase: 'after-61-seconds', ...warning }));
    assert.ok(warning.open, 'After 60 seconds of silence the real WASM page must open the idle warning');
    assert.ok(Number(warning.seconds) > 0 && Number(warning.seconds) <= 30);
    await advance(31);
    const result = await evaluate("({path:location.pathname, stopped:idleRuntimeProbe.stopped, socketClosed:idleRuntimeProbe.peer.readyState===3, dialogClosed:!document.querySelector('dialog[open] .assistant-idle-countdown')})");
    console.log(JSON.stringify({ phase: 'after-deadline', ...result }));
    assert.equal(result.path, '/resident'); assert.ok(result.stopped > 0 && result.socketClosed && result.dialogClosed);
    console.log(JSON.stringify({ passed: true }));
  } catch (error) { console.error(error.message); if (host?.exitCode !== null) console.error(hostOutput.slice(-3000)); process.exitCode = 1; }
  finally {
    if (socket?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} socket.close(); }
    await stop(browser); await stop(host);
    for (const entry of pending.values()) entry.reject(Error('Browser closed')); pending.clear();
    const resolved = path.resolve(profile);
    if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('propflow-idle-runtime-')) fs.rmSync(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    console.log(JSON.stringify({ cleanupComplete: true, hostPid: host?.pid, browserPid: browser?.pid, port, debugPort }));
  }
})();
