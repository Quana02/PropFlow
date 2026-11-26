const assert=require('node:assert/strict'),visual=require('./directory-filter-visual-qa.cjs');
exports.verify=async function(h){
 const {p,navigate,until,evaluate,filters,multi,network,key,scenarios,wait}=h;
 await navigate(p,'/manager/complaints');await until(p,"document.querySelectorAll('.table-scroll tbody tr').length===20");
 const entries=()=>network.filter(x=>x.page===p.name&&x.path==='/api/v1/complaints'&&x.status===200);
 await filters(p);await multi(p,'Trạng thái phản ánh',['Chờ tiếp nhận','Đang xem xét']);
 await evaluate(p,"document.querySelector('.pf-filter-apply').click()");
 for(let i=0;i<200&&new URLSearchParams(entries().at(-1)?.query).getAll('statuses').length!==2;i++)await wait(50);
 await until(p,"!document.querySelector('.pf-filter-popover:popover-open')&&document.querySelector('.table-footer')?.textContent.includes('24 phản ánh')");
 assert.equal(new URLSearchParams(entries().at(-1).query).getAll('statuses').length,2);
 await evaluate(p,"document.querySelector('nav[aria-label=\"Phân trang phản ánh\"] button[aria-label=\"Trang sau\"]').click()");await until(p,"document.querySelectorAll('.table-scroll tbody tr').length===4");assert.equal(new URLSearchParams(entries().at(-1).query).getAll('statuses').length,2);
 await filters(p);await multi(p,'Nhân viên đang xử lý phản ánh',['Fixture STAFF']);await evaluate(p,"document.querySelector('.pf-filter-apply').click()");await until(p,"!document.querySelector('.pf-filter-popover:popover-open')&&document.querySelectorAll('.table-scroll tbody tr').length===12");
 await filters(p);await evaluate(p,"[...document.querySelector('.pf-filter-popover:popover-open footer').querySelectorAll('button')].find(x=>x.textContent.trim()==='Đặt lại').click()");await key(p,'Escape',27);await filters(p);assert.ok((await evaluate(p,"[...document.querySelectorAll('.pf-filter-controls .selection-label')].map(x=>x.textContent)")).some(x=>x.includes('Fixture STAFF')));await key(p,'Escape',27);
 await visual.verifyDirectory({...h,kind:'complaints',fieldCount:5});
 scenarios.push('manager-complaints-real-status-staff-multi-pagination-draft-visual');
};
