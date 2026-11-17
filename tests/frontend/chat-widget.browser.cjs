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
const css = ['src/PropFlow.Web/wwwroot/app.css','src/PropFlow.Web.Client/Features/AiChatbot/Components/ResidentChatWidget.razor.css','src/PropFlow.Web.Client/wwwroot/css/chat-composer.css'].map(file=>fs.readFileSync(path.join(root,file),'utf8')).join('\n');
const previewFile=path.join(os.tmpdir(),'propflow-document-chat-welcome.html');
const previewHtml=fs.existsSync(previewFile)?fs.readFileSync(previewFile,'utf8'):'';
const moduleSource = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/chat-widget.js'), 'utf8');
const shortcutsSource = fs.readFileSync(path.join(root, 'src/PropFlow.Web.Client/wwwroot/js/composer-shortcuts.js'), 'utf8');
if(!fs.readFileSync(path.join(root,'src/PropFlow.Web/Components/App.razor'),'utf8').includes('src="js/composer-shortcuts.js?v='))
  throw Error('The app does not load composer shortcuts.');
const composerFiles = [
  ['Features/Communication/SupportChat/SupportChatWorkspace.razor', 'support-message'],
  ['Features/AiChatbot/Components/ResidentChatWidget.razor', 'rag-question'],
  ['Features/Resident/Portal/Pages/ResidentFacilities.razor', 'facility-need'],
  ['Features/Resident/Portal/Components/ResidentServiceRequestDialog.razor', 'service-description']
];
for(const [file,id] of composerFiles) {
  if(!new RegExp('<textarea[^>]*id="'+id+'"[^>]*data-enter-submit').test(fs.readFileSync(path.join(root,'src/PropFlow.Web.Client',file),'utf8')))
    throw Error('Composer is not wired to shortcuts: '+id);
}
const html = `<!doctype html><html lang="vi"><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:Arial}*{box-sizing:border-box}${css}</style></head><body><div class="rag-widget"><button class="rag-launch" aria-label="Mở Hỏi PropFlow"><span class="rag-launch-icon">✦</span><span class="rag-launch-label">Hỏi PropFlow</span></button></div><script type="module">
import '/composer-shortcuts.js'; import {attach} from '/chat-widget.js';
const template=document.createElement('template');template.innerHTML=await (await fetch('/preview')).text();
const widget=document.querySelector('.rag-widget');window.dragHandle=attach(widget);window.openCount=0;
widget.querySelector('button').addEventListener('click',()=>{
 window.openCount++;if(widget.querySelector('.rag-window')){widget.querySelector('.rag-window').remove();return;}
 const panel=template.content.querySelector('.rag-window')?.cloneNode(true)??document.createElement('section');panel.classList.add('rag-window');
 if(!panel.childElementCount)panel.innerHTML='<header><h2>Hỏi PropFlow</h2><button>Đóng</button></header><div class="rag-thread">Nội dung trò chuyện</div>';
 panel.querySelector('header button').onclick=()=>panel.remove();panel.querySelector('form')?.addEventListener('submit',event=>event.preventDefault());widget.prepend(panel);
});window.ready=true;</script></body></html>`;
const browserPath = [process.env.RESIDENT_TEST_BROWSER, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/usr/bin/chromium', '/usr/bin/google-chrome'].find(p => p && fs.existsSync(p));
if (!browserPath) throw new Error('Set RESIDENT_TEST_BROWSER to a Chromium executable.');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'propflow-resident-layout-'));
const server = http.createServer((req, res) => {
  const script = req.url === '/chat-widget.js' ? moduleSource : req.url === '/composer-shortcuts.js' ? shortcutsSource : null;
  res.setHeader('Content-Type', script !== null ? 'text/javascript' : 'text/html; charset=utf-8');
  res.end(script ?? (req.url==='/preview'?previewHtml:html));
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
      if(result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text); return result.result.value;
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
      if(previewHtml)for(const dark of [false,true]){
        await evaluate(`document.documentElement.classList.toggle('dark',${dark});document.documentElement.dataset.theme='${dark?'dark':'light'}'`);
        const composer=await evaluate(`(()=>{const panel=document.querySelector('.rag-window').getBoundingClientRect(),row=document.querySelector('.pf-chat-composer').getBoundingClientRect(),input=document.querySelector('#rag-question'),button=document.querySelector('.pf-chat-send').getBoundingClientRect();const before=input.getBoundingClientRect().height;input.value='Dòng một\\nDòng hai\\nDòng ba\\nDòng bốn';input.dispatchEvent(new Event('input',{bubbles:true}));const after=input.getBoundingClientRect().height;input.value='';return {fits:row.left>=panel.left&&row.right<=panel.right,buttonInside:button.left>=row.left&&button.right<=row.right,round:getComputedStyle(document.querySelector('.pf-chat-send')).borderRadius==='50%',noResize:getComputedStyle(input).resize==='none',grows:after>before&&after<=120,background:getComputedStyle(document.querySelector('.rag-window')).backgroundColor};})()`);
        assert(composer.fits&&composer.buttonInside&&composer.round&&composer.noResize&&composer.grows,'Composer layout failed: '+JSON.stringify({width,dark,composer}));
        const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},session);fs.writeFileSync(path.join(os.tmpdir(),`propflow-document-chat-${width}-${dark?'dark':'light'}.png`),Buffer.from(shot.data,'base64'));
        results.push({width,dark,composer});
      }
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
    await evaluate(`window.setupComposer=(id,marked=true)=>{
      document.querySelector('#keyboard-fixture')?.remove();
      const form=document.createElement('form');form.id='keyboard-fixture';
      const input=document.createElement('textarea');input.id=id;if(marked)input.setAttribute('data-enter-submit','');input.maxLength=100;
      const button=document.createElement('button');button.type='submit';button.id='keyboard-send';button.textContent='Gửi';
      form.append(input,button);document.body.append(form);window.sent=[];window.draft='';
      input.addEventListener('input',()=>window.draft=input.value);
      form.addEventListener('submit',event=>{event.preventDefault();window.sent.push({text:input.value,submitter:event.submitter?.id});});
      input.value='Xin chào';input.focus();input.setSelectionRange(input.value.length,input.value.length);
    }`);
    const key = async (modifiers=0,repeat=false) => {
      await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,nativeVirtualKeyCode:13,text:'\r',unmodifiedText:'\r',modifiers,autoRepeat:repeat},session);
      await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,nativeVirtualKeyCode:13,modifiers},session);
    };
    for(const [,id] of composerFiles) {
      await evaluate('setupComposer('+JSON.stringify(id)+')');await key();
      assert(await evaluate(`sent.length===1&&sent[0].text==='Xin chào'&&sent[0].submitter==='keyboard-send'`),'Enter did not submit once: '+id);
      await evaluate('setupComposer('+JSON.stringify(id)+')');await key(2);
      assert(await evaluate(`sent.length===0&&draft==='Xin chào\\n'&&document.querySelector('#keyboard-fixture textarea').value===draft`),'Ctrl+Enter did not update the bound draft: '+id);
      await key();assert(await evaluate(`sent.length===1&&sent[0].text==='Xin chào\\n'`),'Enter lost multiline content: '+id);
      results.push({composer:id,enterSends:true,ctrlEnterAddsBoundNewline:true});
    }
    await evaluate(`setupComposer('selection');var input=document.querySelector('#selection');input.value='abcd';input.setSelectionRange(1,3)`);await key(2);
    assert(await evaluate(`draft==='a\\nd'&&document.querySelector('#selection').selectionStart===2`),'Ctrl+Enter did not replace the selected text');
    await evaluate(`setupComposer('readonly');document.querySelector('#readonly').readOnly=true`);await key();await key(2);
    assert(await evaluate(`sent.length===0&&document.querySelector('#readonly').value==='Xin chào'`),'Readonly composer changed or submitted');
    await evaluate(`setupComposer('busy');document.querySelector('#keyboard-send').disabled=true`);await key();
    assert(await evaluate('sent.length===0'),'Disabled send button was bypassed');
    await evaluate(`setupComposer('blank');document.querySelector('#blank').value='  '`);await key();
    assert(await evaluate('sent.length===0'),'Whitespace was submitted');
    await evaluate(`setupComposer('validation');var required=document.createElement('input');required.required=true;document.querySelector('#keyboard-fixture').append(required)`);await key();
    assert(await evaluate('sent.length===0'),'Enter bypassed normal form validation');
    await evaluate(`setupComposer('repeat')`);await key(0,true);assert(await evaluate('sent.length===0'),'Holding Enter submitted a duplicate');
    assert(await evaluate(`(()=>{setupComposer('ime');var input=document.querySelector('#ime');for(const options of [{isComposing:true},{keyCode:229}]){const event=new KeyboardEvent('keydown',{key:'Enter',bubbles:true,cancelable:true,...options});input.dispatchEvent(event);if(event.defaultPrevented)return false;}return sent.length===0;})()`),'Vietnamese/IME confirmation was intercepted');
    await evaluate(`setupComposer('max');var input=document.querySelector('#max');input.maxLength=input.value.length`);await key(2);
    assert(await evaluate(`document.querySelector('#max').value==='Xin chào'`),'Ctrl+Enter bypassed maxlength');
    await evaluate(`setupComposer('ordinary',false)`);await key();
    assert(await evaluate(`sent.length===0&&document.querySelector('#ordinary').value==='Xin chào\\n'`),'Ordinary textareas were affected');
    console.log(JSON.stringify({passed:true,checks:results,keyboardGuards:['selection','readonly','busy','blank','validation','repeat','IME','maxlength','unmarked']}));
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
