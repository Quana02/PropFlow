const assert=require('node:assert/strict');
exports.verify=async function({p,navigate,until,evaluate,wait,apiRequest,send,shot,scenarios}){
 await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
 await navigate(p,'/remote-contracts');
 await until(p,"[...document.querySelectorAll('button')].some(x=>x.textContent.trim().endsWith('Tạo hợp đồng')&&!x.disabled)");
 await evaluate(p,"[...document.querySelectorAll('button')].find(x=>x.textContent.trim().endsWith('Tạo hợp đồng')).click()");
 await until(p,"!!document.querySelector('.remote-create-form')");
 await evaluate(p,"document.querySelector('.remote-create-modal__close').click()");
 const created=await apiRequest(p,'POST','/api/v1/remote-online-sign',{contractName:'Fixture hợp đồng thực tế',deadline:null});assert.equal(created.status,201);
 const id=created.body.id;
 let r=await apiRequest(p,'GET','/api/v1/remote-online-sign/'+id);assert.equal(r.status,200);assert.equal(r.body.contractName,'Fixture hợp đồng thực tế');assert.match(r.body.contractCode,/^HD-/);
 const version=r.body.version;
 r=await apiRequest(p,'PUT','/api/v1/remote-online-sign/'+id+'/metadata',{contractName:'Fixture đã cập nhật',deadline:null,version});assert.equal(r.status,200);
 assert.equal((await apiRequest(p,'PUT','/api/v1/remote-online-sign/'+id+'/metadata',{contractName:'Stale fixture',deadline:null,version})).status,409);
 const candidates=await apiRequest(p,'GET','/api/v1/remote-online-sign/resident-candidates');assert.equal(candidates.status,200);assert.ok(candidates.body.length>0,'Seeded linked resident must appear in actual candidates query');
 await navigate(p,'/remote-contracts');await until(p,"document.body.textContent.includes('Fixture đã cập nhật')");
 for(const width of [1440,768,390])for(const theme of ['light','dark']){await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:width<600},p.session);await evaluate(p,`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.documentElement.classList.toggle('dark',${theme==='dark'})`);await wait(200);await shot(p,`remote-real-list-${width}-${theme}`);assert.ok(await evaluate(p,"document.documentElement.scrollWidth<=innerWidth+1"),'Remote page overflow');}
 await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false},p.session);
 await evaluate(p,"[...document.querySelectorAll('tr')].find(x=>x.textContent.includes('Fixture đã cập nhật')).querySelector('button.remote-delete-action').click()");
 await until(p,"!!document.querySelector('#remote-delete-title')");
 await evaluate(p,"document.querySelector('button.remote-delete-danger').click()");
 await until(p,"!document.querySelector('#remote-delete-title')&&!document.querySelector('.remote-contract-name[title=\"Fixture đã cập nhật\"]')");
 assert.equal((await apiRequest(p,'GET','/api/v1/remote-online-sign/'+id)).status,404);
 scenarios.push('remote-real-admin-login-ui-form-api-create-postgresql-update-stale409-candidates-refresh-six-viewports-ui-delete-no-api-double');
};
