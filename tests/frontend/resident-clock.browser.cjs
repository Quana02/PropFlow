// Real-browser checks using the chatbot's stylesheet and drag module.
// A fresh profile and localhost-only fixture keep user sessions and data untouched.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const { spawn, execFile } = require('node:child_process');
const { once } = require('node:events');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const root = path.resolve(__dirname, '../..');
const css=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/wwwroot/css/resident-portal.css'),'utf8')+'\n'+fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/Features/Communication/Components/NotificationBell.razor.css'),'utf8');
const moduleSource=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/wwwroot/js/resident-clock.js'),'utf8');
const headerSource=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/Features/Resident/Portal/Components/ResidentHeader.razor'),'utf8');
const compact=headerSource.match(/<div class="resident-clock-compact"[\s\S]*?<\/div>/)?.[0] || headerSource.match(/<div class="resident-clock-compact glass-panel"[\s\S]*?<\/div>/)[0];
const stage=fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/Features/Resident/Portal/Components/ResidentDashboardClock.razor'),'utf8').match(/<div class="resident-clock-stage"[\s\S]*?\n<\/div>/)[0].replace(' @ref="element"','');
const html=`<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body><div class="resident-portal"><div class="resident-page-frame"><header class="resident-header"><a class="resident-brand glass-panel"><span class="resident-brand-mark">P</span><span class="resident-brand-content"><strong>PropFlow RESIDENT</strong><small>Căn hộ DEMO-1801 · Tầng 18</small></span></a><div class="resident-header-actions"><div class="resident-context-strip">${compact}<div class="resident-weather glass-panel" data-building-time-zone="Asia/Ho_Chi_Minh"><span class="weather-icon">☀</span><span class="weather-copy"><strong>—°C</strong><small>Chưa có dữ liệu</small><em>Chưa cấu hình địa chỉ chung cư</em></span></div></div><button class="resident-theme-toggle glass-panel">☀</button><button class="resident-profile-trigger glass-panel"><span class="resident-avatar">RD</span><span class="resident-profile-copy"><strong>Resident Demo</strong><small>Căn hộ DEMO-1801 · Tầng 18 · Chủ hộ</small></span></button><div class="notification-bell"><button class="notification-trigger">♧</button></div></div></header><main class="resident-main"><section class="resident-hero"><div class="resident-greeting">Xin chào cư dân</div>${stage}</section><div style="height:1600px;padding:24px">Dịch vụ cư dân</div></main></div></div><script type="module">import { attach } from '/resident-clock.js';window.clockHandle=attach(document.querySelector('.resident-clock-stage'));window.ready=true;</script></body></html>`;
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(p => p && fs.existsSync(p));
if (!browserPath) throw new Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-resident-layout-'));
const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', req.url === '/resident-clock.js' ? 'text/javascript' : 'text/html; charset=utf-8');
  res.end(req.url === '/resident-clock.js' ? moduleSource : html);
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


    const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true},session);if(r.exceptionDetails)throw Error(r.exceptionDetails.text);return r.result.value;};
    const settled=async docked=>{
      for(let i=0;i<40;i++) {
        if(await evaluate('document.querySelector(".resident-portal").classList.contains("resident-clock-docked") === '+docked+' && getComputedStyle(document.querySelector(".resident-clock-stage")).opacity === "'+(docked?'0':'1')+'" && getComputedStyle(document.querySelector(".resident-clock-compact")).opacity === "'+(docked?'1':'0')+'"'))return;
        await wait(50);
      }
      throw Error('Clock transition did not settle');
    };
    for(let i=0;i<100 && !await evaluate('window.ready===true');i++)await wait(50);
    const checks=[];
    for(const width of [1366,1024,820,390,320]) {
      await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false},session);
      for(const dark of [false,true]) {
        await evaluate('document.documentElement.classList.toggle("dark",'+dark+');window.scrollTo(0,0)');await settled(false);
        const top=await evaluate('({docked:document.querySelector(".resident-portal").classList.contains("resident-clock-docked"),heroOpacity:getComputedStyle(document.querySelector(".resident-clock-stage")).opacity,compactOpacity:getComputedStyle(document.querySelector(".resident-clock-compact")).opacity,time:document.getElementById("clockTime").textContent,search:!!document.querySelector("#residentSearch")})');
        await evaluate('window.scrollTo(0,400)');await settled(true);
        const scrolled=await evaluate('(()=>{const compact=document.querySelector(".resident-clock-compact"), c=compact.getBoundingClientRect(),w=document.querySelector(".resident-weather").getBoundingClientRect(),h=document.querySelector(".resident-header").getBoundingClientRect();return {docked:document.querySelector(".resident-portal").classList.contains("resident-clock-docked"),opacity:getComputedStyle(compact).opacity,leftOfWeather:c.right<=w.left+1,aligned:Math.abs((c.y+c.height/2)-(w.y+w.height/2))<12,headerPinned:h.top>=0&&h.top<=17,inViewport:c.left>=0&&w.right<=innerWidth+1,overflow:document.documentElement.scrollWidth>innerWidth+1,sameTime:compact.querySelector("[data-clock-time]").textContent===document.getElementById("clockTime").textContent};})()');
        const passed=!top.docked&&top.heroOpacity==='1'&&top.compactOpacity==='0'&&/^\d{2}:\d{2}$/.test(top.time)&&!top.search&&scrolled.docked&&scrolled.opacity==='1'&&scrolled.leftOfWeather&&scrolled.aligned&&scrolled.headerPinned&&scrolled.inViewport&&!scrolled.overflow&&scrolled.sameTime;
        const controls=await evaluate('(()=>{const boxes=[...document.querySelectorAll(".resident-brand,.resident-clock-compact,.resident-weather,.resident-theme-toggle,.resident-profile-trigger,.notification-trigger")].map(x=>({name:x.className,height:x.getBoundingClientRect().height}));return {boxes,equal:Math.max(...boxes.map(x=>x.height))-Math.min(...boxes.map(x=>x.height))<1};})()');
        checks.push({width,dark,top,scrolled,controls,passed:passed&&controls.equal});
      }
    }
    await evaluate('window.clockHandle.dispose()');
    if(await evaluate('document.querySelector(".resident-portal").classList.contains("resident-clock-docked")'))throw Error('Clock listeners were not cleaned up');
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
