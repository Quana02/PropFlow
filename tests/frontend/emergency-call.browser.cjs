// Optional real Chromium smoke test. Uses a fresh browser profile, fake microphone,
// localhost-only signaling, no user accounts, no recording and no external ICE servers.
const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn, execFile } = require('node:child_process');
const { once } = require('node:events');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const browserPath = [process.env.EMERGENCY_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(value => value && fs.existsSync(value));
if (!browserPath) { process.stderr.write('Set EMERGENCY_TEST_BROWSER to a Chromium executable.\n'); process.exit(1); }
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-emergency-browser-'));
const source = fs.readFileSync(path.resolve(__dirname, '../../src/PropFlow.Web.Client/wwwroot/js/emergency-call.js'));
const html = `<!doctype html><html><body><audio id="resident" autoplay></audio><audio id="manager" autoplay></audio><script type="module">
import { create } from '/emergency-call.js';
let resident, manager;
const peers = [], streams = [], states = {}, errors = [];
const NativePeer = window.RTCPeerConnection;
window.RTCPeerConnection = class extends NativePeer { constructor(config) { super(config); peers.push(this); } };
const originalCapture = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
navigator.mediaDevices.getUserMedia = async constraints => { const stream = await originalCapture(constraints); streams.push(stream); return stream; };
const bridge = (name, other) => ({ async invokeMethodAsync(method, ...args) {
  if (method === 'OnRtcSignal') return other().receive(...args);
  if (method === 'OnRtcState' && args[2] === 'connected') states[name] = true;
  if (method === 'OnMediaError') errors.push(args[0]);
} });
try {
  resident = create(bridge('resident', () => manager), document.getElementById('resident'));
  manager = create(bridge('manager', () => resident), document.getElementById('manager'));
  await manager.connect('test-call', 1, false, []);
  await resident.connect('test-call', 1, true, []);
  const deadline = Date.now() + 12000;
  while ((!states.resident || !states.manager) && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 50));
  await new Promise(resolve => setTimeout(resolve, 1000));
  const packets = [];
  for (const peer of peers) { const stats = await peer.getStats(); packets.push([...stats.values()].filter(x => x.type === 'inbound-rtp' && x.kind === 'audio').reduce((sum, x) => sum + (x.packetsReceived || 0), 0)); }
  const connected = !!states.resident && !!states.manager;
  resident.dispose(); manager.dispose();
  const released = streams.every(stream => stream.getTracks().every(track => track.readyState === 'ended')) && peers.every(peer => peer.connectionState === 'closed');
  window.__result = { passed: connected && packets.length === 2 && packets.every(x => x > 0) && released && !errors.length, connected, audioPackets: packets, microphoneReleased: released, errors };
} catch(error) { window.__result = { passed: false, error: error.message }; }
finally { resident?.dispose(); manager?.dispose(); }
</script></body></html>`;
const server = http.createServer((request, response) => {
  response.setHeader('Content-Type', request.url === '/emergency-call.js' ? 'text/javascript' : 'text/html');
  response.end(request.url === '/emergency-call.js' ? source : html);
});
let browser, socket, port;
let sequence = 0;
const pending = new Map();
function send(method, params = {}, sessionId) {
  return new Promise((resolve, reject) => {
    const id = ++sequence;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 15000);
    pending.set(id, { resolve: value => { clearTimeout(timeout); resolve(value); }, reject: error => { clearTimeout(timeout); reject(error); } });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
(async () => {
  try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening'); port = server.address().port;
    browser = spawn(browserPath, ['--headless=new', '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream', '--autoplay-policy=no-user-gesture-required', '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    process.stdout.write(JSON.stringify({ browserPid: browser.pid, serverPort: port }) + '\n');
    const activePort = path.join(profile, 'DevToolsActivePort');
    const deadline = Date.now() + 15000;
    while (!fs.existsSync(activePort) && Date.now() < deadline) await wait(50);
    const [debugPort, endpoint] = fs.readFileSync(activePort, 'utf8').trim().split(/\r?\n/);
    socket = new WebSocket('ws://127.0.0.1:' + debugPort + endpoint); await once(socket, 'open');
    socket.addEventListener('message', event => { const message = JSON.parse(event.data); const callback = pending.get(message.id); if (callback) { pending.delete(message.id); message.error ? callback.reject(new Error(message.error.message)) : callback.resolve(message.result); } });
    const target = await send('Target.createTarget', { url: 'http://127.0.0.1:' + port + '/test' });
    const session = await send('Target.attachToTarget', { targetId: target.targetId, flatten: true });
    let result;
    const end = Date.now() + 20000;
    while (!result && Date.now() < end) {
      const state = await send('Runtime.evaluate', { expression: 'window.__result || null', returnByValue: true }, session.sessionId);
      result = state.result?.value; if (!result) await wait(100);
    }
    if (!result) throw new Error('Browser media test timed out.');
    process.stdout.write(JSON.stringify(result) + '\n'); if (!result.passed) process.exitCode = 1;
  } catch(error) { process.stderr.write(error.message + '\n'); process.exitCode = 1; }
  finally {
    if (socket?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} socket.close(); }
    if (browser && browser.exitCode === null) {
      await Promise.race([once(browser, 'exit'), wait(5000)]);
      if (browser.exitCode === null) {
        if (process.platform === 'win32') await new Promise(resolve => execFile('taskkill', ['/PID', String(browser.pid), '/T', '/F'], { windowsHide: true }, resolve));
        else browser.kill('SIGKILL');
      }
    }
    server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
    for (const entry of pending.values()) entry.reject(new Error('Browser closed')); pending.clear();
    const resolved = path.resolve(profile);
    if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('propflow-emergency-browser-')) fs.rmSync(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    process.stdout.write(JSON.stringify({ cleanupComplete: true, serverPort: port }) + '\n');
  }
})();
