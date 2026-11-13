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
const css = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/Features/AiChatbot/Components/ResidentChatWidget.razor.css'), 'utf8');
const moduleSource = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/chat-widget.js'), 'utf8');
const html = `<!doctype html><html lang="vi"><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:Arial}*{box-sizing:border-box}${css}</style></head><body><div class="rag-widget"><button class="rag-launch" aria-label="Mở Hỏi PropFlow"><span class="rag-launch-icon">✦</span><span class="rag-launch-label">Hỏi PropFlow</span></button></div><script type="module">import { attach } from '/chat-widget.js'; const widget=document.querySelector('.rag-widget'); window.dragHandle=attach(widget); window.openCount=0; widget.querySelector('button').addEventListener('click',()=>{window.openCount++; if(widget.querySelector('.rag-window')) {widget.querySelector('.rag-window').remove();return;} const panel=document.createElement('section');panel.className='rag-window';panel.innerHTML='<header><h2>Hỏi PropFlow</h2><button>Đóng</button></header><div class="rag-thread">Nội dung trò chuyện</div>';panel.querySelector('button').onclick=()=>panel.remove();widget.prepend(panel);});window.ready=true;</script></body></html>`;
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(p => p && fs.existsSync(p));
if (!browserPath) throw new Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-resident-layout-'));
const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', req.url === '/chat-widget.js' ? 'text/javascript' : 'text/html; charset=utf-8');
  res.end(req.url === '/chat-widget.js' ? moduleSource : html);
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

    const evaluate = async expression => {
      const result = await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true},session);
      if(result.exceptionDetails) throw new Error(result.exceptionDetails.text); return result.result.value;
    };
    for(let i=0;i<100 && !await evaluate('window.ready === true');i++) await wait(50);
    const assert = (condition, message) => { if(!condition) throw new Error(message); };
    const waitForLabel = async () => {
      for(let i=0;i<30;i++) {
        if(await evaluate('getComputedStyle(document.querySelector(".rag-launch-label")).opacity === "1"')) return;
        await wait(50);
      }
      throw new Error('Hover label did not become visible.');
    };
    const bounds = () => evaluate('(()=>{const r=document.querySelector(".rag-launch").getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};})()');
    const iconCenter = () => evaluate('(()=>{const r=document.querySelector(".rag-launch-icon").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()');
    const panelFits = () => evaluate('(()=>{const r=document.querySelector(".rag-window")?.getBoundingClientRect();return !!r && r.x>=11 && r.y>=11 && r.right<=innerWidth-11 && r.bottom<=innerHeight-11;})()');
    const click = async (x,y) => { await send('Input.dispatchMouseEvent',{type:'mouseMoved',x,y},session); await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x,y},session);await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x,y},session);await wait(250); };
    const results=[];
    for(const width of [1366,390,320]) {
      await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false},session);
      await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0},session); await wait(300);
      const r=await bounds(); assert(Math.abs(r.width-56)<1 && Math.abs(r.height-56)<1,'Launcher must be a circle at '+width);
      const beforeHover=await iconCenter();
      await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:r.x+28,y:r.y+28},session);await wait(300);
      await waitForLabel();
      assert(await evaluate('getComputedStyle(document.querySelector(".rag-launch-label")).opacity === "1"'),
        'Hover label missing: '+JSON.stringify(await evaluate('({width:innerWidth,hover:document.querySelector(".rag-launch").matches(":hover"),button:document.querySelector(".rag-launch").getBoundingClientRect().toJSON(),opacity:getComputedStyle(document.querySelector(".rag-launch-label")).opacity})')));
      const afterHover=await iconCenter();
      assert(Math.abs(afterHover.x-beforeHover.x)<1 && Math.abs(afterHover.y-beforeHover.y)<1,
        'Chatbot icon moved away on hover: '+JSON.stringify({width,beforeHover,afterHover}));
      assert(await evaluate('(()=>{const r=document.querySelector(".rag-launch-label").getBoundingClientRect();return r.left>=11 && r.right<=innerWidth-11;})()'), 'Hover label extends beyond viewport');
      for(const offset of [-18,18,0,-12,12]) {
        await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:beforeHover.x+offset,y:beforeHover.y},session);
        await wait(50);
        const center=await iconCenter();
        assert(Math.abs(center.x-beforeHover.x)<1 && Math.abs(center.y-beforeHover.y)<1,'Moving the pointer made the chatbot move');
      }
      const hovered=await bounds();await click(hovered.x+28,hovered.y+28);assert(await panelFits(),'Opened panel outside viewport');
      await evaluate('document.querySelector(".rag-window").remove()'); await wait(50);
      const beforeCount=await evaluate('window.openCount');
      const dragStart=await bounds(); const x=dragStart.x+28,y=dragStart.y+28; const destX=dragStart.y<300 ? width-40 : 40, destY=dragStart.y<300 ? 600 : 150;
      await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x,y},session);
      await send('Input.dispatchMouseEvent',{type:'mouseMoved',button:'left',buttons:1,x:destX,y:destY},session);
      await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:destX,y:destY},session);await wait(400);
      const dragged=await bounds();assert(Math.abs(dragged.y-dragStart.y)>100 && dragged.x>=11,'Launcher did not move within bounds');
      assert(await evaluate('window.openCount')===beforeCount,'Dragging accidentally opened chat');
      await click(dragged.x+28,dragged.y+28);assert(await panelFits(),'Panel outside viewport after dragging');
      await evaluate('document.querySelector(".rag-window").remove()'); await wait(50);
      results.push({width,circle:true,hoverLabel:true,stationaryOnHover:true,hoverLabelFits:true,drag:true,clickAfterDragSuppressed:true,panelFits:true});
    }
    await evaluate('window.dragHandle.dispose()');
    console.log(JSON.stringify({passed:true,checks:results}));
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
