// Real published Web -> real HTTPS Kestrel API -> owned PostgreSQL fixture.
// No fetch replacement, request interception, fake tokens or API doubles.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),readline=require('node:readline');
const {spawn,execFile}=require('node:child_process'),{once}=require('node:events'),assert=require('node:assert/strict');
const web=process.env.PROPFLOW_E2E_WEB,api=process.env.PROPFLOW_E2E_API,password=process.env.PROPFLOW_E2E_PASSWORD,out=process.env.PROPFLOW_E2E_ARTIFACTS;
for(const url of [web,api])assert.match(url||'',/^https:\/\/127\.0\.0\.1:\d+$/,'Only owned loopback HTTPS fixture endpoints allowed');
assert.ok(password&&out);
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'propflow-real-browser-'));
const chrome=[process.env.RESIDENT_TEST_BROWSER,'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p=>p&&fs.existsSync(p));
const wait=ms=>new Promise(r=>setTimeout(r,ms));
const replies=[];readline.createInterface({input:process.stdin}).on('line',line=>{const reply=replies.shift();if(reply)reply(JSON.parse(line));});
async function rpc(message){const result=new Promise(resolve=>replies.push(resolve));process.stdout.write(JSON.stringify(message)+'\n');assert.ok((await result).ok);}
let browser,socket,sequence=0,debugPort;const pending=new Map(),pages=[],requests=new Map(),loginRequests=new Map(),network=[],jsErrors=[],consoleErrors=[],checks=[],scenarios=[];
function send(method,params={},sessionId){return new Promise((resolve,reject)=>{const id=++sequence,t=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout: '+method));},25000);pending.set(id,{resolve:v=>{clearTimeout(t);resolve(v)},reject:e=>{clearTimeout(t);reject(e)}});socket.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
async function evaluate(p,expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true},p.session);if(r.exceptionDetails)throw Error('Browser evaluation failed ('+p.name+'): '+(r.exceptionDetails.exception?.description||r.exceptionDetails.text));return r.result.value;}
async function until(p,expression){for(let n=0;n<200;n++){if(await evaluate(p,expression))return;await wait(100);}await shot(p,'failure-'+p.name);throw Error('UI timeout ('+p.name+'): '+expression);}
async function shot(p,name){const s=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false},p.session);fs.writeFileSync(path.join(out,name+'.png'),Buffer.from(s.data,'base64'));}
async function navigate(p,route){await send('Page.navigate',{url:web+route},p.session);}
async function input(p,id,value){await evaluate(p,`(()=>{const e=document.getElementById(${JSON.stringify(id)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()`);}
async function click(p,label){await evaluate(p,`(()=>{const b=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)});if(!b||b.matches(':disabled'))throw Error('Button unavailable');b.focus();b.click();})()`);}
async function newPage(name){
 const context=await send('Target.createBrowserContext'),target=await send('Target.createTarget',{url:'about:blank',browserContextId:context.browserContextId});
 const attached=await send('Target.attachToTarget',{targetId:target.targetId,flatten:true});const p={name,context:context.browserContextId,target:target.targetId,session:attached.sessionId,token:null};pages.push(p);
 await send('Page.enable',{},p.session);await send('Runtime.enable',{},p.session);await send('Network.enable',{},p.session);return p;
}
async function login(p,role){
 await navigate(p,'/login');await until(p,"!!document.getElementById('username')");
 await input(p,'username','e2e_'+role.toLowerCase());await input(p,'password',password);
  await evaluate(p,"document.querySelector('.auth-login-form form button[type=submit]').click()");
 await until(p,"location.pathname!='/login'");
 for(let n=0;n<100&&!p.token;n++)await wait(100);assert.ok(p.token,'Real login did not return an access token');
 assert.ok(network.some(x=>x.page===p.name&&x.path==='/api/v1/auth/login'&&x.status===200),'Missing actual HTTPS login response');
 await rpc({action:'progress',phase:'real-login-'+role});
}
async function configure(p,email,phone){await input(p,'support-email',email);await input(p,'support-phone',phone);await click(p,'Lưu thay đổi');await until(p,"!!document.querySelector('dialog[open]')");}
async function save(p){await click(p,'Xác nhận lưu');await until(p,"!document.querySelector('dialog')");}
async function apiRequest(p,method,route,body,authenticated=true){
 // Ordinary network request to the real API; token captured from its actual login response.
 return evaluate(p,`(async()=>{const r=await fetch(${JSON.stringify(api+route)},{method:${JSON.stringify(method)},credentials:'include',headers:{'Content-Type':'application/json',${authenticated?`Authorization:${JSON.stringify('Bearer '+p.token)}`:''}},${body===undefined?'':`body:JSON.stringify(${JSON.stringify(body)}),`}});return {status:r.status,body:await r.json().catch(()=>null)};})()`);
}
const settings='/api/v1/administration/system-configuration/support-contacts',publicRoute='/api/v1/public/support-contacts';
(async()=>{
 try{
  assert.ok(chrome,'Chrome/Edge is required');
  browser=spawn(chrome,['--headless=new','--ignore-certificate-errors','--remote-debugging-address=127.0.0.1','--remote-debugging-port=0','--user-data-dir='+profile,'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','about:blank'],{windowsHide:true,stdio:'ignore'});
  const activeFile=path.join(profile,'DevToolsActivePort');for(let n=0;n<300&&!fs.existsSync(activeFile);n++)await wait(50);
  const active=fs.readFileSync(activeFile,'utf8').trim().split(/\r?\n/);debugPort=Number(active[0]);
  fs.writeFileSync(path.join(out,'browser-process.json'),JSON.stringify({pid:browser.pid,debugPort,ownedProfile:true}));
  socket=new WebSocket('ws://127.0.0.1:'+debugPort+active[1]);await once(socket,'open');
  socket.addEventListener('message',async event=>{
   const m=JSON.parse(event.data),callback=pending.get(m.id);if(callback){pending.delete(m.id);m.error?callback.reject(Error(m.error.message)):callback.resolve(m.result);return;}
   const page=pages.find(p=>p.session===m.sessionId);if(!page)return;
   if(m.method==='Runtime.exceptionThrown')jsErrors.push({page:page.name,message:m.params.exceptionDetails.text});
   if(m.method==='Runtime.consoleAPICalled'&&m.params.type==='error')consoleErrors.push({page:page.name,message:m.params.args.map(x=>typeof x.value==='string'?x.value:'[object]').join(' ')});
   if(m.method==='Network.requestWillBeSent')requests.set(m.sessionId+':'+m.params.requestId,m.params.request.method);
   if(m.method==='Network.responseReceived'){
    const r=m.params.response;if(r.url.startsWith(api+'/')){
     const pathname=new URL(r.url).pathname;network.push({page:page.name,method:requests.get(m.sessionId+':'+m.params.requestId),path:pathname,status:r.status});
     if(pathname==='/api/v1/auth/login'&&r.status===200)loginRequests.set(m.sessionId+':'+m.params.requestId,true);
    }
   }
   if(m.method==='Network.loadingFinished'&&loginRequests.has(m.sessionId+':'+m.params.requestId)){
    loginRequests.delete(m.sessionId+':'+m.params.requestId);
    try{const r=await send('Network.getResponseBody',{requestId:m.params.requestId},m.sessionId);page.token=JSON.parse(r.base64Encoded?Buffer.from(r.body,'base64').toString():r.body).accessToken;}catch{jsErrors.push({page:page.name,message:'Could not read real login result'});}
   }
  });
  const first=await newPage('admin-one');await login(first,'ADMIN');
  await navigate(first,'/admin/dashboard?tab=configuration');await until(first,"!!document.getElementById('support-email')");
  assert.ok(await evaluate(first,"document.getElementById('support-save').disabled"));
  let before=network.filter(x=>x.method==='PUT'&&x.path===settings).length;
  await input(first,'support-email','invalid');await input(first,'support-phone','123');await evaluate(first,"document.querySelector('.support-settings form').requestSubmit()");
  await until(first,"document.getElementById('support-email-error').textContent.includes('hợp lệ')");
  assert.equal(network.filter(x=>x.method==='PUT'&&x.path===settings).length,before);scenarios.push('real-ui-validation-no-request');
  await configure(first,'support.real@example.invalid','+84 (24) 1234-5678');
  assert.ok(await evaluate(first,"document.querySelector('dialog').textContent.includes('+842412345678')"));
  for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},first.session);
   await evaluate(first,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'})`);await wait(750);
   assert.ok(await evaluate(first,"(()=>{const d=document.querySelector('dialog'),r=d.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&d.contains(document.activeElement)})()"));
   await shot(first,'dialog-'+width+'-'+theme);checks.push({page:'dialog',width,theme});
  }
  await save(first);await until(first,"document.getElementById('support-phone').value==='+842412345678'");
  await rpc({action:'db',phase:'initial-ui-save',email:'support.real@example.invalid',phone:'+842412345678',audits:2});
  assert.ok(network.some(x=>x.page===first.name&&x.method==='PUT'&&x.path===settings&&x.status===200));scenarios.push('real-login-ui-save-postgresql-audit');
  for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},first.session);
   await evaluate(first,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'})`);await wait(750);
   assert.ok(await evaluate(first,'document.documentElement.scrollWidth<=innerWidth+1'));
   await evaluate(first,"document.getElementById('support-email').focus()");
   await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},first.session);await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},first.session);
   assert.equal(await evaluate(first,'document.activeElement.id'),'support-phone');
   await shot(first,'admin-'+width+'-'+theme);checks.push({page:'admin',width,theme,keyboard:true});
  }
  // Public reads and DOM rendering use real network responses, with no authenticated token required.
  const anonymous=await newPage('anonymous');
  for(const route of ['/contact','/help']){
   await navigate(anonymous,route);await until(anonymous,"!!document.querySelector('a[href=\"tel:+842412345678\"]')");
   assert.equal(await evaluate(anonymous,"document.querySelector('.support-contact-card a[href^=\"mailto:\"]').getAttribute('href')"),'mailto:support.real%40example.invalid');
   for(const width of [390,768,1440])for(const theme of ['light','dark']){
    await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},anonymous.session);
    await evaluate(anonymous,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'})`);await wait(750);
    assert.ok(await evaluate(anonymous,'document.documentElement.scrollWidth<=innerWidth+1'));
    await evaluate(anonymous,"document.querySelector('.support-contact-card').scrollIntoView({block:'center'})");
    await shot(anonymous,route.slice(1)+'-'+width+'-'+theme);checks.push({page:route,width,theme});
   }
  }
  const publicResponse=await apiRequest(anonymous,'GET',publicRoute,undefined,false);
  assert.equal(publicResponse.status,200);assert.deepEqual(Object.keys(publicResponse.body).sort(),['email','phone']);
  assert.equal((await apiRequest(anonymous,'GET',settings,undefined,false)).status,401);
  assert.equal((await apiRequest(anonymous,'PUT',settings,{email:'blocked@example.invalid',phone:'19001234',expectedVersion:'a'.repeat(64)},false)).status,401);scenarios.push('real-public-projection-anonymous-401');
  await navigate(first,'/admin/dashboard?tab=configuration');await until(first,"document.getElementById('support-email')?.value==='support.real@example.invalid'");scenarios.push('refresh-real-persistence');
  const second=await newPage('admin-two');await login(second,'ADMIN2');await navigate(second,'/admin/dashboard?tab=configuration');await until(second,"!!document.getElementById('support-email')");
  await configure(second,'stale@example.invalid','19002222');
  await configure(first,'new.real@example.invalid','19001111');await save(first);await until(first,"document.getElementById('support-email')?.value==='new.real@example.invalid'");
  await save(second);await until(second,"document.querySelector('.support-error')?.textContent.includes('phiên khác')");
  assert.ok(network.some(x=>x.page===second.name&&x.method==='PUT'&&x.path===settings&&x.status===409));
  assert.ok(await evaluate(second,"document.getElementById('support-save').matches(':disabled')"));
  await shot(second,'actual-concurrency-409');await click(second,'Tải lại cấu hình');await until(second,"document.getElementById('support-email')?.value==='new.real@example.invalid'");
  await rpc({action:'db',phase:'real-two-admin-conflict',email:'new.real@example.invalid',phone:'19001111',audits:4});scenarios.push('two-real-admin-ui-409');
  const current=(await apiRequest(first,'GET',settings)).body;
  assert.equal((await apiRequest(first,'PUT',settings,{email:'bad\n',phone:'not-a-phone',expectedVersion:current.version})).status,400);
  await rpc({action:'db',phase:'backend-invalid-no-write',email:'new.real@example.invalid',phone:'19001111',audits:4});scenarios.push('real-backend-validation-400');
  for(const role of ['MANAGER','STAFF','ACCOUNTANT','RESIDENT']){
   const p=await newPage(role.toLowerCase());await login(p,role);
   assert.equal((await apiRequest(p,'GET',settings)).status,403,role+' admin read');
   assert.equal((await apiRequest(p,'PUT',settings,{email:'denied@example.invalid',phone:'19003333',expectedVersion:current.version})).status,403,role+' admin write');
   await navigate(p,'/admin/dashboard?tab=configuration');await wait(1000);assert.equal(await evaluate(p,"!!document.getElementById('support-email')"),false);
   await navigate(p,'/contact');scenarios.push('real-'+role+'-403');
  }
  await rpc({action:'db',phase:'roles-no-write',email:'new.real@example.invalid',phone:'19001111',audits:4});
  // Leave all live authenticated shells before deliberately restarting both actual processes.
  await navigate(first,'/contact');await navigate(second,'/contact');await wait(500);
  await rpc({action:'restart'});
  await navigate(first,'/admin/dashboard?tab=configuration');await until(first,"document.getElementById('support-email')?.value==='new.real@example.invalid'");
  for(const route of ['/contact','/help']){await navigate(anonymous,route);await until(anonymous,"document.querySelector('.support-contact-card')?.textContent.includes('new.real@example.invalid')");}
  await rpc({action:'db',phase:'after-real-web-api-restart',email:'new.real@example.invalid',phone:'19001111',audits:4});scenarios.push('actual-web-api-restart-postgresql-persistence');
  // Same real UI supports clearing and restoring the public values.
  await configure(first,'','');await save(first);await until(first,"document.getElementById('support-save').disabled");
  for(const route of ['/contact','/help']){await navigate(anonymous,route);await until(anonymous,"document.querySelector('.support-contact-card')?.textContent.includes('Chưa công bố')");assert.equal(await evaluate(anonymous,"document.querySelectorAll('.support-contact-card a[href^=\"tel:\"],.support-contact-card a[href^=\"mailto:\"]').length"),0);}
  await rpc({action:'db',phase:'real-ui-unset',email:'',phone:'',audits:6});
  await configure(first,'new.real@example.invalid','19001111');await save(first);await until(first,"document.getElementById('support-email')?.value==='new.real@example.invalid'");
  await rpc({action:'db',phase:'real-ui-restored',email:'new.real@example.invalid',phone:'19001111',audits:8});scenarios.push('real-unset-and-restore-ui');
  await rpc({action:'permission'});
  const noPermission=await newPage('admin-without-permission');await login(noPermission,'ADMIN2');
  assert.equal((await apiRequest(noPermission,'GET',settings)).status,403);
  assert.equal((await apiRequest(noPermission,'PUT',settings,{email:'denied@example.invalid',phone:'19003333',expectedVersion:current.version})).status,403);
  await navigate(noPermission,'/admin/dashboard?tab=configuration');await until(noPermission,"document.body.textContent.includes('không có quyền quản lý cấu hình')");
  assert.equal(await evaluate(noPermission,"!!document.getElementById('support-email')"),false);scenarios.push('real-admin-fixed-permission-required');
  await rpc({action:'db',phase:'final-unchanged-after-denial',email:'new.real@example.invalid',phone:'19001111',audits:8});
  assert.deepEqual(jsErrors,[],'Unhandled JavaScript error in actual E2E');
  assert.deepEqual(consoleErrors,[],'Console error in actual E2E');
  fs.writeFileSync(path.join(out,'network-evidence.json'),JSON.stringify(network,null,2));
  await rpc({action:'complete',passed:true,scenarios,checks,jsErrors,consoleErrors,actualLogin:true,apiDoubles:false,requestInterception:false,actualRestart:true});
 }catch(error){fs.writeFileSync(path.join(out,'browser-failure.txt'),error.message);console.error(error.message);process.exitCode=1;}
 finally{
  fs.writeFileSync(path.join(out,'network-evidence.json'),JSON.stringify(network,null,2));
  fs.writeFileSync(path.join(out,'javascript-evidence.json'),JSON.stringify({jsErrors,consoleErrors},null,2));
  if(socket?.readyState===WebSocket.OPEN){try{await send('Browser.close')}catch{}socket.close();}
  if(browser&&browser.exitCode===null){await new Promise(resolve=>execFile('taskkill',['/PID',String(browser.pid),'/T','/F'],{windowsHide:true},resolve));if(browser.exitCode===null)await Promise.race([once(browser,'exit'),wait(5000)]);}
  if(browser)fs.writeFileSync(path.join(out,'browser-cleanup.json'),JSON.stringify({pid:browser.pid,stopped:browser.exitCode!==null,debugPort}));
  if(path.resolve(profile).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(profile).startsWith('propflow-real-browser-'))fs.rmSync(profile,{recursive:true,force:true,maxRetries:5,retryDelay:100});
  process.stdin.destroy();
 }
})();

