const assert=require('node:assert/strict');
const path=require('node:path');

exports.verifyDirectory=async function({p,kind,send,evaluate,until,wait,shot,filters,key,checks,network,out,fs,fieldCount=7,listPath='/api/v1/'+kind}){
 const evidence=[];
 for(const [width,height] of [[1440,900],[1366,768],[1280,720],[768,900],[390,844],[390,420],[720,450]])for(const theme of ['light','dark']){
  await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<600},p.session);
  await send('Emulation.setTouchEmulationEnabled',{enabled:width<600,maxTouchPoints:1},p.session);
  await evaluate(p,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'});document.querySelector('.pf-filter-trigger').scrollIntoView({block:'center',behavior:'instant'})`);
  await filters(p);
  const requestCount=network.filter(x=>x.page===p.name&&x.path===listPath).length;
  // Reset edits draft only and reveals all seven fields for visual comparison.
  await evaluate(p,`[...document.querySelectorAll('.pf-filter-popover:popover-open footer button')].find(x=>x.textContent.trim()==='Đặt lại').click()`);
  await until(p,`document.querySelectorAll('.pf-filter-popover:popover-open .pf-filter-controls label').length===${fieldCount}&&![...document.querySelectorAll('.pf-filter-popover:popover-open .pf-select__trigger')].some(x=>x.disabled)`);await wait(180);
  const measurements=await evaluate(p,`(()=>{
   const d=document.querySelector('.pf-filter-popover:popover-open'),f=d.querySelector('footer'),h=d.querySelector('header'),body=d.querySelector('.pf-filter-fields');
   const rect=e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height}};
   const fields=[...d.querySelectorAll('.pf-filter-controls label')].map(label=>{
    const control=label.querySelector('.pf-select__trigger,input:not([type=checkbox]),.pf-date-range__trigger');
    return {label:label.firstElementChild.textContent.trim(),range:!!label.closest('.pf-filter-range'),rect:rect(control),labelRect:rect(label.firstElementChild)};
   });
   return {panel:rect(d),header:rect(h),footer:rect(f),buttons:[...f.querySelectorAll('button')].map(rect),fields,scrollbar:getComputedStyle(body).scrollbarColor,scrollbarWidth:body.offsetWidth-body.clientWidth,
    primaryText:getComputedStyle(f.querySelector('.pf-filter-apply')).color,primaryBackground:getComputedStyle(f.querySelector('.pf-filter-apply')).backgroundColor,
    icon:!!h.querySelector('h2 svg.pf-filter-icon'),horizontalOverflow:d.scrollWidth>d.clientWidth+1};
  })()`);
  fs.writeFileSync(path.join(out,`unified-${kind}-last-measurements.json`),JSON.stringify({width,height,theme,...measurements},null,2));
  await shot(p,`unified-${kind}-${width}x${height}-${theme}`,measurements.panel);
  assert.ok(measurements.icon,'Shared header icon missing');
  assert.ok(Math.abs(measurements.panel.width-Math.min(416,width-16))<1,'Directory drawer width differs');
  assert.equal(measurements.header.height,66);assert.equal(measurements.footer.height,77);
  assert.equal(measurements.horizontalOverflow,false);
  const full=measurements.fields.filter(x=>!x.range),reference=full[0].rect;
  for(const field of measurements.fields){
   assert.ok(Math.abs(field.rect.height-44)<1,kind+' control height: '+field.label);
   assert.ok(Math.abs(field.rect.top-field.labelRect.bottom-8)<1,kind+' label gap: '+field.label);
   if(!field.range){assert.ok(Math.abs(field.rect.left-reference.left)<1&&Math.abs(field.rect.right-reference.right)<1,kind+' uneven field width: '+field.label);}
  }
  assert.ok(Math.abs(measurements.buttons[0].left-reference.left)<1&&Math.abs(measurements.buttons[1].right-reference.right)<1,'Footer edges differ from controls');
  assert.ok(measurements.buttons.every(x=>Math.abs(x.height-44)<1)&&Math.abs(measurements.buttons[0].width-measurements.buttons[1].width)<1,'Uneven footer buttons');
  if(kind==='apartments'){
   const [min,max]=measurements.fields.filter(x=>x.range);
   assert.ok(Math.abs(min.rect.width-max.rect.width)<1&&Math.abs(min.rect.top-max.rect.top)<1&&Math.abs(min.rect.left-reference.left)<1&&Math.abs(max.rect.right-reference.right)<1,'Area pair is not balanced');
  }
  checks.push({page:'unified-'+kind,width,height,theme});
  // Scroll does not carry header/footer away, and final fields can be reached.
  await evaluate(p,`(()=>{const b=document.querySelector('.pf-filter-popover:popover-open .pf-filter-fields');b.scrollTop=b.scrollHeight})()`);await wait(80);
  assert.ok(await evaluate(p,`(()=>{const d=document.querySelector('.pf-filter-popover:popover-open'),b=d.querySelector('.pf-filter-fields');return Math.abs(d.querySelector('header').getBoundingClientRect().top-${measurements.header.top})<1&&Math.abs(d.querySelector('footer').getBoundingClientRect().top-${measurements.footer.top})<1&&b.scrollTop+b.clientHeight>=b.scrollHeight-1&&document.documentElement.scrollWidth<=innerWidth})()`),'Body scroll moves chrome or cannot reach last field');
  {
   const labels=await evaluate(p,`[...document.querySelectorAll('.pf-filter-popover:popover-open .pf-filter-controls .pf-select__trigger')].map(x=>x.getAttribute('aria-label'))`);
   for(const label of labels){
    await evaluate(p,`(()=>{const e=document.querySelector(${JSON.stringify('button[aria-label='+JSON.stringify(label)+']')});e.scrollIntoView({block:'nearest'});e.focus();e.click()})()`);
    await until(p,`!!document.querySelector('[role=listbox]:popover-open')`);await wait(60);
    assert.ok(await evaluate(p,`(()=>{const m=document.querySelector('[role=listbox]:popover-open').getBoundingClientRect(),d=document.querySelector('.pf-filter-popover:popover-open'),b=d.querySelector('.pf-filter-fields').getBoundingClientRect(),f=d.querySelector('footer').getBoundingClientRect();return m.left>=0&&m.right<=innerWidth+1&&m.top>=Math.max(0,b.top)-1&&m.bottom<=Math.min(innerHeight,f.top)+1})()`),kind+' menu clips body or covers footer: '+label);
    const usability=await evaluate(p,`(()=>{const m=document.querySelector('[role=listbox]:popover-open'),l=m.querySelector('.menu-options')??m,r=l.getBoundingClientRect(),options=[...m.querySelectorAll('[role=option]')];return {label:${JSON.stringify(label)},width:${width},height:${height},theme:${JSON.stringify(theme)},options:options.length,listHeight:l.clientHeight,visible:options.filter(e=>{const b=e.getBoundingClientRect();return b.top>=r.top-1&&b.bottom<=r.bottom+1}).length,menuHeight:m.getBoundingClientRect().height}})()`);
    evidence.push({menuUsability:usability});
    fs.appendFileSync(path.join(out,`unified-${kind}-menu-usability.jsonl`),JSON.stringify(usability)+'\n');
    if(label===labels.at(-1))await shot(p,`unified-${kind}-menu-${width}x${height}-${theme}`,await evaluate(p,"(()=>{const r=document.querySelector('.pf-filter-popover:popover-open').getBoundingClientRect();return {left:r.left,top:r.top,width:r.width,height:r.height}})()"));
    if((width===1440&&theme==='light'||width===390&&height===420&&theme==='dark')&&await evaluate(p,"document.querySelector('[role=listbox]:popover-open').getAttribute('aria-multiselectable')==='true'"))await shot(p,`unified-${kind}-multi-${labels.indexOf(label)}-${width}x${height}-${theme}`,await evaluate(p,"(()=>{const r=document.querySelector('.pf-filter-popover:popover-open').getBoundingClientRect();return {left:r.left,top:r.top,width:r.width,height:r.height}})()"));
    if(width>=1280&&usability.options>=5)assert.ok(usability.visible>=5,kind+' desktop menu has only '+usability.visible+' full options: '+label);
    if(width>=1280&&usability.options>=8&&await evaluate(p,"!!document.querySelector('[role=listbox]:popover-open .menu-options')"))assert.ok(usability.listHeight>=200,kind+' desktop option area is too short');
    if(height<500&&usability.options>=8)assert.ok(usability.visible>=2,kind+' short viewport menu is unusable');
    if(width===1440&&theme==='light'&&usability.options>=8){
     const searchSelector='[role=listbox]:popover-open input[type=search]';
     if(await evaluate(p,`!!document.querySelector(${JSON.stringify(searchSelector)})`)){
      const labelText=await evaluate(p,"document.querySelector('[role=listbox]:popover-open [role=option]').lastElementChild.textContent.trim()");
      await evaluate(p,`(()=>{const e=document.querySelector(${JSON.stringify(searchSelector)});e.focus();e.value=${JSON.stringify(labelText)};e.dispatchEvent(new Event('input',{bubbles:true}))})()`);await wait(120);
      assert.ok(await evaluate(p,"document.activeElement.matches('input[type=search]')"),'Search input did not take focus');
      assert.ok(await evaluate(p,"document.querySelectorAll('[role=listbox]:popover-open [role=option]').length<"+usability.options),'Option search did not narrow list');
      assert.ok(Math.abs(await evaluate(p,"document.querySelector('[role=listbox]:popover-open').getBoundingClientRect().height")-usability.menuHeight)<1,'Search resized the open menu');
      await evaluate(p,`(()=>{const e=document.querySelector(${JSON.stringify(searchSelector)});e.value='';e.dispatchEvent(new Event('input',{bubbles:true}))})()`);await wait(120);
     }
     const multiple=await evaluate(p,"document.querySelector('[role=listbox]:popover-open').getAttribute('aria-multiselectable')==='true'");
     if(multiple){await evaluate(p,"document.querySelector('[role=listbox]:popover-open [role=option]:not(:disabled)').click()");await wait(100);assert.equal(await evaluate(p,"document.querySelectorAll('[role=listbox]:popover-open [aria-selected=true]').length"),1);}
     const scroll=await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open'),l=m.querySelector('.menu-options')??m,r=l.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2,before:l.scrollTop,canScroll:l.scrollHeight>l.clientHeight}})()");
     if(scroll.canScroll){await send('Input.dispatchMouseEvent',{type:'mouseWheel',x:scroll.x,y:scroll.y,deltaX:0,deltaY:240},p.session);await wait(150);assert.ok(await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open');return (m.querySelector('.menu-options')??m).scrollTop>0})()"),'Mouse wheel did not scroll options');await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open');(m.querySelector('.menu-options')??m).scrollTop=0})()");}
     if(multiple){
      assert.equal(await evaluate(p,"document.querySelectorAll('[role=listbox]:popover-open [aria-selected=true]').length"),1,'Scrolling lost selection');
      assert.ok(Math.abs(await evaluate(p,"document.querySelector('[role=listbox]:popover-open').getBoundingClientRect().height")-usability.menuHeight)<1,'Selecting resized menu');
      const all=await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open'),b=[...m.querySelectorAll('.menu-actions button')].find(x=>x.textContent==='Chọn tất cả');if(!b)return false;b.click();return true})()");await wait(100);
      if(all)assert.equal(await evaluate(p,"document.querySelectorAll('[role=listbox]:popover-open [aria-selected=true]').length"),usability.options);
      await evaluate(p,"[...document.querySelectorAll('[role=listbox]:popover-open .menu-actions button')].find(x=>x.textContent==='Xóa chọn').click()");await wait(100);assert.equal(await evaluate(p,"document.querySelectorAll('[role=listbox]:popover-open [aria-selected=true]').length"),0);
     }
    }
    if(width===390&&height===844&&theme==='light'&&usability.options>=8){
     const point=await evaluate(p,"(()=>{const m=document.querySelector('[role=listbox]:popover-open'),l=m.querySelector('.menu-options')??m;l.scrollTop=0;const r=l.getBoundingClientRect(),point={x:r.left+r.width/2,y:r.top+r.height*.7};window.__dropdownTouch={controller:new AbortController(),events:[],hit:!!document.elementFromPoint(point.x,point.y)?.closest('[role=listbox]'),touchAction:getComputedStyle(l).touchAction,scrollHeight:l.scrollHeight,clientHeight:l.clientHeight};for(const t of ['touchstart','touchmove','touchend'])m.addEventListener(t,()=>window.__dropdownTouch.events.push(t),{signal:window.__dropdownTouch.controller.signal,passive:true});return point})()");
     await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{...point,id:0}]},p.session);
     for(let i=1;i<=8;i++){await send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:point.x,y:point.y-i*15,id:0}]},p.session);await wait(25);}
     await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]},p.session);await wait(250);
     const touch=await evaluate(p,"(()=>{const t=window.__dropdownTouch,m=document.querySelector('[role=listbox]:popover-open'),l=m?.querySelector('.menu-options')??m;t.controller.abort();const result={hit:t.hit,touchAction:t.touchAction,events:t.events,scrollHeight:t.scrollHeight,clientHeight:t.clientHeight,scrollTop:l?.scrollTop??0};delete window.__dropdownTouch;return result})()");
     fs.appendFileSync(path.join(out,'dropdown-touch-evidence.jsonl'),JSON.stringify({kind,label,...touch})+'\n');
     assert.ok(touch.scrollTop>0,'Direct touch did not scroll options: '+JSON.stringify(touch));
    }
    if(width===1440&&theme==='light'){
     const multiple=await evaluate(p,`document.querySelector('[role=listbox]:popover-open').getAttribute('aria-multiselectable')==='true'`);
     await key(p,'ArrowDown',40);
     if(multiple){await key(p,' ',32);await key(p,'Enter',13);}
     else {
      await key(p,'Enter',13);await until(p,`!document.querySelector('[role=listbox]:popover-open')`);
      assert.ok(await evaluate(p,`!!document.querySelector('.pf-filter-popover:popover-open')`),'Enter selected an option and accidentally applied');
      await evaluate(p,`(()=>{const e=document.querySelector(${JSON.stringify('button[aria-label='+JSON.stringify(label)+']')});e.focus();e.click()})()`);await until(p,`!!document.querySelector('[role=listbox]:popover-open')`);
     }
    }
    const overlayClose=await evaluate(p,"!!document.querySelector('[role=listbox][data-filter-placement=overlay]:popover-open [data-filter-menu-close]')");
    if(overlayClose&&width===1440&&theme==='light'){
     await evaluate(p,"[...document.querySelector('[role=listbox]:popover-open').querySelectorAll('button:not(:disabled),input:not(:disabled)')].at(-1).focus()");await key(p,'Tab',9);
     assert.ok(await evaluate(p,"document.activeElement.hasAttribute('data-filter-menu-close')"),'Tab escaped inner overlay');
     await key(p,'Tab',9,8);assert.ok(await evaluate(p,"!!document.activeElement.closest('[role=listbox]:popover-open')"),'Shift+Tab escaped inner overlay');
    }
    if(overlayClose&&width===1440&&theme==='light'){
     await evaluate(p,"document.querySelector('[role=listbox]:popover-open [data-filter-menu-close]').click()");await until(p,"!document.querySelector('[role=listbox]:popover-open')");
     assert.equal(await evaluate(p,"document.activeElement.getAttribute('aria-label')"),label,'Overlay close did not return focus');
     await evaluate(p,`document.querySelector(${JSON.stringify('button[aria-label='+JSON.stringify(label)+']')}).click()`);await until(p,"!!document.querySelector('[role=listbox]:popover-open')");
    }
    await key(p,'Escape',27);assert.ok(await evaluate(p,`!!document.querySelector('.pf-filter-popover:popover-open')&&!document.querySelector('[role=listbox]:popover-open')`),'Escape must close menu before drawer');
   }
   const calendarTrigger=await evaluate(p,"!!document.querySelector('.pf-filter-popover:popover-open .pf-date-range__trigger')");
   if(calendarTrigger){
    await evaluate(p,"(()=>{const b=document.querySelector('.pf-filter-popover:popover-open .pf-date-range__trigger');b.scrollIntoView({block:'nearest'});b.focus();b.click()})()");
    await until(p,"!!document.querySelector('.pf-date-range__calendar:popover-open')");await wait(80);
    assert.ok(await evaluate(p,"(()=>{const c=document.querySelector('.pf-date-range__calendar:popover-open').getBoundingClientRect(),b=document.querySelector('.pf-filter-popover:popover-open .pf-filter-fields').getBoundingClientRect();return c.top>=b.top-1&&c.bottom<=b.bottom+1&&c.left>=0&&c.right<=innerWidth+1})()"),'Date calendar covers drawer chrome');
    const calendar=await evaluate(p,"(()=>{const c=document.querySelector('.pf-date-range__calendar:popover-open');return {height:c.clientHeight,contentHeight:c.scrollHeight}})()");
    fs.appendFileSync(path.join(out,'dropdown-calendar-evidence.jsonl'),JSON.stringify({kind,viewportWidth:width,viewportHeight:height,theme,...calendar})+'\n');
    await shot(p,`unified-${kind}-calendar-${width}x${height}-${theme}`,await evaluate(p,"(()=>{const r=document.querySelector('.pf-filter-popover:popover-open').getBoundingClientRect();return {left:r.left,top:r.top,width:r.width,height:r.height}})()"));
    if(width>=1280)assert.ok(calendar.contentHeight<=calendar.height+1,'Desktop calendar requires scrolling despite available viewport');
    await key(p,'Escape',27);await until(p,"!document.querySelector('.pf-date-range__calendar:popover-open')&&!!document.querySelector('.pf-filter-popover:popover-open')");
   }
  }
  await evaluate(p,`document.querySelector('.pf-filter-popover:popover-open .pf-filter-apply').focus()`);await key(p,'Tab',9);
  assert.ok(await evaluate(p,`document.activeElement.classList.contains('pf-filter-close')`),'Tab escaped directory drawer');
  await key(p,'Tab',9,8);assert.ok(await evaluate(p,`document.activeElement.classList.contains('pf-filter-apply')`),'Shift+Tab escaped directory drawer');
  // Enter on a field may interact with that field, but never auto-applies.
  if(kind==='apartments'){
   await evaluate(p,`document.querySelector('.pf-filter-popover:popover-open input[aria-label="Diện tích từ"]').focus()`);await key(p,'Enter',13);
   assert.ok(await evaluate(p,`!!document.querySelector('.pf-filter-popover:popover-open')`),'Enter in floor input applied the filter');
  }
  assert.equal(network.filter(x=>x.page===p.name&&x.path===listPath).length,requestCount,'Draft reset/interaction sent a list query');
  await key(p,'Escape',27);await until(p,`!document.querySelector('.pf-filter-popover:popover-open')`);
  assert.ok(await evaluate(p,`document.activeElement===document.querySelector('.pf-filter-trigger')`),'Focus did not return');
  evidence.push({width,height,theme,...measurements});
 }
 fs.writeFileSync(path.join(out,`unified-${kind}-measurements.json`),JSON.stringify(evidence,null,2));
 await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
 await send('Emulation.setTouchEmulationEnabled',{enabled:false},p.session);
};

exports.verifyDefaultCallers=async function({p,navigate,until,evaluate,filters,key,shot,checks,send}){
 await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
 for(const route of ['/facilities','/equipment'])for(const theme of ['light','dark']){
  await navigate(p,route);await until(p,`!!document.querySelector('.pf-filter-trigger:not(:disabled)')`);
  await until(p,`document.querySelector('.pf-filter-trigger').getAttribute('aria-disabled')!=='true'`);await evaluate(p,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'})`);await filters(p);
  assert.ok(await evaluate(p,`!document.querySelector('.pf-filter--directory-drawer')&&document.querySelector('.pf-filter-popover:popover-open').getBoundingClientRect().width<416`),'Default filter inherited directory variant');
  await shot(p,'default-filter-'+route.slice(1)+'-'+theme);checks.push({page:'default-filter-'+route.slice(1),width:1440,theme});
  await key(p,'Escape',27);await until(p,`!document.querySelector('.pf-filter-popover:popover-open')`);
 }
};

exports.verifyPagination=async function({p,kind,evaluate,until,network}){
 const resource='/api/v1/'+kind;
 const entries=()=>network.filter(x=>x.page===p.name&&x.path===resource&&x.method==='GET'&&x.status===200);
 const original=entries().at(-1);
 const criteria=query=>[...new URLSearchParams(query)].filter(([key])=>!['pageIndex','pageSize'].includes(key)).map(x=>x.join('=')).sort();
 const originalCriteria=criteria(original.query);
 const row=kind==='residents'?'.resident-table-wrap tbody tr:first-child td:first-child strong':'.apartment-panel tbody tr:first-child td:first-child';
 const first=await evaluate(p,`document.querySelector(${JSON.stringify(row)}).textContent.trim()`);
 for(const [button,page] of [['Trang sau',2],['Trang trước',1]]){
  const selector=`.table-pagination button[aria-label="${button}"]`;
  await until(p,`!!document.querySelector(${JSON.stringify(selector)}+':not(:disabled)')`);
  const before=entries().length;
  await evaluate(p,`document.querySelector(${JSON.stringify(selector)}).click()`);
  await until(p,`document.querySelector('.table-pagination .page-number').getAttribute('aria-label').startsWith('Trang ${page} trên')&&document.querySelector(${JSON.stringify(row)}).textContent.trim()${page===2?'!==':'==='}${JSON.stringify(first)}`);
  assert.ok(entries().length>before,'Pagination did not read the real API');
  const current=entries().at(-1);assert.equal(new URLSearchParams(current.query).get('pageIndex'),String(page));
  assert.deepEqual(criteria(current.query),originalCriteria,'Pagination lost an applied filter');
 }
};
