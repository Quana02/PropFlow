const assert=require('node:assert/strict'),visual=require('./directory-filter-visual-qa.cjs');
exports.verify=async function(h){
 const {p,navigate,until,evaluate,filters,multi,network,scenarios,key,wait}=h;
 await navigate(p,'/maintenance');await until(p,"!!document.querySelector('.table-pagination')&&document.querySelector('.table-footer')?.textContent.includes('125 lịch')");
 for(const [index,resource,label,statuses,count,fields] of [[0,'schedules','lịch bảo trì',['Đã hủy','Đang hoạt động'],125,3],[1,'tasks','công việc bảo trì',['Mở','Đã giao'],24,7],[2,'history','lịch sử bảo trì',['Mở','Đã giao'],24,7]]){
  if(index){await evaluate(p,`document.querySelectorAll('[role=tablist][aria-label="Không gian bảo trì"] button')[${index}].click()`);await until(p,"!!document.querySelector('.pf-filter-trigger:not(:disabled)')&&!!document.querySelector('.table-pagination')");}
  const entries=()=>network.filter(x=>x.page===p.name&&x.path==='/api/v1/maintenance/'+resource&&x.status===200);
  await filters(p);await multi(p,'Trạng thái '+label,statuses);
  if(index)await multi(p,'Mức ưu tiên '+label,['Thấp','Cao']);
  await evaluate(p,"document.querySelector('.pf-filter-apply').click()");
  for(let i=0;i<200&&new URLSearchParams(entries().at(-1)?.query).getAll('statuses').length!==2;i++)await wait(50);
  await until(p,"!document.querySelector('.pf-filter-popover:popover-open')");await until(p,`document.querySelector('.table-footer')?.textContent.includes('${count}')`);
  assert.equal(new URLSearchParams(entries().at(-1).query).getAll('statuses').length,2);
  if(index)assert.equal(new URLSearchParams(entries().at(-1).query).getAll('priorities').length,2);
  const before=entries().length;await evaluate(p,"document.querySelector('.table-pagination button[aria-label=\"Trang sau\"]').click()");await until(p,"document.querySelector('.table-pagination .page-number')?.getAttribute('aria-label')?.startsWith('Trang 2 trên')");assert.ok(entries().length>before);assert.equal(new URLSearchParams(entries().at(-1).query).getAll('statuses').length,2);
  await visual.verifyDirectory({...h,kind:'maintenance-'+resource,listPath:'/api/v1/maintenance/'+resource,fieldCount:fields});
  scenarios.push('manager-maintenance-'+resource+'-real-multi-pagination-draft-calendar-visual');
 }
};
