// Real WASM UI against explicit in-browser API doubles. PostgreSQL/RBAC are exercised separately by SupportContactsHttpTests.
// Uses only an owned temporary browser profile, free loopback ports and an owned Web host; no user DB or AWS.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),net=require('node:net');
const {spawn,execFile}=require('node:child_process'),{once}=require('node:events'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),profile=fs.mkdtempSync(path.join(os.tmpdir(),'propflow-support-test-'));
const configuration=process.env.PROPFLOW_TEST_CONFIGURATION||'Debug';
const browserPath=[process.env.RESIDENT_TEST_BROWSER,'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p=>p&&fs.existsSync(p));
const wait=ms=>new Promise(r=>setTimeout(r,ms));
async function freePort(){const s=net.createServer();s.listen(0,'127.0.0.1');await once(s,'listening');const p=s.address().port;await new Promise(r=>s.close(r));return p;}
let host,browser,socket,port,debugPort,sequence=0,hostOutput='';const pending=new Map();
function send(method,params={},sessionId){return new Promise((resolve,reject)=>{const id=++sequence,t=setTimeout(()=>{pending.delete(id);reject(Error('Timed out: '+method));},20000);pending.set(id,{resolve:v=>{clearTimeout(t);resolve(v)},reject:e=>{clearTimeout(t);reject(e)}});socket.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
async function stop(p){if(!p||p.exitCode!==null)return;if(process.platform==='win32')await new Promise(r=>execFile('taskkill',['/PID',String(p.pid),'/T','/F'],{windowsHide:true},r));else p.kill('SIGTERM');if(p.exitCode===null)await Promise.race([once(p,'exit'),wait(5000)]);}
const fakes=String.raw`(()=>{
 const original=window.fetch.bind(window),uid='083485b5-2dcc-45cb-bae4-92fec9b89625';
 const state=JSON.parse(sessionStorage.getItem('support-test-state')||'null')||{email:null,phone:null,version:'a'.repeat(64)};
 window.supportProbe={puts:0,writes:0,conflict:false,publicStatus:200,saveStatus:200,readStatus:200,network:false,delay:0};
 window.fetch=async(input,init)=>{
  const url=new URL(typeof input==='string'?input:input.url,location.href),p=url.pathname,probe=window.supportProbe;
  if(p==='/appsettings.json'||p.startsWith('/appsettings.'))return new Response(JSON.stringify({Api:{BaseUrl:'https://support-test.invalid/'}}),{headers:{'Content-Type':'application/json'}});
  if(!p.startsWith('/api/')&&!p.startsWith('/hubs/'))return original(input,init);
  let value,status=200;const method=init?.method||input.method||'GET';
  if(p.endsWith('/auth/csrf'))value={token:'test-csrf'};
  else if(p.endsWith('/auth/refresh'))value={accessToken:'test-only.invalid.signature',expiresAt:new Date(Date.now()+3600000).toISOString(),user:{id:uid,username:'support-test-admin',displayName:'Test Admin',role:'ADMIN',status:'ACTIVE',emailVerified:true,permissions:['MANAGE_SYSTEM_CONFIGURATION']}};
  else if(p.endsWith('/public/support-contacts')){
   if(probe.network)throw new TypeError('Isolated test network failure');
   status=probe.publicStatus;value=status===200?{email:state.email,phone:state.phone}:{title:'Không thể tải thông tin hỗ trợ.'};
  }else if(p.endsWith('/system-configuration/support-contacts')){
   if(method==='PUT'){
    probe.puts++;const body=JSON.parse(await new Response(init?.body||'{}').text());
    if(probe.delay)await new Promise(r=>setTimeout(r,probe.delay));
    if(probe.network)throw new TypeError('Isolated test network failure');
    if(probe.saveStatus!==200){status=probe.saveStatus;value={title:'Không thể lưu cấu hình. Vui lòng thử lại.'};}
    else if(probe.conflict||body.expectedVersion!==state.version){status=409;value={code:'support_contacts_changed',title:'Cấu hình đã thay đổi'};}
    else{state.email=body.email;state.phone=body.phone;state.version=crypto.randomUUID().replaceAll('-','').repeat(2);probe.writes++;sessionStorage.setItem('support-test-state',JSON.stringify(state));value=state;}
   }else{status=probe.readStatus;value=status===200?state:{title:'Không thể tải cấu hình hỗ trợ.'};}
  }else if(p.endsWith('/auth/logout'))value={};else{status=503;value={title:'Outside isolated UI test'};}
  return new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}});
 };
})();`;
(async()=>{
 try{
  assert.ok(browserPath,'No test browser found');port=await freePort();
  host=spawn('dotnet',['run','--no-build','--no-launch-profile','--project','src/PropFlow.Web/PropFlow.Web.csproj','--configuration',configuration,'--urls','http://127.0.0.1:'+port],{cwd:root,windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development'},stdio:['ignore','pipe','pipe']});
  host.stdout.on('data',x=>hostOutput+=x);host.stderr.on('data',x=>hostOutput+=x);console.log(JSON.stringify({phase:'host',hostPid:host.pid,port}));
  for(let i=0;i<300&&!hostOutput.includes('Now listening on:');i++){if(host.exitCode!==null)throw Error('Test host exited');await wait(100);}assert.ok(hostOutput.includes('Now listening on:'),'Web host not ready');
  browser=spawn(browserPath,['--headless=new','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+profile,'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','about:blank'],{windowsHide:true,stdio:'ignore'});
  const activeFile=path.join(profile,'DevToolsActivePort');for(let i=0;i<300&&!fs.existsSync(activeFile);i++)await wait(50);
  const active=fs.readFileSync(activeFile,'utf8').trim().split(/\r?\n/);debugPort=Number(active[0]);console.log(JSON.stringify({phase:'browser',browserPid:browser.pid,debugPort}));
  socket=new WebSocket('ws://127.0.0.1:'+debugPort+active[1]);await once(socket,'open');socket.addEventListener('message',event=>{const m=JSON.parse(event.data),cb=pending.get(m.id);if(cb){pending.delete(m.id);m.error?cb.reject(Error(m.error.message)):cb.resolve(m.result)}});
  const target=await send('Target.createTarget',{url:'about:blank'}),session=await send('Target.attachToTarget',{targetId:target.targetId,flatten:true}),id=session.sessionId;
  const errors=[];socket.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.sessionId===id&&m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text);});
  await send('Page.enable',{},id);await send('Runtime.enable',{},id);await send('Page.addScriptToEvaluateOnNewDocument',{source:fakes},id);
  async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true},id);if(r.exceptionDetails)throw Error(r.exceptionDetails.text+': '+r.exceptionDetails.exception?.description);return r.result.value;}
  async function until(expression){for(let i=0;i<160;i++){if(await evaluate(expression))return;await wait(100)}await screenshot('fe158-polish-failure');throw Error('UI condition failed: '+expression+'; '+await evaluate("document.querySelector('.support-settings')?.textContent||location.pathname"));}
  async function navigate(route){await send('Page.navigate',{url:'http://127.0.0.1:'+port+route},id);}
  const input=(name,value)=>evaluate("(()=>{const e=document.getElementById("+JSON.stringify(name)+");e.value="+JSON.stringify(value)+";e.dispatchEvent(new Event('input',{bubbles:true}));})()");
  const click=label=>evaluate("(()=>{const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==="+JSON.stringify(label)+");if(!b)throw Error('Missing button: '+"+JSON.stringify(label)+");b.focus();b.click();})()");
  async function key(key,code=key,extra={}){await send('Input.dispatchKeyEvent',{type:'keyDown',key,code,...extra},id);await send('Input.dispatchKeyEvent',{type:'keyUp',key,code,...extra},id);}
  async function confirm(){await click('Lưu thay đổi');await until("!!document.querySelector('dialog[open]')");await click('Xác nhận lưu');await until("!document.querySelector('dialog')");}
  const checks=[],scenarios=[];
  async function viewport(width,theme){await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},id);await evaluate("document.documentElement.dataset.theme="+JSON.stringify(theme)+";document.documentElement.classList.toggle('dark',"+(theme==='dark')+")");await wait(750);}
  async function screenshot(name){const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false},id);fs.writeFileSync(path.join(root,'artifacts',name+'.png'),Buffer.from(shot.data,'base64'));}
  await navigate('/admin/dashboard?tab=configuration');await until("!!document.getElementById('support-email')");
  assert.equal(await evaluate("document.querySelector('.admin-nav a[aria-label=\"Cấu hình hệ thống\"]').getAttribute('aria-current')"),'page');
  assert.ok(await evaluate("document.getElementById('support-save').disabled"));
  await evaluate("document.querySelector('.support-settings form').requestSubmit()");await wait(100);
  assert.equal(await evaluate("supportProbe.puts"),0);assert.equal(await evaluate("!!document.querySelector('dialog')"),false);scenarios.push('unchanged-no-request');
  assert.ok(await evaluate("document.getElementById('support-email').labels.length===1&&document.getElementById('support-phone').labels.length===1"));
  await input('support-email','bad-email');await click('Lưu thay đổi');
  await until("document.getElementById('support-email-error').textContent.includes('hợp lệ')");assert.equal(await evaluate("supportProbe.puts"),0);
  await input('support-email','support@example.invalid');await input('support-phone','javascript:12345678');await click('Lưu thay đổi');
  await until("document.getElementById('support-phone-error').textContent.includes('8 đến 15')");assert.equal(await evaluate("supportProbe.puts"),0);scenarios.push('invalid-input');
  await input('support-phone','1900 1234');
  console.log(JSON.stringify({phase:'keyboard-confirm',saveEnabled:await evaluate("!document.getElementById('support-save').disabled")}));
  await evaluate("document.getElementById('support-phone').focus()");
  await key('Enter','Enter',{windowsVirtualKeyCode:13,text:'\r',unmodifiedText:'\r'});
  await until("!!document.querySelector('dialog[open]')");
  assert.ok(await evaluate("document.querySelector('dialog').contains(document.activeElement)"));
  assert.ok(await evaluate("document.querySelector('.support-preview').textContent.includes('Chưa công bố')&&document.querySelector('.support-preview').textContent.includes('19001234')"));
  for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await viewport(width,theme);
   assert.ok(await evaluate("(()=>{const r=document.querySelector('dialog').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1})()"));
   await screenshot('fe158-polish-dialog-'+width+'-'+theme);
   for(let i=0;i<5;i++){await key('Tab','Tab',{windowsVirtualKeyCode:9});assert.ok(await evaluate("document.querySelector('dialog').contains(document.activeElement)"));}
   await key('Tab','Tab',{windowsVirtualKeyCode:9,modifiers:8});assert.ok(await evaluate("document.querySelector('dialog').contains(document.activeElement)"));
   checks.push({page:'dialog',width,theme,keyboard:true});
  }
  await key('Escape','Escape',{windowsVirtualKeyCode:27});await until("!document.querySelector('dialog')");
  assert.equal(await evaluate("document.activeElement.id"),'support-phone');
  assert.equal(await evaluate("document.getElementById('support-email').value"),'support@example.invalid');assert.equal(await evaluate('supportProbe.puts'),0);scenarios.push('enter-escape-focus-cancel');
  // Slow response proves repeated confirm cannot start a second request.
  await evaluate('supportProbe.delay=700');await click('Lưu thay đổi');await until("!!document.querySelector('dialog[open]')");
  await evaluate("(()=>{const b=document.querySelector('.pf-dialog__primary');b.click();b.click();b.click();})()");
  await until('supportProbe.puts===1');
  assert.ok(await evaluate("document.getElementById('support-save').disabled&&document.querySelector('.pf-dialog__primary').disabled"));
  await until("!document.querySelector('dialog')");
  assert.equal(await evaluate('supportProbe.puts'),1);assert.equal(await evaluate('supportProbe.writes'),1);
  await until("document.activeElement.id==='support-title'");
  assert.equal(await evaluate("document.getElementById('support-phone').value"),'19001234');assert.ok(await evaluate("document.getElementById('support-save').disabled"));scenarios.push('save-double-submit');
  await input('support-phone','1900 1234');assert.ok(await evaluate("document.getElementById('support-save').disabled"));scenarios.push('normalized-no-op');
  await input('support-email','draft@example.invalid');await click('Hủy thay đổi');assert.equal(await evaluate("document.getElementById('support-email').value"),'support@example.invalid');
  // Form, validation, shared dialog and shell at all required viewports/themes.
  for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await viewport(width,theme);await wait(100);
   assert.ok(await evaluate('document.documentElement.scrollWidth<=innerWidth+1'));
   assert.ok(await evaluate("(()=>{const input=document.getElementById('support-email'),s=getComputedStyle(input),panel=getComputedStyle(document.querySelector('.support-panel'));return s.backgroundColor===panel.backgroundColor&&s.color!==s.backgroundColor&&parseFloat(s.borderRadius)>0})()"),'Input must match the themed panel and have a visible border radius');
   await evaluate("document.getElementById('support-email').focus()");await key('Tab','Tab',{windowsVirtualKeyCode:9});
   assert.equal(await evaluate('document.activeElement.id'),'support-phone');
   await screenshot('fe158-polish-admin-'+width+'-'+theme);checks.push({page:'admin',width,theme,keyboard:true});
  }
  for(const failure of [503,'network',403,401]){
   await navigate('/admin/dashboard?tab=configuration');await until("!!document.getElementById('support-email')");
   await input('support-email','unsaved@example.invalid');
   await evaluate(failure==='network'?'supportProbe.network=true':'supportProbe.saveStatus='+failure);await confirm();
   if(failure===401){
    await until("location.pathname==='/login'||!!document.querySelector('.support-error')");
    assert.equal(await evaluate('supportProbe.writes'),0);
    assert.ok(await evaluate("location.pathname==='/login'||document.getElementById('support-save').matches(':disabled')"));
    scenarios.push('expired-session');continue;
   }
   await until("!!document.querySelector('.support-error')");
   assert.equal(await evaluate('supportProbe.writes'),0);
   assert.equal(await evaluate("document.getElementById('support-email').value"),'unsaved@example.invalid');
   assert.equal(await evaluate("document.querySelector('.support-error').textContent.includes('thành công')"),false);
   if(failure===401||failure===403)assert.ok(await evaluate("document.getElementById('support-save').matches(':disabled')"));
   else assert.ok(await evaluate("!document.getElementById('support-save').matches(':disabled')"));
   scenarios.push('save-failure-'+failure);
  }
  await navigate('/admin/dashboard?tab=configuration');await until("!!document.getElementById('support-email')");
  await evaluate('supportProbe.conflict=true');await input('support-email','conflict@example.invalid');await confirm();
  await until("document.querySelector('.support-error')?.textContent.includes('phiên khác')");
  assert.ok(await evaluate("document.getElementById('support-save').matches(':disabled')"));
  await evaluate('supportProbe.conflict=false');await click('Tải lại cấu hình');await until("document.getElementById('support-email').value==='support@example.invalid'");scenarios.push('409-reload');
  await evaluate('supportProbe.readStatus=503');await click('Tải lại cấu hình');await until("!!document.querySelector('.support-error')");
  assert.ok(await evaluate("document.getElementById('support-save').matches(':disabled')"));
  await evaluate('supportProbe.readStatus=200');await click('Tải lại cấu hình');await until("!document.querySelector('.support-error')");scenarios.push('read-failure-retry');
  // Refresh re-reads the saved backend result (the test double persists only in the owned profile).
  await navigate('/admin/dashboard?tab=configuration');await until("document.getElementById('support-email')?.value==='support@example.invalid'");scenarios.push('refresh');
  for(const route of ['/contact','/help']){
   await navigate(route);await until("!!document.querySelector('a[href=\"tel:19001234\"]')");
   assert.equal(await evaluate("document.querySelector('a[href^=\"mailto:\"]').getAttribute('href')"),'mailto:support%40example.invalid');
   for(const width of [390,768,1440])for(const theme of ['light','dark']){
    await viewport(width,theme);assert.ok(await evaluate('document.documentElement.scrollWidth<=innerWidth+1'));
    await screenshot('fe158-polish-'+route.slice(1)+'-'+width+'-'+theme);checks.push({page:route,width,theme});
   }
  }
  await navigate('/admin/dashboard?tab=configuration');await until("!!document.getElementById('support-email')");
  await input('support-email','');await input('support-phone','');await confirm();assert.equal(await evaluate('supportProbe.writes'),1);
  for(const route of ['/contact','/help']){
   await navigate(route);await until("document.querySelector('.support-contact-card')?.textContent.includes('Chưa công bố')");
   assert.equal(await evaluate("document.querySelectorAll('.support-contact-card a[href^=\"mailto:\"],.support-contact-card a[href^=\"tel:\"]').length"),0);
   await evaluate('supportProbe.publicStatus=503');
   // The API is re-read on page load or retry, never equating a failure with unset.
   await evaluate("sessionStorage.setItem('support-test-public-fail','1')");
   await send('Page.addScriptToEvaluateOnNewDocument',{source:"if(sessionStorage.getItem('support-test-public-fail'))supportProbe.publicStatus=503"},id);
   await navigate(route);await until("document.querySelector('.support-contact-card')?.textContent.includes('Chưa thể tải')");
   assert.equal(await evaluate("document.querySelector('.support-contact-card').textContent.includes('Chưa công bố')"),false);
   await evaluate("sessionStorage.removeItem('support-test-public-fail');supportProbe.publicStatus=200");await click('Thử lại');
   await until("document.querySelector('.support-contact-card')?.textContent.includes('Chưa công bố')");
  }
  scenarios.push('public-populated-unset-error-retry');
  // No unhandled JavaScript errors: includes the dialog close() lifecycle.
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({passed:true,scenarios,checks,unhandledJavaScriptErrors:errors.length,apiDoubles:true,realDatabaseUsed:false}));
 }catch(e){console.error(e.message);process.exitCode=1;}
 finally{
  if(socket?.readyState===WebSocket.OPEN){try{await send('Browser.close')}catch{}socket.close()}
  await stop(browser);await stop(host);
  for(const cb of pending.values())cb.reject(Error('Browser closed'));pending.clear();
  const resolved=path.resolve(profile);if(resolved.startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(resolved).startsWith('propflow-support-test-'))fs.rmSync(resolved,{recursive:true,force:true,maxRetries:5,retryDelay:100});
  console.log(JSON.stringify({cleanupComplete:true,hostPid:host?.pid,browserPid:browser?.pid,serverPort:port,debugPort}));
 }
})();

