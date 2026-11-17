// The real chat and resident styles, measured in an isolated browser with no application data.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http');
const {spawn,execFile}=require('node:child_process');const {once}=require('node:events');
const root=path.resolve(__dirname,'../..'),wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const css=['src/PropFlow.Web/wwwroot/app.css','src/PropFlow.Web.Client/wwwroot/css/resident-portal.css','src/PropFlow.Web.Client/Features/Communication/SupportChat/SupportChatWorkspace.razor.css','src/PropFlow.Web.Client/wwwroot/css/chat-composer.css'].map(file=>fs.readFileSync(path.join(root,file),'utf8')).join('\n');
const html=`<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:Arial}*{box-sizing:border-box}body{padding:20px}${css}</style></head><body><div class="resident-portal"><div class="support-workspace">
<header class="support-heading"><div><span class="support-eyebrow">KẾT NỐI TRỰC TIẾP</span><h1>Liên hệ Ban quản lý</h1><p>Mô tả nhu cầu của bạn để được hỗ trợ.</p></div><span class="support-connection online">Đã kết nối trực tuyến</span></header>
<div class="support-columns"><aside class="support-list"><div class="support-list-heading"><h2>Lịch sử liên hệ</h2><button>Phiên mới</button></div><button class="conversation-link selected"><span><strong>Quản lý demo</strong><small>Đang trao đổi</small></span><time>17/11 19:00</time></button><button class="conversation-link"><span><strong>Phiên trước</strong><small>Đã kết thúc</small></span></button><div class="support-pagination"><button>Trước</button><span>Trang 1</span><button>Sau</button></div><p class="support-empty">Lịch sử liên hệ</p></aside>
<section class="support-chat"><header class="support-chat-heading"><div><h2>Quản lý demo</h2><small>Tin đã gửi vẫn được lưu trong lịch sử.</small></div><button class="support-close">Kết thúc phiên</button></header><p class="draft-disclosure">Chỉ tin đã gửi được lưu vào lịch sử.</p><div class="support-thread"><article class="support-bubble system"><small>Trợ lý tiếp nhận</small><p>Đã kết nối với Ban quản lý.</p></article><article class="support-bubble theirs"><div><strong>Ban quản lý</strong><time>19:00</time></div><p>Tôi có thể giúp gì?</p></article><article class="support-bubble mine"><div><strong>Bạn</strong><time>19:01</time></div><p>Tôi cần hỗ trợ sửa chữa.</p><small class="support-message-label support-receipt">Đã xem</small></article><div class="support-live-draft"><strong>Bản nháp</strong><p>Đang nhập nội dung</p></div><p class="support-typing">Đang trả lời…</p></div><form class="support-compose"><div class="support-reply-draft"><span>Đang trả lời BQL</span></div><div class="support-compose-row pf-chat-composer"><textarea placeholder="Nhập tin nhắn">Nội dung đang nhập</textarea><button type="button" class="support-send pf-chat-send">➤</button></div><div class="support-compose-meta"><small>Enter để gửi</small><small>0 / 4.000 ký tự</small></div></form></section></div><div class="support-ended">Phiên đã kết thúc <button>Mở phiên mới</button></div><div class="support-error">Không thể kết nối <button>Thử lại</button></div></div></div></body></html>`;
const browserPath=[process.env.RESIDENT_TEST_BROWSER,'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(file=>file&&fs.existsSync(file));
if(!browserPath)throw Error('No browser available.');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'propflow-chat-theme-'));
const server=http.createServer((request,response)=>{response.setHeader('Content-Type','text/html; charset=utf-8');response.end(html);});
let browser,socket,port,debugPort,sequence=0;const pending=new Map();
function send(method,params={},sessionId){return new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(Error('Browser timeout'));},15000);pending.set(id,{resolve:value=>{clearTimeout(timer);resolve(value);},reject:error=>{clearTimeout(timer);reject(error);}});socket.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
(async()=>{try{
 server.listen(0,'127.0.0.1');await once(server,'listening');port=server.address().port;
 browser=spawn(browserPath,['--headless=new','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+profile,'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','about:blank'],{windowsHide:true,stdio:'ignore'});
 console.log(JSON.stringify({browserPid:browser.pid,serverPort:port}));
 const active=path.join(profile,'DevToolsActivePort');for(let i=0;i<300&&!fs.existsSync(active);i++)await wait(50);
 const [portText,endpoint]=fs.readFileSync(active,'utf8').trim().split(/\r?\n/);debugPort=Number(portText);socket=new WebSocket('ws://127.0.0.1:'+debugPort+endpoint);await once(socket,'open');
 socket.addEventListener('message',event=>{const message=JSON.parse(event.data),callback=pending.get(message.id);if(callback){pending.delete(message.id);message.error?callback.reject(Error(message.error.message)):callback.resolve(message.result);}});
 const target=await send('Target.createTarget',{url:'http://127.0.0.1:'+port});const session=(await send('Target.attachToTarget',{targetId:target.targetId,flatten:true})).sessionId;
 const evaluate=async expression=>{const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true},session);if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description??result.exceptionDetails.text);return result.result.value;};
 for(let i=0;i<100&&!await evaluate('!!document.querySelector(".support-compose")');i++)await wait(50);
 const checks=[];
 for(const width of [1366,820,390,320])for(const mode of ['light','dark','attribute']){
  await send('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:false},session);
  await evaluate(`document.documentElement.className=${JSON.stringify(mode==='dark'?'dark':'')};document.documentElement.dataset.theme=${JSON.stringify(mode==='light'?'light':'dark')}`);
  const result=await evaluate(`(()=>{
   const rgb=value=>value.match(/[\\d.]+/g).map(Number);const blend=(front,back)=>{const a=front[3]??1;return front.slice(0,3).map((v,i)=>v*a+back[i]*(1-a));};
   function background(element){if(!element)return [255,255,255];const c=rgb(getComputedStyle(element).backgroundColor);return blend(c,background(element.parentElement));}
   const luminance=rgb=>rgb.map(v=>{v/=255;return v<=.04045?v/12.92:((v+.055)/1.055)**2.4}).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
   const selectors=['.support-heading h1','.support-heading p','.support-eyebrow','.support-connection','.support-list h2','.conversation-link strong','.conversation-link small','.conversation-link time','.support-pagination span','.support-chat-heading h2','.support-chat-heading small','.support-close','.draft-disclosure','.support-bubble.mine p','.support-bubble.theirs p','.support-bubble.system p','.support-bubble time','.support-bubble strong','.support-bubble>small','.support-live-draft p','.support-typing','.support-compose textarea','.support-compose-meta small','.support-reply-draft','.support-ended','.support-error'];
   const contrasts=selectors.map(selector=>{const el=document.querySelector(selector),fg=luminance(rgb(getComputedStyle(el).color)),bg=luminance(background(el));return {selector,ratio:Math.round(((Math.max(fg,bg)+.05)/(Math.min(fg,bg)+.05))*100)/100};});
   const surfaces=['.support-list','.conversation-link','.support-chat','.support-thread','.support-bubble.mine','.support-bubble.theirs','.support-bubble.system','.draft-disclosure','.support-compose'];
   return {overflow:document.documentElement.scrollWidth>innerWidth+1,contrasts,lightSurfaces:surfaces.filter(selector=>Math.max(...background(document.querySelector(selector)))>110),threadBackground:getComputedStyle(document.querySelector('.support-thread')).backgroundColor};
  })()`);
  const failures=result.contrasts.filter(x=>x.ratio<(mode==='light'?3:4.5));
  checks.push({width,mode,overflow:result.overflow,failures,lightSurfaces:mode==='light'?[]:result.lightSurfaces,threadBackground:result.threadBackground});
  if(width===1366&&mode!=='attribute'){const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},session);fs.writeFileSync(path.join(os.tmpdir(),'propflow-support-chat-'+mode+'.png'),Buffer.from(shot.data,'base64'));}
 }
 console.log(JSON.stringify({passed:checks.every(x=>!x.overflow&&!x.failures.length&&!x.lightSurfaces.length),checks}));if(checks.some(x=>x.overflow||x.failures.length||x.lightSurfaces.length))process.exitCode=1;
}catch(error){console.error(error.message);process.exitCode=1;}finally{
 if(socket?.readyState===WebSocket.OPEN){try{await send('Browser.close');}catch{}socket.close();}
 if(browser&&browser.exitCode===null){await Promise.race([once(browser,'exit'),wait(5000)]);if(browser.exitCode===null)await new Promise(resolve=>execFile('taskkill',['/PID',String(browser.pid),'/T','/F'],{windowsHide:true},resolve));}
 server.closeAllConnections();await new Promise(resolve=>server.close(resolve));for(const callback of pending.values())callback.reject(Error('Browser closed'));pending.clear();
 const resolved=path.resolve(profile);if(resolved.startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(resolved).startsWith('propflow-chat-theme-'))fs.rmSync(resolved,{recursive:true,force:true,maxRetries:5,retryDelay:100});
 console.log(JSON.stringify({cleanupComplete:true,browserPid:browser?.pid,serverPort:port,debugPort}));
}})();
