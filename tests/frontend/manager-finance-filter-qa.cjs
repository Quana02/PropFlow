const assert=require('node:assert/strict'),visual=require('./directory-filter-visual-qa.cjs');
exports.verify=async function(h){
 const {p,navigate,until,evaluate,wait,filters,multi,apiRequest,key,scenarios}=h;
 await navigate(p,'/operations/service-fee');await until(p,"document.querySelectorAll('.invoice-table tbody tr').length===10&&!!document.querySelector('.pf-filter-trigger:not(:disabled)')");
 const all=await apiRequest(p,'GET','/api/v1/billing/cycle/invoices');assert.equal(all.status,200);assert.equal(all.body.filter(x=>x.number.startsWith('QA-INV-')).length,24);
 await evaluate(p,"(()=>{const e=document.querySelector('input[aria-label=\"Tìm hóa đơn\"]');e.value='QA-INV-';e.dispatchEvent(new Event('input',{bubbles:true}))})()");await until(p,"document.querySelector('.invoice-toolbar .result-count').textContent.includes('24')");
 const periods=[...new Set(all.body.filter(x=>x.number.startsWith('QA-INV-')).map(x=>x.period.slice(0,7)))].sort().reverse().slice(0,6).map(x=>x.slice(5)+'/'+x.slice(0,4));
 await filters(p);await multi(p,'Lọc kỳ hóa đơn',periods);await multi(p,'Lọc loại hóa đơn',['Phí dịch vụ','Tiền thuê tháng']);await multi(p,'Lọc thanh toán',['Chưa thanh toán','Thanh toán một phần','Đã thanh toán','Quá hạn']);
 assert.ok(await evaluate(p,"document.querySelector('.invoice-toolbar .result-count').textContent.includes('24')"),'Draft applied automatically');
 await evaluate(p,"document.querySelector('.pf-filter-apply').click()");await until(p,"!document.querySelector('.pf-filter-popover:popover-open')&&document.querySelector('.invoice-toolbar .result-count').textContent.includes('12')");
 assert.equal(await evaluate(p,"document.querySelector('.pf-management-metrics strong').textContent"),'12');
 await evaluate(p,"document.querySelector('nav[aria-label=\"Phân trang hóa đơn\"] button[aria-label=\"Trang sau\"]').click()");await until(p,"document.querySelectorAll('.invoice-table tbody tr').length===2");
 await filters(p);await evaluate(p,"[...document.querySelector('.pf-filter-popover:popover-open footer').querySelectorAll('button')].find(x=>x.textContent.trim()==='Đặt lại').click()");await key(p,'Escape',27);await filters(p);assert.ok((await evaluate(p,"[...document.querySelectorAll('.selection-label')].map(x=>x.textContent)")).some(x=>x.includes('6')));await key(p,'Escape',27);
 await visual.verifyDirectory({...h,kind:'manager-invoices',listPath:'/api/v1/billing/cycle/invoices',fieldCount:3});
 scenarios.push('manager-invoices-real-postgresql-complete-collection-multi-OR-AND-overlap-summary-pagination-visual');
};
