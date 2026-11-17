// Real-browser layout check using the utility component rendered by HtmlRenderer and its actual stylesheet.
// A fresh profile and localhost-only fixture keep user sessions and data untouched.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn, execFile } = require('node:child_process');
const { once } = require('node:events');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const root = path.resolve(__dirname, '../..');
const preview=path.join(os.tmpdir(),'propflow-utility-insight-preview.html');
if(!fs.existsSync(preview))throw Error('Run UtilityInsightUiTests with PROPFLOW_UTILITY_PREVIEW_FILE first.');
const css=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/obj/Fe10Verification/net8.0/scopedcss/projectbundle/PropFlow.Web.Client.bundle.scp.css'),'utf8')+'\n'+fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/Layout/Shared/AppShell.razor.css'),'utf8');
const html='<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:Segoe UI,Arial,sans-serif}*{box-sizing:border-box}.app-shell{padding:16px}.utility-detail{max-width:1100px;margin:0 auto}'+css+'</style></head><body><div class="app-shell">'+fs.readFileSync(preview,'utf8')+'</div></body></html>';
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(p => p && fs.existsSync(p));
if (!browserPath) throw new Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-resident-layout-'));
const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', req.url === '/resident.css' ? 'text/css' : 'text/html; charset=utf-8');
  res.end(req.url === '/resident.css' ? css : html);
});
let browser, socket, sequence = 0, port;
const pending = new Map();
function send(method, params = {}, sessionId) {
  return new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Browser command timed out.')); }, 15000);
    pending.set(id, { resolve: x => { clearTimeout(timer); resolve(x); }, reject: e => { clearTimeout(timer); reject(e); } });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
(async () => {
  try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening'); port = server.address().port;
    browser = spawn(browserPath, ['--headless=new', '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    console.log(JSON.stringify({ browserPid: browser.pid, serverPort: port }));
    const activePort = path.join(profile, 'DevToolsActivePort');
    const deadline = Date.now() + 15000;
    while (!fs.existsSync(activePort) && Date.now() < deadline) await wait(50);
    const [debugPort, endpoint] = fs.readFileSync(activePort, 'utf8').trim().split(/\r?\n/);
    socket = new WebSocket('ws://127.0.0.1:' + debugPort + endpoint); await once(socket, 'open');
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data), callback = pending.get(message.id);
      if (callback) { pending.delete(message.id); message.error ? callback.reject(new Error(message.error.message)) : callback.resolve(message.result); }
    });
    const target = await send('Target.createTarget', { url: 'http://127.0.0.1:' + port });
    const session = (await send('Target.attachToTarget', { targetId: target.targetId, flatten: true })).sessionId;

    const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true},session);if(r.exceptionDetails)throw Error(r.exceptionDetails.text);return r.result.value;};
    for(let i=0;i<100&&!await evaluate('!!document.querySelector(".utility-detail")');i++)await wait(50);
    const checks=[];
    for(const width of [1366,820,390,320]) {
      await send('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false},session);
      for(const dark of [false,true]) {
        await evaluate('document.documentElement.classList.toggle("dark",'+dark+');document.querySelector(".app-shell").classList.toggle("app-shell-dark",'+dark+');document.querySelector("details").open=true');await wait(750);
        const result=await evaluate('(()=>{const root=document.querySelector(".utility-detail"),cards=[...document.querySelectorAll(".utility-signal")],bar=document.querySelectorAll(".utility-bar-column"),bounds=root.getBoundingClientRect();return {width:innerWidth,dark:document.documentElement.classList.contains("dark"),overflow:document.documentElement.scrollWidth>innerWidth+1,cardsFit:cards.every(c=>c.getBoundingClientRect().left>=bounds.left&&c.getBoundingClientRect().right<=bounds.right+1),twoColumns:innerWidth>760?Math.abs(cards[0].getBoundingClientRect().top-cards[1].getBoundingClientRect().top)<1:cards[1].getBoundingClientRect().top>cards[0].getBoundingClientRect().bottom,barCount:bar.length,color:getComputedStyle(root).color,background:getComputedStyle(root).backgroundColor};})()');
        result.passed=!result.overflow&&result.cardsFit&&result.twoColumns&&result.barCount===18;checks.push(result);
        if(width===1366||width===390){const screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},session);fs.writeFileSync(path.join(os.tmpdir(),'propflow-utility-'+width+'-'+(dark?'dark':'light')+'.png'),Buffer.from(screenshot.data,'base64'));}
      }
    }
    console.log(JSON.stringify({passed:checks.every(x=>x.passed),checks}));if(checks.some(x=>!x.passed))process.exitCode=1;
  } catch (e) { console.error(e.message); process.exitCode = 1; }
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
    if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('propflow-resident-layout-')) fs.rmSync(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    console.log(JSON.stringify({ cleanupComplete: true, serverPort: port }));
  }
})();
