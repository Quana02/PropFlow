const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { test } = require('node:test');
const geometry = import('data:text/javascript;base64,' + fs.readFileSync(path.join(__dirname, '../../src/PropFlow.Web.Client/wwwroot/js/parking-shapes.js')).toString('base64'));
test('smart guides align two distant walls and report their physical gap without changing references',async()=>{
  const g=await geometry,a={id:'left',kind:'WALL',x:0,y:100,width:80,height:200,rotation:0},b={id:'right',kind:'WALL',x:500,y:103,width:80,height:200,rotation:0},before=structuredClone([a,b]);
  const result=g.smartPlacement([b],[a,b],1);assert.equal(result.shapes[0].y,100);assert.equal(result.shapes[0].x,500);assert.ok(result.guides.some(s=>s.axis==='y'&&s.position===100));assert.equal(result.distances.find(d=>d.axis==='x').gap,420);assert.deepEqual([a,b],before);
});
test('guides support centre and edge alignment with a constant screen pixel threshold at different zooms',async()=>{
  const g=await geometry,ref={id:'r',kind:'RECT',x:100,y:100,width:100,height:100,rotation:0},moving={id:'m',kind:'RECT',x:500,y:128,width:60,height:50,rotation:0};
  const centre=g.smartPlacement([moving],[ref],1);assert.equal(centre.shapes[0].y,125);assert.ok(centre.guides.some(s=>s.position===150));
  assert.equal(g.smartPlacement([{...moving,x:143,y:500}],[ref],1).shapes[0].x,140);
  const zoomOut=g.smartPlacement([{...moving,y:120,height:100}],[ref],.25);assert.equal(zoomOut.shapes[0].y,100);
  const zoomIn=g.smartPlacement([{...moving,y:102,height:100}],[ref],4,{fallbackY:1});assert.equal(zoomIn.shapes[0].y,103);assert.equal(zoomIn.guides.length,0);
});
test('equal spacing works between neighbours and when extending a row',async()=>{
  const g=await geometry,rect=(id,x)=>({id,kind:'RECT',x,y:100,width:100,height:100,rotation:0});
  const middle=g.smartPlacement([rect('m',203)],[rect('a',0),rect('b',400)],1);assert.equal(middle.shapes[0].x,200);assert.deepEqual(middle.distances.filter(d=>d.axis==='x').map(d=>d.gap),[100,100]);assert.ok(middle.distances.filter(d=>d.axis==='x').every(d=>d.equal));
  const end=g.smartPlacement([rect('m',403)],[rect('a',0),rect('b',200)],1);assert.equal(end.shapes[0].x,400);assert.deepEqual(end.distances.filter(d=>d.axis==='x').map(d=>d.gap),[100,100]);
});
test('smart group placement preserves internal layout and ignores the original selected objects',async()=>{
  const g=await geometry,moving=[{id:'a',kind:'RECT',x:500,y:103,width:80,height:200,rotation:0},{id:'b',kind:'DIVIDER',x:600,y:123,width:40,height:100,rotation:0}],ref={id:'r',kind:'RECT',x:0,y:100,width:80,height:200,rotation:0};
  const result=g.smartPlacement(moving,[ref,...moving.map(s=>({...s,x:s.x+2}))],1);assert.equal(result.shapes[0].y,100);assert.equal(result.shapes[1].y,120);assert.equal(result.shapes[1].x-result.shapes[0].x,100);assert.deepEqual(result.shapes.map(s=>s.id),['a','b']);
  const isolated=g.smartPlacement(moving,moving,1,{fallbackX:4,fallbackY:7});assert.equal(isolated.guides.length,0);assert.equal(isolated.distances.length,0);assert.equal(isolated.shapes[0].x,504);
});
test('all editable entity kinds including rotated symbols can use the same guides',async()=>{
  const g=await geometry;for(const kind of Object.keys(g.shapeNames)){const shape={id:'m',kind,label:'',...g.drawnGeometry(kind,{x:200,y:200},{x:280,y:320},1600,1000,true),rotation:23},b=g.shapeBounds(shape),ref={id:'r',kind:'RECT',x:1000,y:b.top-3,width:80,height:b.bottom-b.top,rotation:0};const result=g.smartPlacement([shape],[ref],1);assert.ok(Math.abs(g.shapeBounds(result.shapes[0]).top-ref.y)<1e-8,kind);assert.ok(result.guides.some(s=>s.axis==='y'),kind);assert.equal(result.shapes[0].rotation,23);}
});
test('rotating a mixed group transforms every world point around one pivot and preserves layout and metadata',async()=>{
  const g=await geometry,shapes=[{id:'t',kind:'DIVIDER',label:'',x:120,y:160,width:80,height:180,rotation:25},{id:'stall',kind:'SPACE',label:'P-001',x:340,y:130,width:100,height:200,rotation:10,needsReview:true,outline:[{x:0,y:0},{x:1,y:.1},{x:1,y:1},{x:0,y:.9}]},{id:'curve',kind:'CURVE',label:'',x:200,y:400,width:200,height:100,rotation:30,controlPoints:[{x:0,y:1},{x:.5,y:0},{x:1,y:1}]},{id:'gate',kind:'GATE',label:'Cổng',x:500,y:420,width:150,height:80,rotation:0}],before=structuredClone(shapes),frame=g.groupFrame(shapes);
  for(const degrees of [-90,90,180,37.5,360]){const result=g.rotatedShapes(shapes,1600,1000,degrees,true);assert.equal(result.error,undefined);for(let i=0;i<shapes.length;i++){const a=shapes[i],b=result.shapes[i];assert.equal(b.id,a.id);assert.equal(b.label,a.label);assert.equal(b.width,a.width);assert.equal(b.height,a.height);assert.equal(b.rotation,g.normalizeAngle(a.rotation+degrees));assert.deepEqual(b.controlPoints,a.controlPoints);assert.deepEqual(b.outline,a.outline);for(const local of[{x:0,y:0},{x:1,y:1},{x:.5,y:.5},...(a.controlPoints||[])]){const p=g.worldPoint(a,local),actual=g.worldPoint(b,local),angle=degrees*Math.PI/180,x=frame.cx+(p.x-frame.cx)*Math.cos(angle)-(p.y-frame.cy)*Math.sin(angle),y=frame.cy+(p.x-frame.cx)*Math.sin(angle)+(p.y-frame.cy)*Math.cos(angle);assert.ok(Math.hypot(actual.x-x,actual.y-y)<1e-8);}}}assert.deepEqual(shapes,before);
});
test('group rotation rejects invalid or out-of-bounds results atomically and reverses a full turn',async()=>{
  const g=await geometry,shapes=[{id:'a',kind:'RECT',label:'',x:10,y:10,width:80,height:40,rotation:0},{id:'b',kind:'RECT',label:'',x:150,y:10,width:80,height:40,rotation:0}],before=structuredClone(shapes);
  assert.ok(g.rotatedShapes(shapes,300,100,90).error);assert.ok(g.rotatedShapes(shapes,300,100,NaN,true).error);assert.ok(g.rotatedShapes([shapes[0]],300,100,90,true).error);assert.deepEqual(shapes,before);assert.deepEqual(g.rotatedShapes(shapes,300,100,360,true).shapes,shapes);
  const pivot={x:120,y:30};let current=shapes;for(let i=0;i<4;i++)current=g.rotatedShapes(current,300,100,90,true,pivot).shapes;for(let i=0;i<shapes.length;i++){assert.ok(Math.abs(current[i].x-shapes[i].x)<1e-8);assert.ok(Math.abs(current[i].y-shapes[i].y)<1e-8);assert.equal(current[i].rotation,shapes[i].rotation);}
});
test('aligning a row of overlapping T dividers separates their bars by the requested gap',async()=>{
  const g=await geometry,shapes=Array.from({length:6},(_,i)=>({id:String(i),kind:'DIVIDER',x:100+i*10,y:60+i,width:100,height:300,rotation:0}));
  const result=g.arrangeShapes(shapes,2000,1000,{axis:'y',mode:'align',gap:30});
  assert.equal(result.error,undefined);assert.ok(result.shapes.every(s=>g.shapeBounds(s).top===60));
  assert.deepEqual(g.selectionGaps(result.shapes,'x').map(p=>p.gap),[30,30,30,30,30]);
});
test('spacing two unequal boxes applies an exact edge gap and leaves source geometry untouched',async()=>{
  const g=await geometry,shapes=[{id:'a',kind:'RECT',x:20,y:40,width:80,height:120,rotation:0},{id:'b',kind:'RECT',x:180,y:43,width:60,height:120,rotation:0}],before=structuredClone(shapes);
  for(const axis of ['x','y']){const result=g.arrangeShapes(shapes,500,500,{axis,mode:'space',gap:25});assert.equal(result.error,undefined);assert.equal(g.selectionGaps(result.shapes,axis)[0].gap,25);assert.equal(result.shapes[0][axis],shapes[0][axis]);assert.equal(result.shapes[1][axis==='x'?'y':'x'],shapes[1][axis==='x'?'y':'x']);}
  assert.deepEqual(shapes,before);
});
test('distribution preserves endpoints and gives equal edge gaps for different widths and input order',async()=>{
  const g=await geometry,shapes=[{id:'c',kind:'RECT',x:350,y:20,width:100,height:40,rotation:0},{id:'a',kind:'RECT',x:20,y:20,width:50,height:40,rotation:0},{id:'b',kind:'RECT',x:130,y:20,width:80,height:40,rotation:0}];
  const result=g.arrangeShapes(shapes,500,400,{axis:'x',mode:'distribute'});assert.deepEqual(result.shapes.map(s=>s.id),['c','a','b']);assert.equal(result.shapes[0].x,350);assert.equal(result.shapes[1].x,20);assert.deepEqual(g.selectionGaps(result.shapes,'x').map(p=>p.gap),[100,100]);
});
test('alignment uses rotated bounds, preserves controls, and all layout failures leave originals intact',async()=>{
  const g=await geometry,shapes=[{id:'a',kind:'RECT',x:50,y:60,width:80,height:40,rotation:25},{id:'b',kind:'CURVE',x:210,y:170,width:80,height:40,rotation:0,controlPoints:[{x:0,y:1},{x:.5,y:0},{x:1,y:1}]}],before=structuredClone(shapes);
  const result=g.arrangeShapes(shapes,500,400,{axis:'y',mode:'align'});const bounds=result.shapes.map(g.shapeBounds);assert.ok(Math.abs(bounds[0].top-bounds[1].top)<1e-8);assert.deepEqual(result.shapes[1].controlPoints,shapes[1].controlPoints);assert.equal(result.shapes[0].rotation,25);
  for(const gap of [-1,NaN,Infinity,1000])assert.ok(g.arrangeShapes(shapes,500,400,{axis:'x',mode:'space',gap}).error);
  assert.ok(g.arrangeShapes(shapes,500,400,{axis:'x',mode:'distribute'}).error);assert.deepEqual(shapes,before);
  const overlapping=[0,10,20].map((x,i)=>({id:String(i),kind:'RECT',x,y:20,width:100,height:40,rotation:0}));assert.ok(g.arrangeShapes(overlapping,500,400,{axis:'x',mode:'distribute'}).error);
});
test('measurements require calibration and convert m/cm without inventing a physical size',async()=>{
  const g=await geometry,scale={start:{x:0,y:0},end:{x:300,y:400},length:10,unit:'m',confirmed:false};assert.deepEqual(g.measurementScale({scale}),{factor:null,unit:'m'});scale.confirmed=true;assert.deepEqual(g.measurementScale({scale}),{factor:.02,unit:'m'});assert.deepEqual(g.measurementScale({scale},'cm'),{factor:2,unit:'cm'});assert.equal(25/g.measurementScale({scale}).factor,1250);scale.end=scale.start;assert.deepEqual(g.measurementScale({scale}),{factor:null,unit:'m'});
});
test('free canvas draws, moves, pastes and arranges beyond the old page in all directions',async()=>{
  const g=await geometry;
  for(const kind of ['RECT','SQUARE','WALL_CIRCLE','LINE','CURVE','DIVIDER']){const shape={id:kind,kind,label:'',...g.drawnGeometry(kind,{x:-30000,y:-22000},{x:-30100,y:-22150},1600,1000,true)};assert.ok(g.frameInside(shape,1600,1000,true),kind);assert.ok(!g.frameInside(shape,1600,1000));const moved=g.translatedShapes([shape],1600,1000,60000,50000,true);assert.ok(moved[0].x>10000);const drawing={width:1600,height:1000,shapes:[],infiniteCanvas:true};assert.ok(!g.pasteShapes(drawing,[shape],20,()=>kind+'copy').error);}
  const shapes=[0,1,2].map((i)=>({id:String(i),kind:'DIVIDER',x:1500+i*10,y:100,width:200,height:300,rotation:0}));const arranged=g.arrangeShapes(shapes,1600,1000,{axis:'y',mode:'align',gap:1000,infiniteCanvas:true});assert.ok(!arranged.error);assert.ok(arranged.shapes.at(-1).x>1600);assert.deepEqual(g.selectionGaps(arranged.shapes,'x').map(p=>p.gap),[1000,1000]);assert.ok(!g.frameInside({...shapes[0],x:Infinity},1600,1000,true));assert.ok(!g.frameInside({...shapes[0],x:2e9},1600,1000,true));
});
test('layout allows symbols to overlap but does not use them as parking spacing anchors',async()=>{
  const g=await geometry,shapes=[{id:'a',kind:'DIVIDER',x:100,y:20,width:100,height:300,rotation:0},{id:'b',kind:'DIVIDER',x:110,y:40,width:100,height:300,rotation:0},{id:'label',kind:'LABEL',label:'P',x:110,y:20,width:100,height:50,rotation:0}];
  const result=g.arrangeShapes(shapes,1000,1000,{axis:'y',mode:'align',gap:30});assert.equal(result.shapes[2].x,110);assert.deepEqual(g.selectionGaps(result.shapes,'x').map(p=>p.gap),[30]);
});
test('pasting mixed objects preserves geometry, deep copies controls and assigns unique stall labels', async()=>{
  const g=await geometry,templates=[{id:'old-1',kind:'SPACE',label:'P-001',x:40,y:40,width:40,height:60,rotation:15,needsReview:true,outline:[{x:0,y:0},{x:1,y:0},{x:1,y:1},{x:0,y:1}]},{id:'old-2',kind:'CURVE',label:'',x:120,y:60,width:80,height:60,rotation:30,controlPoints:[{x:0,y:1},{x:.5,y:0},{x:1,y:1}]},{id:'old-3',kind:'SPACE',label:'P-002',x:260,y:60,width:40,height:60,rotation:0}],original=structuredClone(templates);
  const drawing={width:600,height:400,shapes:[...templates,{kind:'SPACE',label:' p-003 ',id:'other'}]},newIds=['new-1','new-2','new-3'];
  const result=g.pasteShapes(drawing,templates,20,()=>newIds.shift());
  assert.equal(result.error,undefined);assert.deepEqual(result.shapes.map(s=>s.id),['new-1','new-2','new-3']);assert.deepEqual(result.shapes.filter(s=>s.kind==='SPACE').map(s=>s.label),['P-004','P-005']);
  for(let i=0;i<templates.length;i++){assert.equal(result.shapes[i].x-templates[i].x,20);assert.equal(result.shapes[i].y-templates[i].y,20);assert.equal(result.shapes[i].rotation,templates[i].rotation);assert.ok(g.frameInside(result.shapes[i],600,400));}
  assert.equal(result.shapes[0].needsReview,true);result.shapes[1].controlPoints[0].x=.3;result.shapes[0].outline[0].x=.2;assert.deepEqual(templates,original);
});
test('group translation clamps the shared offset at rotated boundaries without changing spacing',async()=>{
  const g=await geometry,shapes=[{kind:'RECT',x:40,y:40,width:60,height:30,rotation:45},{kind:'DIVIDER',x:180,y:120,width:40,height:80,rotation:30}];
  for(const delta of [-500,500]){const moved=g.translatedShapes(shapes,300,240,delta,delta);assert.ok(moved);assert.ok(moved.every(s=>g.frameInside(s,300,240)));assert.ok(Math.abs((moved[1].x-moved[0].x)-140)<1e-8);assert.ok(Math.abs((moved[1].y-moved[0].y)-80)<1e-8);}
  assert.equal(g.translatedShapes(shapes,100,100,20,20),null);
});
test('paste rejects an entire batch on capacity or smaller canvas and never changes the draft',async()=>{
  const g=await geometry,shape={id:'a',kind:'RECT',label:'',x:10,y:10,width:80,height:80,rotation:0};
  for(const drawing of [{width:200,height:200,shapes:Array.from({length:999},(_,i)=>({...shape,id:String(i)}))},{width:50,height:50,shapes:[]}]){const before=structuredClone(drawing);assert.ok(g.pasteShapes(drawing,[shape,shape],20,()=> 'unused').error);assert.deepEqual(drawing,before);}
  const drawing={width:200,height:200,shapes:[]};assert.ok(g.pasteShapes(drawing,[],20).error);
});
test('clipboard snapshot can be pasted repeatedly after originals are removed',async()=>{
  const g=await geometry,buffer=[{id:'deleted',kind:'SPACE',label:'P-001',x:10,y:10,width:40,height:60,rotation:0}],drawing={width:400,height:400,shapes:[]};
  const a=g.pasteShapes(drawing,buffer,20,()=> 'first').shapes;drawing.shapes.push(...a);const b=g.pasteShapes(drawing,buffer,40,()=> 'second').shapes;
  assert.equal(a[0].label,'P-001');assert.equal(b[0].label,'P-002');assert.equal(b[0].x-a[0].x,20);assert.equal(buffer[0].id,'deleted');assert.equal(buffer[0].x,10);
});
test('horizontal, vertical and reversed lines retain the exact gesture and stay in the sheet', async () => {
  const g = await geometry;
  for (const [a, b] of [[{x:0,y:0},{x:100,y:0}],[{x:500,y:300},{x:500,y:200}],[{x:140,y:180},{x:10,y:50}]]) {
    const shape = {kind:'LINE', ...g.drawnGeometry('LINE', a, b, 500, 300)};
    assert.deepEqual(g.worldPoint(shape, shape.controlPoints[0]), a);assert.deepEqual(g.worldPoint(shape, shape.controlPoints[1]), b);assert.ok(g.frameInside(shape,500,300));
  }
  assert.equal(g.drawnGeometry('LINE',{x:10,y:10},{x:11,y:11},500,300),null);
});
test('square and round obstacles remain square even when drawn towards a boundary', async () => {
  const g = await geometry;
  for (const kind of ['SQUARE','WALL_SQUARE','WALL_CIRCLE']) {
    const shape = {...g.drawnGeometry(kind,{x:490,y:20},{x:499,y:100},500,300),kind};
    assert.equal(shape.width,shape.height);assert.ok(g.frameInside(shape,500,300));
  }
});
test('T tail grows along the rotated axis while the crossbar stays fixed for every angle', async () => {
  const g = await geometry;
  for (let rotation = 0; rotation < 360; rotation += 15) {
    const shape = {kind:'DIVIDER',x:200,y:200,width:50,height:80,rotation}, anchor = g.worldPoint(shape,{x:.5,y:0});
    const a=rotation*Math.PI/180,p={x:anchor.x-Math.sin(a)*160,y:anchor.y+Math.cos(a)*160};
    const after={...shape,...g.extendDivider(shape,p)}, actual=g.worldPoint(after,{x:.5,y:0});
    assert.ok(Math.abs(after.height-160)<1e-8);assert.ok(Math.hypot(actual.x-anchor.x,actual.y-anchor.y)<1e-8);
  }
  assert.equal(g.normalizeAngle(360),0);assert.equal(g.normalizeAngle(-15),345);
  assert.equal(g.extendDivider({x:20,y:20,width:50,height:80,rotation:0},{x:45,y:219.999987},10).height,200);
});
test('quadratic curve keeps an editable bend and all symbols produce finite paths', async () => {
  const g=await geometry,curve={kind:'CURVE',...g.drawnGeometry('CURVE',{x:20,y:150},{x:200,y:150},500,300)};
  assert.equal(curve.controlPoints.length,3);assert.match(g.shapePath(curve),/Q/);assert.ok(g.frameInside(curve,500,300));
  for(const kind of ['DIVIDER','GATE','ENTRANCE','EXIT'])assert.doesNotMatch(g.shapePath({kind,x:20,y:30,width:80,height:60,rotation:0}),/NaN|undefined|Infinity/);
});
test('region selection includes whole objects in either drag direction and keeps partially crossed objects', async () => {
  const g=await geometry, shapes=[{id:'inside',kind:'RECT',x:20,y:20,width:40,height:40,rotation:0},{id:'crossed',kind:'RECT',x:80,y:20,width:60,height:40,rotation:0},{id:'outside',kind:'RECT',x:200,y:20,width:40,height:40,rotation:0},{id:'legacy',kind:'TRACE',x:20,y:20,width:40,height:40,rotation:0}];
  assert.deepEqual(g.selectInRectangle(shapes,{x:0,y:0},{x:100,y:100}),['inside']);
  assert.deepEqual(g.selectInRectangle(shapes,{x:100,y:100},{x:0,y:0}),['inside']);
  assert.deepEqual(g.selectInRectangle(shapes,{x:0,y:0},{x:0,y:100}),[]);
  assert.deepEqual(g.selectInRectangle(shapes,{x:NaN,y:0},{x:100,y:100}),[]);
});
test('region uses rotated geometry and visible curve extrema instead of the control handle', async () => {
  const g=await geometry;
  const rotated={id:'rotated',kind:'RECT',x:25,y:25,width:50,height:50,rotation:45};
  assert.deepEqual(g.selectInRectangle([rotated],{x:25,y:25},{x:75,y:75}),[]);
  assert.deepEqual(g.selectInRectangle([rotated],{x:10,y:10},{x:90,y:90}),['rotated']);
  const curve={id:'curve',kind:'CURVE',x:0,y:0,width:100,height:100,rotation:0,controlPoints:[{x:0,y:1},{x:.5,y:0},{x:1,y:1}]};
  assert.deepEqual(g.selectInRectangle([curve],{x:0,y:49},{x:100,y:101}),['curve']);
  const circle={id:'circle',kind:'WALL_CIRCLE',x:20,y:20,width:40,height:40,rotation:45};
  assert.deepEqual(g.selectInRectangle([circle],{x:19,y:19},{x:61,y:61}),['circle']);
});
