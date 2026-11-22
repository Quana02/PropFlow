// Rendered Blazor pages and real scoped styles; isolated browser profile, no user session or API writes.
const fs = require('node:fs'), path = require('node:path'), os = require('node:os'), http = require('node:http');
const { spawn, execFile } = require('node:child_process');
const { once } = require('node:events');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const root = path.resolve(__dirname, '../..');
const greetingOnly = process.argv.includes('--greeting-only');
const liveVoice = process.argv.includes('--live-voice');
const roles = liveVoice ? ['welcome', 'resident', 'manager', 'live-listening', 'live-speaking'] : process.argv.includes('--continuous-voice') ? ['welcome', 'resident', 'manager', 'listening', 'speaking'] : ['welcome', 'resident', 'manager'];
const scopedStyles = [process.env.PROPFLOW_TEST_CONFIGURATION || 'Debug', 'Fe10Verification'].map(configuration => path.join(root, `src/PropFlow.Web.Client/obj/${configuration}/net8.0/scopedcss/projectbundle/PropFlow.Web.Client.bundle.scp.css`)).find(file => fs.existsSync(file));
const css = fs.readFileSync(path.join(root, 'src/PropFlow.Web/wwwroot/app.css'), 'utf8') + '\n'
  + fs.readFileSync(scopedStyles, 'utf8')
  + '\n' + fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Layout/Shared/AppShell.razor.css'), 'utf8')
  + '\n' + fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/css/chat-composer.css'), 'utf8');
const pages = Object.fromEntries((greetingOnly ? ['welcome'] : roles).map(role => {
  if(greetingOnly)return [role,'<!doctype html><html lang="vi"><head><meta charset="utf-8"><base href="/"></head><body>Greeting playback test</body></html>'];
  const file = path.join(os.tmpdir(), `propflow-facility-${role}.html`);
  if (!fs.existsSync(file)) throw Error('Run FacilityDiscoveryUiTests with PROPFLOW_FACILITY_PREVIEW_DIR set to TEMP.');
  return [role, '<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>'
    + 'body{margin:0;font-family:Segoe UI,Arial,sans-serif}*{box-sizing:border-box}.app-shell{padding:16px}' + css
    + '</style></head><body><div class="app-shell">' + fs.readFileSync(file, 'utf8') + '</div></body></html>'];
}));
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium'].find(p => p && fs.existsSync(p));
if (!browserPath) throw Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-facility-layout-'));
let greetingDownloads = 0, speechApiCalls = 0;
const publicAssets = {
  '/js/facility-assistant-voice.js': ['js/facility-assistant-voice.js','text/javascript'],
  '/js/resident-request-voice.js': ['js/resident-request-voice.js','text/javascript'],
  '/js/facility-live-voice.js': ['js/facility-live-voice.js','text/javascript'],
  '/js/facility-live-capture.js': ['js/facility-live-capture.js','text/javascript'],
  '/audio/facility/welcome-vi.wav': ['audio/facility/welcome-vi.wav','audio/wav'],
  '/audio/facility/new-need-vi.wav': ['audio/facility/new-need-vi.wav','audio/wav']
};
const server = http.createServer((req, res) => {
  const pathname = new URL(req.url,'http://localhost').pathname;
  if(pathname.startsWith('/api/')){speechApiCalls++;res.writeHead(503);res.end();return;}
  if(Object.hasOwn(publicAssets,pathname)){
    const [file,type]=publicAssets[pathname];if(pathname.endsWith('welcome-vi.wav'))greetingDownloads++;
    res.setHeader('Content-Type',type);res.setHeader('Cache-Control','public, max-age=86400');
    res.end(fs.readFileSync(path.join(root,'src/PropFlow.Web.Client/wwwroot',file)));return;
  }
  res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(pages[pathname.slice(1)] || pages.resident);
});
let browser, socket, sequence = 0, port, debugPort;
const pending = new Map();
function send(method, params = {}, sessionId) {
  return new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(Error('Browser command timed out.')); }, 15000);
    pending.set(id, { resolve: x => { clearTimeout(timer); resolve(x); }, reject: e => { clearTimeout(timer); reject(e); } });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
(async () => {
  try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening'); port = server.address().port;
    browser = spawn(browserPath, ['--headless=new', '--mute-audio', ...(liveVoice ? ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] : []), '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    console.log(JSON.stringify({ browserPid: browser.pid, serverPort: port }));
    const activePort = path.join(profile, 'DevToolsActivePort'), deadline = Date.now() + 15000;
    while (!fs.existsSync(activePort) && Date.now() < deadline) await wait(50);
    const active = fs.readFileSync(activePort, 'utf8').trim().split(/\r?\n/); debugPort = Number(active[0]);
    socket = new WebSocket('ws://127.0.0.1:' + debugPort + active[1]); await once(socket, 'open');
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data), callback = pending.get(message.id);
      if (callback) { pending.delete(message.id); message.error ? callback.reject(Error(message.error.message)) : callback.resolve(message.result); }
    });
    const target = await send('Target.createTarget', { url: 'about:blank' });
    const session = (await send('Target.attachToTarget', { targetId: target.targetId, flatten: true })).sessionId;
    const evaluate = async expression => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture:true }, session); if (r.exceptionDetails) throw Error(r.exceptionDetails.text); return r.result.value; };
    const checks = [];
    for (const role of (greetingOnly ? [] : roles)) {
      await send('Page.navigate', { url: 'http://127.0.0.1:' + port + '/' + role }, session);
      const selector = role === 'manager' ? '.facility-guides' : '.resident-facility-discovery';
      for (let i = 0; i < 100 && !await evaluate('!!document.querySelector(' + JSON.stringify(selector) + ')'); i++) await wait(50);
      for (const width of [1366, 820, 390, 320]) {
        await send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false }, session);
        for (const dark of [false, true]) {
          await evaluate(`document.documentElement.classList.toggle('dark',${dark});document.documentElement.dataset.theme='${dark?'dark':'light'}';document.querySelector('.app-shell').classList.toggle('app-shell-dark',${dark})`); await wait(750);
          const expected = role === 'resident' ? 'Nhân viên phụ trách' : role === 'welcome' ? 'Chạm để nói' : role === 'manager' ? 'Giờ mở và đóng cửa' : role.endsWith('listening') ? 'Mình đang nghe bạn' : 'Ngắt lời và nói tiếp';
          const result = await evaluate(`(()=>{const root=document.querySelector(${JSON.stringify(selector)}),bounds=root.getBoundingClientRect(),fields=[...root.querySelectorAll('input:not([type=checkbox]),textarea,select')];return {overflow:document.documentElement.scrollWidth>innerWidth+1,fieldsFit:fields.every(c=>{const r=c.getBoundingClientRect();return r.left>=bounds.left-1&&r.right<=bounds.right+1}),color:getComputedStyle(root).color,hasContent:root.innerText.includes(${JSON.stringify(expected)})};})()`);
          result.role = role; result.width = width; result.dark = dark;
          result.passed = !result.overflow && result.fieldsFit && result.hasContent && (dark ? result.color === (role==='manager'?'rgb(241, 247, 255)':'rgb(237, 246, 255)') : result.color === (role !== 'manager' ? 'rgb(20, 39, 66)' : 'rgb(23, 36, 58)')); checks.push(result);
          if (width === 1366 || width === 390) {
            const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true }, session);
            fs.writeFileSync(path.join(os.tmpdir(), `propflow-facility-${role}-${width}-${dark ? 'dark' : 'light'}.png`), Buffer.from(shot.data, 'base64'));
          }
        }
      }
    }
    if(greetingOnly){await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/welcome'},session);for(let i=0;i<100&&!await evaluate('location.pathname==="/welcome"');i++)await wait(50);}
    const greeting=await evaluate(`(async()=>{
      const voice=await import('/js/facility-assistant-voice.js?latency-test');const states=[];
      const control=voice.create({invokeMethodAsync:async(name,epoch,state)=>{states.push(state);}});
      try{
        performance.clearResourceTimings();voice.preloadGreetings();
        const deadline=performance.now()+5000;
        while(!performance.getEntriesByType('resource').some(x=>x.name.includes('welcome-vi.wav'))&&performance.now()<deadline)await new Promise(r=>setTimeout(r,20));
        const samples=[];
        for(let i=0;i<5;i++){const start=performance.now();const played=await control.playGreeting('WELCOME',100+i);samples.push({played,ms:Math.round((performance.now()-start)*10)/10});control.stopSpeaking();}
        return {samples,speakingObserved:states.includes('speaking'),maxStartMs:Math.max(...samples.map(x=>x.ms))};
      }finally{control.dispose();}
    })()`);
    greeting.downloads=greetingDownloads;greeting.speechApiCalls=speechApiCalls;
    // Live entry uses the model's audio stream. The legacy bundled-greeting benchmark applies only to legacy runs.
    greeting.passed=greeting.samples.every(x=>x.played&&(liveVoice||x.ms<1000))&&greeting.speakingObserved&&greetingDownloads===1&&speechApiCalls===0;
    let live;
    if(liveVoice){
      await evaluate(`(async()=>{
      const module=await import('/js/facility-live-voice.js?browser-test');const original=window.WebSocket;let peer;const sent=[],states=[];
      class FakeSocket{constructor(){peer=this;this.readyState=1;this.bufferedAmount=0;setTimeout(()=>this.onopen?.(),0);}send(text){const data=JSON.parse(text);sent.push(data);if(data.setup)setTimeout(()=>this.onmessage?.({data:JSON.stringify({setupComplete:{}})}),0);}close(){this.readyState=3;}}
      window.WebSocket=FakeSocket;const control=module.create({invokeMethodAsync:async(name,epoch,state)=>states.push([name,state])});
      try{
        const supported=module.capabilities();const started=await control.start({token:'auth_tokens/test-only',webSocketUrl:'wss://generativelanguage.googleapis.com/ws/test',setup:{model:'models/test-only'},expiresAt:new Date(Date.now()+600000).toISOString()},1);
        const limit=performance.now()+4000;while(!sent.some(x=>x.realtimeInput?.audio)&&performance.now()<limit)await new Promise(r=>setTimeout(r,50));
        const pcm=btoa(String.fromCharCode(...new Uint8Array(2400)));peer.onmessage({data:JSON.stringify({serverContent:{modelTurn:{parts:[{inlineData:{data:pcm,mimeType:'audio/pcm;rate=24000'}}]},turnComplete:true}})});
        await new Promise(r=>setTimeout(r,250));
        window.propflowLiveProbe={control,peer,sent,states,original,supported,started,before:sent.filter(x=>x.realtimeInput?.audio).length};
        return started;
      }catch(error){control.dispose();window.WebSocket=original;throw error;}
      })()`);
      const otherTab=await send('Target.createTarget',{url:'about:blank'});
      await send('Target.activateTarget',{targetId:otherTab.targetId});await wait(700);
      live=await evaluate(`(async()=>{const p=window.propflowLiveProbe;try{
        const hidden=document.visibilityState==='hidden',active=p.peer.readyState===1,audioFrames=p.sent.filter(x=>x.realtimeInput?.audio).length;
        const continuedInBackground=hidden&&active&&audioFrames>p.before&&!p.states.some(x=>x[1]==='paused');
        p.peer.onmessage({data:JSON.stringify({toolCall:{functionCalls:[{id:'goodbye',name:'end_conversation'}]}})});
        await new Promise(r=>setTimeout(r,30));
        // Two seconds leaves room for the browser's one-second background timer batching.
        const pcm=btoa(String.fromCharCode(...new Uint8Array(24000)).repeat(4));
        p.peer.onmessage({data:JSON.stringify({serverContent:{outputTranscription:{text:'Tạm biệt bạn!'},modelTurn:{parts:[{inlineData:{data:pcm,mimeType:'audio/pcm;rate=24000'}}]},turnComplete:true}})});
        await new Promise(r=>setTimeout(r,20));
        const waitedForGoodbye=!p.states.some(x=>x[0]==='OnLiveEnded');
        const limit=performance.now()+4000;while(!p.states.some(x=>x[0]==='OnLiveEnded')&&performance.now()<limit)await new Promise(r=>setTimeout(r,50));
        const farewellCompleted=p.states.filter(x=>x[0]==='OnLiveEnded').length===1;
        return {passed:p.supported&&p.started&&continuedInBackground&&waitedForGoodbye&&farewellCompleted&&p.peer.readyState===3&&p.states.some(x=>x[1]==='speaking'),supported:p.supported,started:p.started,audioFrames,continuedInBackground,waitedForGoodbye,farewellCompleted,stopped:p.peer.readyState===3};
      }finally{p.control.dispose();window.WebSocket=p.original;delete window.propflowLiveProbe;}})()`);
      await send('Target.activateTarget',{targetId:target.targetId});
    }
    console.log(JSON.stringify({ passed: checks.every(x => x.passed)&&greeting.passed&&(!liveVoice||live.passed), checks, greeting, live }));
    if (checks.some(x => !x.passed)||!greeting.passed||(liveVoice&&!live.passed)) process.exitCode = 1;
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
    for (const entry of pending.values()) entry.reject(Error('Browser closed')); pending.clear();
    const resolved = path.resolve(profile);
    if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('propflow-facility-layout-')) fs.rmSync(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    console.log(JSON.stringify({ cleanupComplete: true, browserPid: browser?.pid, serverPort: port, debugPort }));
  }
})();
