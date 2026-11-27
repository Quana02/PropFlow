const directoryVisualQa=require('./directory-filter-visual-qa.cjs');
// Real published Web -> real HTTPS Kestrel API -> owned PostgreSQL fixture.
// No fetch replacement, request interception, fake tokens or API doubles.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),readline=require('node:readline');
const {spawn,execFile}=require('node:child_process'),{once}=require('node:events'),assert=require('node:assert/strict');
const web=process.env.PROPFLOW_E2E_WEB,api=process.env.PROPFLOW_E2E_API,password=process.env.PROPFLOW_E2E_PASSWORD,out=process.env.PROPFLOW_E2E_ARTIFACTS;
for(const url of [web,api])assert.match(url||'',/^https:\/\/127\.0\.0\.1:\d+$/,'Only owned loopback HTTPS fixture endpoints allowed');
assert.ok(password&&out);
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'propflow-excel-browser-'));
const chrome=[process.env.RESIDENT_TEST_BROWSER,'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p=>p&&fs.existsSync(p));
const wait=ms=>new Promise(r=>setTimeout(r,ms));
const replies=[];readline.createInterface({input:process.stdin}).on('line',line=>{const reply=replies.shift();if(reply)reply(JSON.parse(line));});
async function rpc(message){const result=new Promise(resolve=>replies.push(resolve));process.stdout.write(JSON.stringify(message)+'\n');assert.ok((await result).ok);}
let browser,socket,sequence=0,debugPort;const downloads=[];const pending=new Map(),pages=[],requests=new Map(),assetPaths=new Map(),assetFailures=[],loginRequests=new Map(),network=[],jsErrors=[],consoleErrors=[],checks=[],scenarios=[];
function send(method,params={},sessionId){return new Promise((resolve,reject)=>{const id=++sequence,t=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout: '+method));},25000);pending.set(id,{resolve:v=>{clearTimeout(t);resolve(v)},reject:e=>{clearTimeout(t);reject(e)}});socket.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
async function evaluate(p,expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true},p.session);if(r.exceptionDetails)throw Error('Browser evaluation failed ('+p.name+'): '+(r.exceptionDetails.exception?.description||r.exceptionDetails.text));return r.result.value;}
async function until(p,expression,attempts=200){for(let n=0;n<attempts;n++){if(await evaluate(p,expression))return;await wait(100);}await shot(p,'failure-'+p.name);throw Error('UI timeout ('+p.name+'): '+expression);}
async function shot(p,name,clip){const options={format:'png',captureBeyondViewport:false};if(clip){const m=await send('Page.getLayoutMetrics',{},p.session),v=m.cssVisualViewport??m.visualViewport;options.clip={x:clip.left+v.pageX,y:clip.top+v.pageY,width:clip.width,height:clip.height,scale:1};}const s=await send('Page.captureScreenshot',options,p.session);fs.writeFileSync(path.join(out,name+'.png'),Buffer.from(s.data,'base64'));}
async function navigate(p,route){await send('Page.navigate',{url:web+route},p.session);}
async function input(p,id,value){await evaluate(p,`(()=>{const e=document.getElementById(${JSON.stringify(id)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()`);}
async function click(p,label){await evaluate(p,`(()=>{const b=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)});if(!b||b.matches(':disabled'))throw Error('Button unavailable');b.focus();b.click();})()`);}
async function newPage(name){
 const context=await send('Target.createBrowserContext'),target=await send('Target.createTarget',{url:'about:blank',browserContextId:context.browserContextId});
 const attached=await send('Target.attachToTarget',{targetId:target.targetId,flatten:true});const p={name,context:context.browserContextId,target:target.targetId,session:attached.sessionId,token:null};pages.push(p);
 await send('Browser.setDownloadBehavior',{behavior:'allowAndName',downloadPath:out,browserContextId:p.context,eventsEnabled:true});await send('Page.enable',{},p.session);await send('Runtime.enable',{},p.session);await send('Network.enable',{},p.session);await send('Page.bringToFront',{},p.session);return p;
}
async function login(p,role){
 await navigate(p,'/login');await until(p,"!!document.getElementById('username')",600);
 await input(p,'username','e2e_'+role.toLowerCase());await input(p,'password',password);
  await evaluate(p,"document.querySelector('.auth-login-form form button[type=submit]').click()");
 await until(p,"location.pathname!='/login'");
 for(let n=0;n<100&&!p.token;n++)await wait(100);assert.ok(p.token,'Real login did not return an access token');
 assert.ok(network.some(x=>x.page===p.name&&x.path==='/api/v1/auth/login'&&x.status===200),'Missing actual HTTPS login response');
 await rpc({action:'progress',phase:'real-login-'+role});
}
async function apiRequest(p,method,route,body,authenticated=true){
 // Ordinary network request to the real API; token captured from its actual login response.
 return evaluate(p,`(async()=>{const headers={'Content-Type':'application/json',${authenticated?`Authorization:${JSON.stringify('Bearer '+p.token)}`:''}};if(!['GET','HEAD'].includes(${JSON.stringify(method)})){const c=await fetch(${JSON.stringify(api+'/api/v1/auth/csrf')},{credentials:'include',headers});headers['X-CSRF-TOKEN']=(await c.json()).token;}const r=await fetch(${JSON.stringify(api+route)},{method:${JSON.stringify(method)},credentials:'include',headers,${body===undefined?'':`body:JSON.stringify(${JSON.stringify(body)}),`}});return {status:r.status,body:await r.json().catch(()=>null)};})()`);
}

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
   if(m.method==='Browser.downloadWillBegin'){downloads.push({guid:m.params.guid,name:m.params.suggestedFilename,state:'started'});return;}
   if(m.method==='Browser.downloadProgress'){const d=downloads.find(x=>x.guid===m.params.guid);if(d)d.state=m.params.state;return;}
   const page=pages.find(p=>p.session===m.sessionId);if(!page)return;
   if(m.method==='Runtime.exceptionThrown')jsErrors.push({page:page.name,message:m.params.exceptionDetails.text});
   if(m.method==='Runtime.consoleAPICalled'&&m.params.type==='error')consoleErrors.push({page:page.name,message:m.params.args.map(x=>typeof x.value==='string'?x.value:'[object]').join(' ')});
   if(m.method==='Network.requestWillBeSent'){requests.set(m.sessionId+':'+m.params.requestId,m.params.request.method);if(m.params.request.url.startsWith(web+'/'))assetPaths.set(m.sessionId+':'+m.params.requestId,new URL(m.params.request.url).pathname);}
   if(m.method==='Network.loadingFailed'&&assetPaths.has(m.sessionId+':'+m.params.requestId))assetFailures.push({page:page.name,path:assetPaths.get(m.sessionId+':'+m.params.requestId),error:m.params.errorText,canceled:m.params.canceled??false});
   if(m.method==='Network.responseReceived'){
    const r=m.params.response;if(r.url.startsWith(api+'/')){
     const parsed=new URL(r.url),pathname=parsed.pathname;network.push({page:page.name,method:requests.get(m.sessionId+':'+m.params.requestId),path:pathname,query:parsed.search,status:r.status});
     if(pathname==='/api/v1/auth/login'&&r.status===200)loginRequests.set(m.sessionId+':'+m.params.requestId,true);
    }
   }
   if(m.method==='Network.loadingFinished'&&loginRequests.has(m.sessionId+':'+m.params.requestId)){
    loginRequests.delete(m.sessionId+':'+m.params.requestId);
    try{const r=await send('Network.getResponseBody',{requestId:m.params.requestId},m.sessionId);page.token=JSON.parse(r.base64Encoded?Buffer.from(r.body,'base64').toString():r.body).accessToken;}catch{jsErrors.push({page:page.name,message:'Could not read real login result'});}
   }
  });

  async function download(p,label,prefix){
   const previous=downloads.length;await click(p,label);
   for(let n=0;n<300;n++){
    const d=downloads.slice(previous).find(x=>x.name.startsWith(prefix)&&x.state==='completed');
    if(d&&fs.existsSync(path.join(out,d.guid))){
     const file=path.join(out,downloads.length+'-'+path.basename(d.name.replaceAll('\\','/')));
     fs.renameSync(path.join(out,d.guid),file);return file;
    }
    await wait(100);
   }
   throw Error('Actual browser download missing: '+prefix);
  }
  async function upload(p,file){
   const dom=await send('DOM.getDocument',{},p.session),node=await send('DOM.querySelector',{nodeId:dom.root.nodeId,selector:'#apartment-excel-file'},p.session);
   assert.ok(node.nodeId);await send('DOM.setFileInputFiles',{nodeId:node.nodeId,files:[file]},p.session);
  }
  async function dropFile(p,file){
   const point=await evaluate(p,"(()=>{const e=document.getElementById('apartment-excel-file');e.scrollIntoView({block:'center',behavior:'instant'});const r=e.getBoundingClientRect();e.focus();return {x:r.left+r.width/2,y:r.top+r.height/2}})()");
   assert.ok(await evaluate(p,"document.activeElement.id==='apartment-excel-file'"),'File picker is not keyboard focusable');
   const data={items:[],files:[file],dragOperationsMask:1};
   await send('Input.dispatchDragEvent',{type:'dragEnter',...point,data},p.session);
   await until(p,"document.querySelector('.excel-dropzone').classList.contains('is-dragging')");
   await send('Input.dispatchDragEvent',{type:'dragOver',...point,data},p.session);
   await send('Input.dispatchDragEvent',{type:'drop',...point,data},p.session);
  }
  async function search(p,selector,text,expected){
   // Start interactions after the first real directory response is visible.
   await until(p,"/\\/\\s*[1-9]\\d*/.test(document.querySelector('.result-count')?.textContent||'')");
   await evaluate(p,"(()=>{const e=document.querySelector("+JSON.stringify(selector)+");e.value="+JSON.stringify(text)+";e.dispatchEvent(new Event('input',{bubbles:true}));})()");
   await wait(100);
   await evaluate(p,"document.querySelector("+JSON.stringify(selector)+").dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',bubbles:true}))");
   await until(p,"document.querySelector('.result-count')?.textContent.includes("+JSON.stringify(expected)+")");
  }
  async function filters(p){await until(p,"!!document.querySelector('.pf-filter-trigger:not(:disabled)')&&document.querySelector('.pf-filter-trigger').getAttribute('aria-disabled')!=='true'");await evaluate(p,"document.querySelector('.pf-filter-trigger').click()");await until(p,"!!document.querySelector('.pf-filter-popover:popover-open .pf-filter-controls')");}
  async function field(p,selector,value){await evaluate(p,"(()=>{const e=document.querySelector("+JSON.stringify(selector)+");e.value="+JSON.stringify(value)+";e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()");await wait(80);}
  async function key(p,key,code,modifiers=0){await send('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,windowsVirtualKeyCode:code,modifiers},p.session);await send('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:code,modifiers},p.session);await wait(100);}
  async function multi(p,aria,labels){
   const trigger='button[aria-label='+JSON.stringify(aria)+']',menu='[role=listbox][aria-label='+JSON.stringify(aria)+']';
   await until(p,'!!document.querySelector('+JSON.stringify(trigger)+')&&!document.querySelector('+JSON.stringify(trigger)+').disabled');
   await evaluate(p,'document.querySelector('+JSON.stringify(trigger)+').click()');await until(p,'!!document.querySelector('+JSON.stringify(menu)+')');
   await evaluate(p,"(()=>{const m=document.querySelector("+JSON.stringify(menu)+");[...m.querySelectorAll('.menu-actions button')].find(x=>x.textContent==='Xóa chọn')?.click()})()");await wait(100);
   for(const label of labels){await evaluate(p,"(()=>{const m=document.querySelector("+JSON.stringify(menu)+");const b=[...m.querySelectorAll('[role=option]')].find(x=>x.lastElementChild.textContent.trim()==="+JSON.stringify(label)+");if(!b)throw Error('Missing multi option');b.click()})()");await wait(100);}
   assert.equal(await evaluate(p,`document.querySelector(${JSON.stringify(menu)}).querySelectorAll('[role=option][aria-selected="true"]').length`),labels.length,aria+' selected values: '+JSON.stringify(await evaluate(p,`[...document.querySelector(${JSON.stringify(menu)}).querySelectorAll('[role=option]')].map(x=>({label:x.textContent,selected:x.getAttribute('aria-selected')}))`)));
   await evaluate(p,'document.querySelector('+JSON.stringify(trigger)+').click()');await until(p,"!document.querySelector('[role=listbox]:popover-open')");
  }
  async function option(p,aria,label){
   const trigger='button[aria-label='+JSON.stringify(aria)+']',menu='[role=listbox][aria-label='+JSON.stringify(aria)+']';
   if(await evaluate(p,`!!document.querySelector(${JSON.stringify(trigger)})?.closest('.pf-filter-multi')`))return multi(p,aria,[label]);
   await until(p,'!!document.querySelector('+JSON.stringify(trigger)+')&&!document.querySelector('+JSON.stringify(trigger)+').disabled');
   await evaluate(p,'document.querySelector('+JSON.stringify(trigger)+').click()');await until(p,'!!document.querySelector('+JSON.stringify(menu)+')');
   await evaluate(p,"(()=>{const m=document.querySelector("+JSON.stringify(menu)+");const b=[...m.querySelectorAll('[role=option]')].find(x=>x.textContent.trim().replace(/check$/,'').trim()==="+JSON.stringify(label)+");if(!b)throw Error('Missing filter option');b.click()})()");await until(p,"!document.querySelector('[role=listbox]:popover-open')");
  }
  async function apply(p,kind,count,changed=true){
   const path='/api/v1/'+kind,previous=network.filter(x=>x.page===p.name&&x.path===path).length;
   await evaluate(p,"document.querySelector('.pf-filter-popover:popover-open .pf-filter-apply').click()");
   await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");
   if(changed){for(let n=0;n<100&&network.filter(x=>x.page===p.name&&x.path===path).length===previous;n++)await wait(100);assert.ok(network.filter(x=>x.page===p.name&&x.path===path).length>previous,'Filter did not reach the actual API');}
   await until(p,"!document.querySelector('."+(kind==='apartments'?'apartment':'resident')+"-results').classList.contains('is-loading')&&document.querySelector('.result-count')?.textContent.includes("+JSON.stringify('/ '+count+' '+(kind==='apartments'?'căn hộ':'cư dân'))+")");
   assert.ok(await evaluate(p,"!document.querySelector('.resident-notice.error,.apartment-page .notice[role=alert]')"),'Directory returned an error');
  }
  async function reset(p,kind,count){await filters(p);await evaluate(p,"[...document.querySelector('.pf-filter-popover:popover-open').querySelectorAll('footer button')].find(x=>x.textContent.trim()==='Đặt lại').click()");await wait(80);await apply(p,kind,count);}
  async function filterViews(p,kind){
   for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},p.session);
   await evaluate(p,"document.documentElement.dataset.theme="+JSON.stringify(theme)+";document.documentElement.classList.toggle('dark',"+(theme==='dark')+");document.querySelector('.pf-filter-trigger').scrollIntoView({block:'center',behavior:'instant'})");await filters(p);await wait(250);
   assert.ok(await evaluate(p,"(()=>{const e=document.querySelector('.pf-filter-popover:popover-open'),r=e.getBoundingClientRect(),b=e.querySelector('.pf-filter-apply').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&b.bottom<=innerHeight+1&&e.scrollWidth<=e.clientWidth+1})()"),'Filter popover overflow');
   await shot(p,kind+'-filters-'+width+'-'+theme);checks.push({page:kind+'-filters',width,theme});await evaluate(p,"document.querySelector('.pf-filter-popover:popover-open .pf-filter-close').click()");await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");
  }
   await directoryVisualQa.verifyDirectory({p,kind,send,evaluate,until,wait,shot,filters,key,checks,network,out,fs});scenarios.push('unified-'+kind+'-layout-scroll-menus-keyboard-and-viewport-matrix');
  }
  async function residentDrawerChecks(p){
   await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
   await filters(p);await wait(200);
   assert.ok(await evaluate(p,"(()=>{const d=document.querySelector('.pf-filter-popover:popover-open');return d.getBoundingClientRect().width>=400&&d.getBoundingClientRect().width<=420&&[...d.querySelectorAll('.pf-select__trigger,footer button')].every(x=>Math.abs(x.getBoundingClientRect().height-44)<1)})()"),'Inconsistent drawer sizing');
   const requestsBefore=network.filter(x=>x.page===p.name&&x.path==='/api/v1/residents').length;
   await multi(p,'Trạng thái cư dân',['Đã chuyển đi']);
   assert.equal(network.filter(x=>x.page===p.name&&x.path==='/api/v1/residents').length,requestsBefore,'Draft selection auto applied');
   await key(p,'Escape',27);await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");assert.ok(await evaluate(p,"document.activeElement===document.querySelector('.pf-filter-trigger')"),'Close did not return focus');
   await filters(p);assert.ok(await evaluate(p,"document.querySelector('button[aria-label=\"Trạng thái cư dân\"]').textContent.includes('2 mục đã chọn')"),'Canceled draft persisted');
   await evaluate(p,"document.querySelector('button[aria-label=\"Trạng thái cư dân\"]').focus();document.activeElement.click()");await wait(100);
   await evaluate(p,"[...document.querySelectorAll('.pf-filter-multi-menu .menu-actions button')].find(x=>x.textContent==='Chọn tất cả').click()");await wait(100);
   assert.equal(await evaluate(p,"document.querySelectorAll('.pf-filter-multi-menu [aria-selected=true]').length"),3);
   await evaluate(p,"[...document.querySelectorAll('.pf-filter-multi-menu .menu-actions button')].find(x=>x.textContent==='Xóa chọn').click()");await wait(100);
   assert.equal(await evaluate(p,"document.querySelectorAll('.pf-filter-multi-menu [aria-selected=true]').length"),0);
   await key(p,'Escape',27);await key(p,'Escape',27);await filters(p);
   // The single-select account menu also consumes Escape before the outer drawer.
   await evaluate(p,"document.querySelector('button[aria-label=\"Liên kết tài khoản\"]').focus();document.activeElement.click()");await wait(150);
   await key(p,'Escape',27);assert.ok(await evaluate(p,"!!document.querySelector('.pf-filter-popover:popover-open')&&!document.querySelector('[role=listbox]:popover-open')"),'Single-select Escape dismissed drawer');
   await evaluate(p,"document.querySelector('.pf-filter-apply').focus()");await key(p,'Tab',9);assert.ok(await evaluate(p,"document.activeElement.classList.contains('pf-filter-close')"),'Tab escaped drawer');
   await key(p,'Tab',9,8);assert.ok(await evaluate(p,"document.activeElement.classList.contains('pf-filter-apply')"),'Reverse Tab escaped drawer');
   await evaluate(p,"document.querySelector('button[aria-label=\"Trạng thái cư dân\"]').focus();document.activeElement.click()");await wait(100);await key(p,'ArrowDown',40);await key(p,' ',32);await key(p,'Escape',27);assert.ok(await evaluate(p,"!!document.querySelector('.pf-filter-popover:popover-open')&&!document.querySelector('[role=listbox]:popover-open')"),'Nested Escape dismissed drawer');
   await evaluate(p,"document.querySelector('button[aria-label=\"Căn hộ\"]').focus();document.activeElement.click()");await wait(150);
   await field(p,'.pf-filter-multi-menu input[type=search]','OTHER-E2E');assert.equal(await evaluate(p,"document.querySelectorAll('.pf-filter-multi-menu [role=option]').length"),1);await key(p,'Escape',27);
   await key(p,'Escape',27);await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");
   // Compact viewport and a CSS-pixel viewport equivalent to desktop at 200% zoom.
   for(const [width,height,label] of [[390,420,'short-mobile'],[720,450,'zoom-200-equivalent']]){
    await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<600},p.session);await filters(p);await wait(150);
    const positions=await evaluate(p,"(()=>{const d=document.querySelector('.pf-filter-popover:popover-open'),b=d.querySelector('.pf-filter-fields'),h=d.querySelector('header').getBoundingClientRect(),f=d.querySelector('footer').getBoundingClientRect();b.scrollTop=b.scrollHeight;return {header:h.top,footer:f.top}})()");await wait(150);
    assert.ok(await evaluate(p,"(()=>{const d=document.querySelector('.pf-filter-popover:popover-open'),f=d.querySelector('footer').getBoundingClientRect();return f.bottom<=innerHeight&&Math.abs(f.top-"+positions.footer+")<1&&Math.abs(d.querySelector('header').getBoundingClientRect().top-"+positions.header+")<1&&document.documentElement.scrollWidth<=innerWidth})()"),'Scrolling moved footer or overflowed viewport');
    await evaluate(p,"document.querySelector('button[aria-label=\"Quan hệ với căn hộ\"]').focus();document.activeElement.click()");await wait(150);
    assert.ok(await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open'),r=m.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight})()"),'Multi-select menu clipped');
    await shot(p,'resident-drawer-'+label);await key(p,'Escape',27);await key(p,'Escape',27);await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");
   }
   await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
   scenarios.push('resident-drawer-draft-cancel-keyboard-focus-trap-nested-escape-search-and-short-viewport');
  }
  async function picture(p,page,width,theme){
   await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},p.session);
   await evaluate(p,"document.documentElement.dataset.theme="+JSON.stringify(theme)+";document.documentElement.classList.toggle('dark',"+(theme==='dark')+")");await wait(750);
   if(page!=='dialog'){await evaluate(p,"[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Xuất Excel')?.scrollIntoView({block:'center',behavior:'instant'})");await wait(100);}
   assert.ok(await evaluate(p,'document.documentElement.scrollWidth<=innerWidth+1'),'Horizontal page overflow');
   if(page!=='dialog')assert.ok(await evaluate(p,"[...document.querySelectorAll('button')].filter(x=>['Nhập Excel','Xuất Excel'].includes(x.textContent.trim())).every(x=>{const r=x.getBoundingClientRect();return r.width>0&&r.left>=0&&r.right<=innerWidth+1})"),'Excel toolbar controls are clipped');
   if(page!=='dialog')assert.ok(await evaluate(p,"(()=>{const h=document.querySelector('."+(page==='apartments'?'header-actions':'resident-header-actions')+"');const labels=[...h.querySelectorAll('button,a')].map(x=>x.textContent.trim());const x=labels.indexOf('Xuất Excel');return x>=0&&x<labels.findIndex(t=>t.includes("+JSON.stringify(page==='apartments'?'Loại căn hộ':'Tài khoản cư dân')+"))&&!document.querySelector('.pf-list-toolbar .excel-export')})()"),'Excel actions are not before the existing header actions');
   await shot(p,page+'-'+width+'-'+theme);checks.push({page,width,theme});
  }
  const staff=await newPage('staff');await login(staff,'STAFF');await directoryVisualQa.verifyDefaultCallers({p:staff,navigate,until,evaluate,filters,key,shot,checks,send});scenarios.push('staff-assets-default-filter-variant-regression');
  await send('Target.disposeBrowserContext',{browserContextId:staff.context});
  const manager=await newPage('manager');await login(manager,'MANAGER');
  const managerOnly=process.env.PROPFLOW_MANAGER_FILTERS_ONLY==='1';
  if(!managerOnly){
  await send('Emulation.setDeviceMetricsOverride',{width:1100,height:900,deviceScaleFactor:1,mobile:false},manager.session);
  await navigate(manager,'/apartments');await until(manager,"!![...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Nhập Excel'&&!x.disabled)");
  await until(manager,"[...document.querySelectorAll('.apartment-panel tbody tr')].some(x=>x.textContent.includes('OTHER-E2E'))");
  assert.ok(await evaluate(manager,"(()=>{const cells=[...document.querySelectorAll('.apartment-panel tbody tr')].find(x=>x.textContent.includes('OTHER-E2E')).querySelectorAll('td');return cells[2].textContent.includes('Không thuộc bộ lọc')&&cells[3].textContent.includes('Không thuộc bộ lọc')&&cells[3].textContent.includes('OTHER-PERSON')})()"),'Owner and current resident must appear in separate cells');
  scenarios.push('separate-owner-and-current-resident-columns');
  await click(manager,'Nhập Excel');await until(manager,"!!document.querySelector('dialog[open] .excel-import')");
  await shot(manager,'dropzone-empty');
  const template=await download(manager,'Tải file mẫu Excel','PropFlow_MauNhapCanHo');
  await rpc({action:'prepare',file:template});scenarios.push('real-template-download-xlsx-library-input');
  await rpc({action:'inspect',file:path.join(out,'text-format-probe.xlsx'),kind:'security'});scenarios.push('library-verified-leading-zeros-long-phone-date-and-formula-prefixes');
  await evaluate(manager,"(()=>{window.__excelBusyStates=new Set();new MutationObserver(()=>{const e=document.querySelector('.excel-import');if(e)window.__excelBusyStates.add((e.getAttribute('aria-busy')||'').toLowerCase())}).observe(document.body,{subtree:true,attributes:true,attributeFilter:['aria-busy']});})()");
  for(const [file,message] of [['input-empty.xlsx','Chọn file'],['input-broken.xlsx','File'],['input-oversized.xlsx','Chọn file']]){
   await upload(manager,path.join(out,file));await until(manager,"document.querySelector('.excel-error')?.textContent.includes("+JSON.stringify(message)+")");
   assert.ok(await evaluate(manager,"document.querySelector('.pf-dialog__primary').disabled"));
  }
  await rpc({action:'db',phase:'empty-broken-oversized-ui-no-write',count:0});scenarios.push('real-upload-client-and-server-errors-no-write');
  await upload(manager,path.join(out,'input-invalid.xlsx'));
  await until(manager,"document.querySelector('.excel-summary')?.textContent.includes('Trùng 2')");
  assert.ok(await evaluate(manager,"document.querySelector('.pf-dialog__primary').disabled"));
  assert.equal(await evaluate(manager,"document.querySelectorAll('.excel-preview tbody tr').length"),2);
  assert.ok(await evaluate(manager,"document.querySelector('.excel-preview').textContent.includes('Diện tích')"));
  await rpc({action:'db',phase:'invalid-preview',count:0});
  await shot(manager,'actual-preview-errors');
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27},manager.session);
  await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27},manager.session);
  await until(manager,"!document.querySelector('dialog')");await rpc({action:'db',phase:'cancel-no-write',count:0});scenarios.push('invalid-preview-error-table-cancel-no-write');
  await click(manager,'Nhập Excel');await until(manager,"!!document.querySelector('dialog[open]')");
  await evaluate(manager,"document.querySelector('.pf-dialog__secondary').focus()");
  assert.ok(await evaluate(manager,"document.activeElement.matches('.pf-dialog__secondary')"));
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',text:'\r',unmodifiedText:'\r',windowsVirtualKeyCode:13},manager.session);await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13},manager.session);
  await until(manager,"!document.querySelector('dialog')");scenarios.push('keyboard-enter-cancel-no-write');
  await click(manager,'Nhập Excel');await until(manager,"!!document.querySelector('dialog[open]')");
  await dropFile(manager,path.join(out,'input-valid.xlsx'));
  await until(manager,"document.querySelector('.excel-summary')?.textContent.includes('Hợp lệ 25')");
  assert.ok(await evaluate(manager,"document.querySelector('.excel-selected-file').textContent.includes('input-valid.xlsx')"));
  assert.ok(await evaluate(manager,"!document.querySelector('.excel-dropzone').classList.contains('is-dragging')"));
  scenarios.push('native-file-drop-preview-selected-name-and-keyboard-focus');
  assert.ok(await evaluate(manager,"!document.querySelector('.pf-dialog__primary').disabled"));
  await rpc({action:'db',phase:'valid-preview-no-write',count:0});
  assert.ok(await evaluate(manager,"window.__excelBusyStates.has('true')"),'Upload loading state was never rendered');
  for(const width of [390,768,1440])for(const theme of ['light','dark']){
   await picture(manager,'dialog',width,theme);
   assert.ok(await evaluate(manager,"(()=>{const d=document.querySelector('dialog'),r=d.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1&&d.contains(document.activeElement)})()"));
   for(let i=0;i<8;i++){await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},manager.session);await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},manager.session);assert.ok(await evaluate(manager,"document.querySelector('dialog').contains(document.activeElement)"));}
  }
  const confirmPoint=await evaluate(manager,"(()=>{const b=document.querySelector('.pf-dialog__primary');b.scrollIntoView({block:'nearest'});const r=b.getBoundingClientRect();if(r.top<0||r.bottom>innerHeight||r.left<0||r.right>innerWidth)throw Error('Confirm button cannot be reached');return {x:r.left+r.width/2,y:r.top+r.height/2};})()");
  await shot(manager,'dialog-confirm-controls-visible');
  for(let n=0;n<2;n++){
   await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...confirmPoint},manager.session);
   await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...confirmPoint},manager.session);
  }
  await until(manager,"!document.querySelector('dialog')");
  await until(manager,"document.querySelector('.result-count')?.textContent.includes('/ 26 căn hộ')");
  await rpc({action:'db',phase:'after-ui-import',count:25});scenarios.push('real-ui-create-25-postgresql-audit-no-account-or-relationship-writes');
  assert.equal(network.filter(x=>x.method==='POST'&&x.path==='/api/v1/apartments/excel/commit'&&x.status===200).length,1);
  await search(manager,'.apartment-search input','XLS-E2E-','/ 25 căn hộ');
  await filters(manager);
  for(const aria of ['Trạng thái quản lý','Tình trạng sở hữu','Tình trạng cư trú'])assert.equal(await evaluate(manager,'document.querySelector('+JSON.stringify('button[aria-label='+JSON.stringify(aria)+']')+').querySelector(".pf-select__value,.selection-label").textContent.trim()'),'Tất cả');
  await field(manager,'.pf-filter-controls input[aria-label="Diện tích từ"]','90');await field(manager,'.pf-filter-controls input[aria-label="Diện tích đến"]','65');
  await evaluate(manager,"document.querySelector('.pf-filter-popover:popover-open .pf-filter-apply').click()");await until(manager,"!!document.querySelector('.pf-filter-popover:popover-open .filter-error')");
  await field(manager,'.pf-filter-controls input[aria-label="Diện tích từ"]','');await field(manager,'.pf-filter-controls input[aria-label="Diện tích đến"]','');await option(manager,'Tình trạng sở hữu','Có chủ sở hữu');await apply(manager,'apartments',0);
  await filters(manager);await option(manager,'Tình trạng sở hữu','Chưa có chủ sở hữu');await apply(manager,'apartments',25);
  await filters(manager);await field(manager,'.pf-filter-controls input[aria-label="Diện tích từ"]','75,51');await apply(manager,'apartments',0);
  await reset(manager,'apartments',25);
  const beforeCancelCount=network.filter(x=>x.page==='manager'&&x.path==='/api/v1/apartments').length;
  await filters(manager);await option(manager,'Tầng căn hộ','Tầng 1');await evaluate(manager,"document.querySelector('.pf-filter-popover:popover-open .pf-filter-close').click()");await wait(150);
  assert.equal(network.filter(x=>x.page==='manager'&&x.path==='/api/v1/apartments').length,beforeCancelCount,'Canceled filter must not issue a list request');
  assert.ok(await evaluate(manager,"document.querySelector('.result-count').textContent.includes('/ 25 căn hộ')"));
  await filters(manager);await multi(manager,'Tầng căn hộ',['Tầng 1','Tầng 2']);await field(manager,'.pf-filter-controls input[aria-label="Diện tích từ"]','75,5');await field(manager,'.pf-filter-controls input[aria-label="Diện tích đến"]','75.5');
  await multi(manager,'Trạng thái quản lý',['Ngừng quản lý','Đang quản lý']);await multi(manager,'Loại căn hộ',['Loại Excel E2E phụ','Loại Excel E2E']);await option(manager,'Tình trạng cư trú','Trống');await option(manager,'Tình trạng sở hữu','Chưa có chủ sở hữu');await apply(manager,'apartments',25);
  assert.equal(await evaluate(manager,"document.querySelector('.pf-filter-trigger strong').textContent.trim()"),'7');
  const appliedApartment=new URLSearchParams(network.filter(x=>x.page==='manager'&&x.path==='/api/v1/apartments').at(-1).query);
  for(const [key,value] of Object.entries({minArea:'75.5',maxArea:'75.5',hasOwner:'false',occupancy:'VACANT'}))assert.equal(appliedApartment.get(key)?.toLowerCase(),value.toLowerCase());
  for(const key of ['statuses','floorNumbers','apartmentUnitTypeIds'])assert.equal(appliedApartment.getAll(key).length,2,'List lost a multi-select value');
  assert.deepEqual(appliedApartment.getAll('statuses'),['INACTIVE','ACTIVE']);assert.deepEqual(appliedApartment.getAll('floorNumbers'),['1','2']);scenarios.push('apartment-multi-select-OR-AND-all-fields-validation-cancel-reset-and-empty-results');
  await filterViews(manager,'apartments');await directoryVisualQa.verifyPagination({p:manager,kind:'apartments',evaluate,until,network});scenarios.push('apartment-ui-pagination-retains-all-applied-filters');
  const apartmentList=await apiRequest(manager,'GET','/api/v1/apartments?search=XLS-E2E-&pageSize=100');
  assert.equal(apartmentList.status,200);
  assert.deepEqual(apartmentList.body.items.map(x=>[x.unitNumber,x.floorNumber,x.unitType,x.usableAreaM2,(x.owners.length?x.owners.map(r=>r.fullName).join('\n'):'Chưa xác định'),(x.currentResidents.length?x.currentResidents.map(r=>r.fullName+' · '+r.residentCode).join('\n'):'Căn hộ trống'),'Đang quản lý',x.isOccupied?'Đang có cư dân':'Căn hộ trống']),JSON.parse(fs.readFileSync(path.join(out,'expected-apartments.json'),'utf8')));
  for(const width of [390,768,1440])for(const theme of ['light','dark'])await picture(manager,'apartments',width,theme);
  await field(manager,'.apartment-search input','not-submitted-search');
  const apartments=await download(manager,'Xuất Excel','PropFlow_DanhSachCanHo_');
  await field(manager,'.apartment-search input','XLS-E2E-');scenarios.push('export-uses-applied-search-not-unsubmitted-search-text');
  assert.ok(network.some(x=>x.page===manager.name&&x.path==='/api/v1/apartments/excel/export'&&['statuses','floorNumbers','apartmentUnitTypeIds'].every(k=>new URLSearchParams(x.query).getAll(k).length===2)),'Apartment Excel lost a multi-select value');
  await rpc({action:'inspect',file:apartments,kind:'apartments'});scenarios.push('filtered-apartment-export-xlsx-library-read-25-rows');
  await rpc({action:'api-offline'});
  const beforeFailureDownloads=downloads.length;await click(manager,'Xuất Excel');
  await until(manager,"document.body.textContent.includes('Không kết nối được máy chủ')");
  assert.equal(downloads.length,beforeFailureDownloads);await shot(manager,'real-export-network-error');
  await rpc({action:'api-online'});
  const recovered=await download(manager,'Xuất Excel','PropFlow_DanhSachCanHo_');await rpc({action:'inspect',file:recovered,kind:'apartments'});
  scenarios.push('real-owned-api-outage-download-error-and-successful-retry');
  await navigate(manager,'/apartments');await until(manager,"!!document.querySelector('.apartment-search input')");
  await search(manager,'.apartment-search input','XLS-E2E-','/ 25 căn hộ');
  await rpc({action:'db',phase:'after-refresh',count:25});scenarios.push('refresh-preserves-imported-list');
  await navigate(manager,'/residents');await until(manager,"!!document.querySelector('.resident-page .pf-list-search input')");
  assert.equal(await evaluate(manager,"[...document.querySelectorAll('button')].some(x=>x.textContent.trim()==='Nhập Excel')"),false);
  await search(manager,'.resident-page .pf-list-search input','Excel Nguyễn','/ 101 cư dân');
  await search(manager,'.resident-page .pf-list-search input','Không thuộc','/ 1 cư dân');
  await filters(manager);
  await multi(manager,'Trạng thái cư dân',['Đang hoạt động','Ngừng hoạt động']);
  await multi(manager,'Căn hộ',['OTHER-E2E · Tầng 1','XLS-E2E-0001 · Tầng 2']);
  await multi(manager,'Tầng cư dân',['Tầng 1','Tầng 2']);
  await multi(manager,'Quan hệ với căn hộ',['Có sở hữu căn hộ','Đang cư trú']);
  await apply(manager,'residents',1);
  assert.ok(network.some(x=>x.page===manager.name&&x.path==='/api/v1/residents'&&['statuses','apartmentUnitIds','floorNumbers','relationshipKinds'].every(k=>new URLSearchParams(x.query).getAll(k).length===2)),'List lost a multi-select field');
  const related=await download(manager,'Xuất Excel','PropFlow_DanhSachCuDan_');await rpc({action:'inspect',file:related,kind:'related'});
  assert.ok(network.some(x=>x.page===manager.name&&x.path==='/api/v1/residents/excel/export'&&['statuses','apartmentUnitIds','floorNumbers','relationshipKinds'].every(k=>new URLSearchParams(x.query).getAll(k).length===2)),'Excel lost a multi-select field');
  scenarios.push('four-multi-select-fields-real-query-and-one-row-excel-OR-AND-no-duplicates');
  await reset(manager,'residents',1);
  await filters(manager);await option(manager,'Tầng cư dân','Tầng 1');await option(manager,'Quan hệ với căn hộ','Vừa sở hữu vừa cư trú');await option(manager,'Hình thức cư trú','Chủ sở hữu đang cư trú');await option(manager,'Vai trò trong hộ','Chủ hộ');await option(manager,'Liên kết tài khoản','Chưa liên kết tài khoản');await apply(manager,'residents',1);
  await filters(manager);await option(manager,'Tầng cư dân','Tầng 2');await apply(manager,'residents',0);
  await reset(manager,'residents',1);await filters(manager);await option(manager,'Liên kết tài khoản','Đã liên kết tài khoản');await apply(manager,'residents',0);await reset(manager,'residents',1);
  await search(manager,'.resident-page .pf-list-search input','Excel Nguyễn','/ 101 cư dân');
  await filters(manager);await option(manager,'Quan hệ với căn hộ','Không có quan hệ hiện hành');await option(manager,'Liên kết tài khoản','Chưa liên kết tài khoản');await multi(manager,'Trạng thái cư dân',['Đang hoạt động','Ngừng hoạt động']);await apply(manager,'residents',101);
  assert.equal(await evaluate(manager,"document.querySelector('.pf-filter-trigger strong').textContent.trim()"),'3');
  // A user can apply with the multi-select menu still open; its top layer
  // must leave the footer clickable and dispose without interop races.
  await filters(manager);await evaluate(manager,"document.querySelector('button[aria-label=\"Trạng thái cư dân\"]').focus();document.activeElement.click()");
  await until(manager,"!!document.querySelector('.pf-filter-multi-menu:popover-open')");
  await evaluate(manager,"[...document.querySelectorAll('.pf-filter-multi-menu [role=option]')].find(x=>x.lastElementChild.textContent.trim()==='Đã chuyển đi').click()");await wait(100);
  const applyPoint=await evaluate(manager,"(()=>{const r=document.querySelector('.pf-filter-apply').getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()");
  await send('Input.dispatchMouseEvent',{type:'mousePressed',...applyPoint,button:'left',clickCount:1},manager.session);
  await send('Input.dispatchMouseEvent',{type:'mouseReleased',...applyPoint,button:'left',clickCount:1},manager.session);
  await until(manager,"!document.querySelector('.pf-filter-popover:popover-open')&&document.querySelector('.result-count').textContent.includes('/ 101 cư dân')");
  for(let i=0;i<100&&!network.some(x=>x.page===manager.name&&x.path==='/api/v1/residents'&&new URLSearchParams(x.query).getAll('statuses').length===3);i++)await wait(50);
  assert.ok(network.some(x=>x.page===manager.name&&x.path==='/api/v1/residents'&&new URLSearchParams(x.query).getAll('statuses').length===3),'Clicking footer through open menu lost the draft');
  await filters(manager);await multi(manager,'Trạng thái cư dân',['Đang hoạt động','Ngừng hoạt động']);await apply(manager,'residents',101);
  scenarios.push('native-mouse-apply-with-multi-select-menu-open');
  await filterViews(manager,'residents');await residentDrawerChecks(manager);await directoryVisualQa.verifyPagination({p:manager,kind:'residents',evaluate,until,network});scenarios.push('resident-ui-pagination-retains-all-multi-select-filters');scenarios.push('resident-filters-current-relations-floor-account-validation-reset-and-empty-results');
  const residentLists=await Promise.all([1,2].map(page=>apiRequest(manager,'GET','/api/v1/residents?search='+encodeURIComponent('Excel Nguyễn')+'&pageSize=100&pageIndex='+page)));
  assert.ok(residentLists.every(x=>x.status===200));
  const listedResidents=residentLists.flatMap(x=>x.body.items),residentOracle=JSON.parse(fs.readFileSync(path.join(out,'expected-residents.json'),'utf8'));
  assert.deepEqual(listedResidents.map(x=>[x.residentCode,x.fullName,'Đang hoạt động',x.hasAccount?'Đã liên kết tài khoản':'Chưa có tài khoản',x.apartmentRelationships.filter(r=>r.isCurrentOwner).map(r=>r.unitNumber).join(', '),x.apartmentRelationships.filter(r=>r.isCurrentResident).map(r=>r.unitNumber).join(', '),x.phoneNumber,x.email]),residentOracle);
  for(const width of [390,768,1440])for(const theme of ['light','dark'])await picture(manager,'residents',width,theme);
  const residents=await download(manager,'Xuất Excel','PropFlow_DanhSachCuDan_');
  assert.ok(network.some(x=>x.page===manager.name&&x.path==='/api/v1/residents/excel/export'&&new URLSearchParams(x.query).getAll('statuses').length===2),'Excel lost multi-select values');
  await rpc({action:'inspect',file:residents,kind:'residents'});scenarios.push('filtered-resident-export-all-101-xlsx-library-text-unicode');
  const accountant=await newPage('accountant');await login(accountant,'ACCOUNTANT');
  await navigate(accountant,'/residents');await until(accountant,"!!document.querySelector('.resident-page .pf-list-search input')");
  await search(accountant,'.resident-page .pf-list-search input','Excel Nguyễn','/ 101 cư dân');
  const privateList=await apiRequest(accountant,'GET','/api/v1/residents?search='+encodeURIComponent('Excel Nguyễn')+'&pageSize=100');
  assert.equal(privateList.status,200);assert.ok(privateList.body.items.every(x=>x.phoneNumber==null&&x.email==null));
  const redacted=await download(accountant,'Xuất Excel','PropFlow_DanhSachCuDan_');
  await rpc({action:'inspect',file:redacted,kind:'redacted'});scenarios.push('accountant-export-xlsx-library-no-email-or-phone-columns');
  await navigate(accountant,'/apartments');await until(accountant,"!!document.querySelector('.apartment-search input')");
  assert.equal(await evaluate(accountant,"[...document.querySelectorAll('button')].some(x=>x.textContent.trim()==='Nhập Excel')"),false);
  assert.equal((await apiRequest(accountant,'GET','/api/v1/apartments/excel/template')).status,403);scenarios.push('accountant-cannot-import');
  await rpc({action:'db',phase:'final-data-unchanged-after-exports',count:25});
  assert.equal(downloads.length,6,"Expected one workbook per successful download, with no extra template download");
  }
  const selectedFeatures=(process.env.PROPFLOW_FILTER_QA_FEATURES||'').split(',').filter(Boolean);
  const managerGroups=[['assets','manager-assets-filter-qa'],['requests','manager-requests-filter-qa'],['complaints','manager-complaints-filter-qa'],['maintenance','manager-maintenance-filter-qa'],['invoice','manager-finance-filter-qa'],['announcement','manager-announcements-filter-qa'],['parking','parking-real-qa'],['remote','remote-sign-real-qa']];
  assert.ok(selectedFeatures.every(x=>managerGroups.some(([feature])=>feature===x)),'Unknown QA feature');
  for(const [feature,module] of managerGroups){
   if(selectedFeatures.length&&!selectedFeatures.includes(feature))continue;
   await rpc({action:'progress',phase:'manager-'+feature+'-filters'});
   const qaPage=feature==='remote'?await newPage('admin-remote'):manager;if(feature==='remote')await login(qaPage,'ADMIN');
   await require('./'+module+'.cjs').verify({p:qaPage,navigate,until,evaluate,wait,filters,multi,network,checks,scenarios,apiRequest,send,shot,key,out,fs});
  }
  await rpc({action:'db',phase:'final-manager-filter-queries-no-business-writes',count:managerOnly?0:25});
  assert.deepEqual(jsErrors,[]);assert.deepEqual(consoleErrors,[]);
  await rpc({action:'complete',passed:true,scenarios,checks,jsErrors,consoleErrors,actualLogin:true,apiDoubles:false,requestInterception:false,workbookLibraryRead:!managerOnly,nativeExcelRead:false});
 }catch(error){fs.writeFileSync(path.join(out,'browser-failure.txt'),error.stack||error.message);console.error(error.stack||error.message);process.exitCode=1;}
 finally{
  fs.writeFileSync(path.join(out,'network-evidence.json'),JSON.stringify(network,null,2));
  fs.writeFileSync(path.join(out,'javascript-evidence.json'),JSON.stringify({jsErrors,consoleErrors},null,2));
  fs.writeFileSync(path.join(out,'asset-loading-evidence.json'),JSON.stringify(assetFailures,null,2));
  if(socket?.readyState===WebSocket.OPEN){try{await send('Browser.close')}catch{}socket.close();}
  if(browser&&browser.exitCode===null){await new Promise(resolve=>execFile('taskkill',['/PID',String(browser.pid),'/T','/F'],{windowsHide:true},resolve));if(browser.exitCode===null)await Promise.race([once(browser,'exit'),wait(5000)]);}
  if(browser)fs.writeFileSync(path.join(out,'browser-cleanup.json'),JSON.stringify({pid:browser.pid,stopped:browser.exitCode!==null,debugPort}));
  if(path.resolve(profile).startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(profile).startsWith('propflow-excel-browser-'))fs.rmSync(profile,{recursive:true,force:true,maxRetries:5,retryDelay:100});
  process.stdin.destroy();
 }
})();
