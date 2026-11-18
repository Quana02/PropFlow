import assert from 'node:assert/strict';

export default async function verifyPostPull({login,navigate,evaluate,until,click,pressKey,api,setViewport,snapshot,save}) {
  const manager=await login('manager'); const accountant=await login('accountant'); const staff=await login('staff');
  const admin=await login('admin'); const resident=await login('resident');
  const initial=await api(manager,'/api/v1/facility-guides');
  await save({setup:{managementStatus:initial.status,facilityCount:initial.data?.facilities?.length}});
  assert.equal(initial.status,200);
  if(!initial.data.facilities.length) {
    const created=await api(manager,'/api/v1/Facilities',{code:'POST-PULL-GUIDE',name:'Tiện ích kiểm thử shared select',locationDescription:'Vị trí fixture'});
    assert.equal(created.status,201);
  }
  await navigate(manager,'/operations/facility-guides');
  await until(()=>evaluate(manager,'document.querySelectorAll(".facility-guides .pf-select").length===4'),'All four shared selects must render');
  assert.equal(await evaluate(manager,'document.querySelectorAll(".facility-guides select").length'),0);
  const data=await api(manager,'/api/v1/facility-guides');assert.equal(data.status,200);assert.ok(data.data.facilities.length);
  const measurements=[];
  for(const width of [390,768,1440]) {
    await setViewport(manager,width);
    for(const mode of ['light','dark']) {
      await evaluate(manager,`if(document.documentElement.dataset.theme!==${JSON.stringify(mode)})window.propFlowTheme.toggle()`);
      await evaluate(manager,'document.querySelector("#guide-audience").scrollIntoView({block:"center",behavior:"instant"});document.querySelector("#guide-audience").focus({preventScroll:true})');
      await click(manager,'#guide-audience');
      await until(()=>evaluate(manager,'Boolean(document.querySelector(".facility-guides [role=listbox]"))'),'Shared select must open');
      await evaluate(manager,'new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
      const aria=await evaluate(manager,'({expanded:document.querySelector("#guide-audience").getAttribute("aria-expanded"),selected:[...document.querySelectorAll(".facility-guides [role=option]")].map(e=>e.getAttribute("aria-selected"))})');
      assert.equal(aria.expanded,'true');assert.equal(aria.selected.filter(value=>value==='true').length,1);assert.ok(aria.selected.every(value=>value==='true'||value==='false'));
      const box=await evaluate(manager,`(()=>{const e=document.querySelector('.facility-guides [role=listbox]');const r=e.getBoundingClientRect();const s=getComputedStyle(e);const root=document.querySelector('.facility-guides');return {fits:r.left>=-1&&r.right<=innerWidth+1&&r.top>=-1&&r.bottom<=innerHeight+1,background:s.backgroundColor,text:s.color,overflow:root.scrollWidth<=innerWidth,rect:{left:r.left,right:r.right,top:r.top,bottom:r.bottom},rootWidth:root.scrollWidth,viewport:innerWidth,popover:e.matches(':popover-open')};})()`);
      await snapshot(manager,'post-pull-select-'+width+'-'+mode);
      await save({setup:{managementStatus:initial.status,facilityCount:initial.data.facilities.length},measurements:[...measurements,{width,mode,...box}]});
      assert.ok(box.fits&&box.overflow,'Facility select overflows '+width);assert.notEqual(box.background,box.text);
      await snapshot(manager,'post-pull-select-'+width+'-'+mode);measurements.push({width,mode,...box});
      await pressKey(manager,'Escape');
      await evaluate(manager,'new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
      await save({measurements,keyboardProbe:await evaluate(manager,'({expanded:document.querySelector("#guide-audience").getAttribute("aria-expanded"),listboxes:document.querySelectorAll(".facility-guides [role=listbox]").length,focused:document.activeElement?.id})')});
      await until(()=>evaluate(manager,'document.querySelector("#guide-audience").getAttribute("aria-expanded")?.toLowerCase()==="false"&&!document.querySelector(".facility-guides [role=listbox]")'),'Escape must close shared select');
    }
  }
  await evaluate(manager,'document.querySelector("#guide-audience").focus()');
  await pressKey(manager,'ArrowDown');
  await until(()=>evaluate(manager,'document.querySelector("#guide-audience").getAttribute("aria-expanded")?.toLowerCase()==="true"&&Boolean(document.querySelector(".facility-guides [role=listbox]"))'),'Arrow key must open shared select');
  await until(()=>evaluate(manager,'Boolean(document.querySelector(".facility-guides [role=listbox]")?.matches(":popover-open"))'),'Arrow-opened popover must finish JS setup');
  await pressKey(manager,'Enter');
  await until(()=>evaluate(manager,'document.querySelector("#guide-audience").getAttribute("aria-expanded")?.toLowerCase()==="false"&&!document.querySelector(".facility-guides [role=listbox]")'),'Enter must select and close');
  const edited='Nội dung nháp giữ lại khi API lỗi';
  await evaluate(manager,`(()=>{const e=document.querySelector('.facility-guides textarea');e.value=${JSON.stringify(edited)};e.dispatchEvent(new Event('change',{bubbles:true}));const native=window.fetch.bind(window);window.fetch=async(resource,options)=>{const url=new URL(resource instanceof Request?resource.url:resource,location.href);if(url.pathname.startsWith('/api/v1/facility-guides/')&&(options?.method??resource?.method)?.toUpperCase()==='PUT'){window.fetch=native;return new Promise(resolve=>window.releaseGuideFailure=()=>resolve(new Response(JSON.stringify({status:503,title:'Fixture tạm gián đoạn'}),{status:503,headers:{'Content-Type':'application/problem+json'}})))}return native(resource,options)};})()`);
  await click(manager,'.facility-guides button[type=submit]');
  await until(()=>evaluate(manager,'document.querySelector("#guide-facility").disabled&&document.querySelector("#guide-audience").disabled&&Boolean(window.releaseGuideFailure)'),'Busy selects must be disabled');
  await evaluate(manager,'window.releaseGuideFailure()');
  await until(()=>evaluate(manager,'Boolean(document.querySelector(".facility-guides [role=alert]"))'),'Failure feedback missing');
  assert.equal(await evaluate(manager,'document.querySelector(".facility-guides textarea").value'),edited);
  assert.equal(await evaluate(manager,'document.querySelector("#guide-facility").disabled'),false);
  for(const [tab,path] of [[manager,'/operations/utility-insights'],[accountant,'/finance/utility-insights']]) {
    await navigate(tab,path);await until(()=>evaluate(tab,'Boolean(document.querySelector(".utility-workspace"))'),'Approved utility role route missing');
    assert.equal((await api(tab,'/api/v1/utility-insights?month=2026-10-01')).status,200);
  }
  await navigate(staff,'/staff/complaints');await until(()=>evaluate(staff,'location.pathname==="/staff/complaints"&&document.querySelector(".staff-nav")'),'Staff complaints route missing');
  assert.ok(await evaluate(staff,'[...document.querySelectorAll(".staff-nav a")].some(link=>link.getAttribute("href")==="/staff/complaints"&&link.getAttribute("aria-current")==="page")'));
  for(const tab of [staff,admin,resident]) {
    assert.equal((await api(tab,'/api/v1/utility-insights?month=2026-10-01')).status,403);
    await navigate(tab,'/operations/utility-insights');
    await until(()=>evaluate(tab,'!document.querySelector(".utility-workspace")&&document.body.innerText.includes("quyền")'),'Wrong role loaded private utility UI');
  }
  await evaluate(manager,'Blazor.navigateTo("/account")');
  await until(()=>evaluate(manager,'location.pathname==="/account"&&!document.querySelector(".facility-guides")'),'Leave guide before empty state probe');
  await evaluate(manager,`(()=>{const native=window.fetch.bind(window);window.fetch=async(resource,...args)=>{const url=new URL(resource instanceof Request?resource.url:resource,location.href);if(url.pathname==='/api/v1/facility-guides'){window.fetch=native;return new Promise(resolve=>window.releaseEmptyGuide=()=>resolve(new Response(JSON.stringify({facilities:[],staff:[],today:${JSON.stringify(data.data.today)}}),{status:200,headers:{'Content-Type':'application/json'}})))}return native(resource,...args)};Blazor.navigateTo('/operations/facility-guides');})()`);
  await until(()=>evaluate(manager,'document.querySelector(".facility-guides [role=status]")?.innerText.includes("Đang tải")&&Boolean(window.releaseEmptyGuide)'),'Guide loading state missing');
  await evaluate(manager,'window.releaseEmptyGuide()');
  await until(()=>evaluate(manager,'Boolean(document.querySelector("#guide-facility")?.disabled)&&!document.querySelector(".facility-guides form")&&document.querySelector(".facility-guides").innerText.includes("Tạo hồ bơi")'),'Empty guide state must disable selection');
  await save({sharedSelects:4,keyboard:true,busyDisabled:true,loadingAndEmpty:true,failedSaveRetainsDraft:true,approvedUtilityRoles:true,wrongRolesBlocked:true,staffComplaintMenu:true,measurements});
}
