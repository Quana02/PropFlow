// Numeric diagram geometry only; no arbitrary SVG or executable content is stored in drawings.
export const shapeNames = { SPACE: 'Ô đỗ', LANE: 'Lối đi', LINE: 'Đường thẳng', CURVE: 'Đường cong', RECT: 'Chữ nhật', SQUARE: 'Hình vuông', DIVIDER: 'Phân cách chữ T', WALL: 'Vật cản chữ nhật', WALL_SQUARE: 'Vật cản vuông', WALL_CIRCLE: 'Vật cản tròn', ENTRANCE: 'Lối vào', EXIT: 'Lối ra', GATE: 'Cổng', LABEL: 'Chú thích' };
export const squareKinds = new Set(['SQUARE', 'WALL_SQUARE', 'WALL_CIRCLE']);
export const pathKinds = new Set(['LINE', 'CURVE', 'DIVIDER', 'GATE', 'ENTRANCE', 'EXIT']);
export const normalizeAngle = value => ((value % 360) + 360) % 360;
export const overlayKinds = new Set(['LABEL','ENTRANCE','EXIT','GATE','LANE']);
export const maxCoordinate = 1e9;
export function worldPoint(shape, point) {
  const a = shape.rotation * Math.PI / 180, x = (point.x - .5) * shape.width, y = (point.y - .5) * shape.height;
  return { x: shape.x + shape.width / 2 + x * Math.cos(a) - y * Math.sin(a), y: shape.y + shape.height / 2 + x * Math.sin(a) + y * Math.cos(a) };
}
export function pointFrame(points) {
  const minX = Math.min(...points.map(p => p.x)), minY = Math.min(...points.map(p => p.y));
  const width = Math.max(4, Math.max(...points.map(p => p.x)) - minX), height = Math.max(4, Math.max(...points.map(p => p.y)) - minY);
  // Give horizontal/vertical strokes a small frame while keeping their endpoints exactly on the gesture.
  const x = minX - (width - (Math.max(...points.map(p => p.x)) - minX)) / 2;
  const y = minY - (height - (Math.max(...points.map(p => p.y)) - minY)) / 2;
  return { x, y, width, height, rotation: 0, controlPoints: points.map(p => ({ x: (p.x - x) / width, y: (p.y - y) / height })) };
}
export function drawnGeometry(kind, start, end, canvasWidth, canvasHeight, infinite = false) {
  if (kind === 'LINE' || kind === 'CURVE') {
    if (Math.hypot(end.x - start.x, end.y - start.y) < 4) return null;
    const points = [start, end];
    if (kind === 'CURVE') {
      const length = Math.hypot(end.x - start.x, end.y - start.y), bend = Math.min(100, length * .4);
      const x=(start.x+end.x)/2+(end.y-start.y)/length*bend,y=(start.y+end.y)/2-(end.x-start.x)/length*bend;
      points.splice(1,0,{x:infinite?x:Math.max(2,Math.min(canvasWidth-2,x)),y:infinite?y:Math.max(2,Math.min(canvasHeight-2,y))});
    }
    return boundedPointFrame(points, canvasWidth, canvasHeight, infinite);
  }
  let width = Math.abs(end.x - start.x), height = Math.abs(end.y - start.y);
  if (squareKinds.has(kind)) {
    const size = infinite?Math.max(width,height):Math.min(Math.max(width, height), end.x < start.x ? start.x : canvasWidth - start.x, end.y < start.y ? start.y : canvasHeight - start.y);
    width = height = size;
  }
  if (width < 4 || height < 4) return null;
  return { x: end.x < start.x ? start.x - width : start.x, y: end.y < start.y ? start.y - height : start.y, width, height, rotation: 0 };
}
export function boundedPointFrame(points, canvasWidth, canvasHeight, infinite = false) {
  const frame = pointFrame(points);
  if(infinite)return frame;
  frame.x = Math.max(0, Math.min(canvasWidth - frame.width, frame.x));
  frame.y = Math.max(0, Math.min(canvasHeight - frame.height, frame.y));
  frame.controlPoints = points.map(p => ({ x: (p.x - frame.x) / frame.width, y: (p.y - frame.y) / frame.height }));
  return frame;
}
export function shapePath(shape) {
  const { x, y, width: w, height: h } = shape;
  if (shape.kind === 'LINE' || shape.kind === 'CURVE') {
    const points = shape.controlPoints.map(p => ({ x: x + p.x * w, y: y + p.y * h }));
    return shape.kind === 'LINE' ? `M${points[0].x} ${points[0].y}L${points[1].x} ${points[1].y}` : `M${points[0].x} ${points[0].y}Q${points[1].x} ${points[1].y} ${points[2].x} ${points[2].y}`;
  }
  if (shape.kind === 'DIVIDER') return `M${x} ${y}H${x + w}M${x + w / 2} ${y}V${y + h}`;
  if (shape.kind === 'GATE') return `M${x + w * .08} ${y + h}V${y + h * .15}H${x + w * .18}V${y + h}ZM${x + w * .82} ${y + h}V${y + h * .65}H${x + w * .92}V${y + h}ZM${x + w * .18} ${y + h * .18}L${x + w * .88} ${y}L${x + w * .9} ${y + h * .12}L${x + w * .18} ${y + h * .3}Z`;
  const points = [[0, .32], [.62, .32], [.62, 0], [1, .5], [.62, 1], [.62, .68], [0, .68]];
  return points.map(([u, v], i) => `${i ? 'L' : 'M'}${x + (shape.kind === 'EXIT' ? 1 - u : u) * w} ${y + v * h}`).join('') + 'Z';
}
export function extendDivider(shape, pointer, step = 0) {
  const anchor = worldPoint(shape, { x: .5, y: 0 }), angle = shape.rotation * Math.PI / 180, axis = { x: -Math.sin(angle), y: Math.cos(angle) };
  const projected = (pointer.x - anchor.x) * axis.x + (pointer.y - anchor.y) * axis.y;
  const height = Math.max(4, step > 0 ? Math.round(projected / step) * step : projected);
  return { height, x: anchor.x + axis.x * height / 2 - shape.width / 2, y: anchor.y + axis.y * height / 2 - height / 2 };
}
export function frameInside(shape, width, height, infinite = false) {
  if (![shape.x, shape.y, shape.width, shape.height, shape.rotation].every(Number.isFinite) || shape.width < 4 || shape.height < 4) return false;
  if (shape.controlPoints && Math.hypot((shape.controlPoints[0].x - shape.controlPoints.at(-1).x) * shape.width, (shape.controlPoints[0].y - shape.controlPoints.at(-1).y) * shape.height) < 4) return false;
  return [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }].map(p => worldPoint(shape, p)).every(p => infinite?Number.isFinite(p.x)&&Number.isFinite(p.y)&&Math.abs(p.x)<=maxCoordinate&&Math.abs(p.y)<=maxCoordinate:p.x >= -.01 && p.y >= -.01 && p.x <= width + .01 && p.y <= height + .01);
}
export function selectInRectangle(shapes, start, end) {
  if (![start.x, start.y, end.x, end.y].every(Number.isFinite)) return [];
  const left = Math.min(start.x, end.x), right = Math.max(start.x, end.x), top = Math.min(start.y, end.y), bottom = Math.max(start.y, end.y);
  if (right <= left || bottom <= top) return [];
  return shapes.filter(shape => {
    if (shape.kind === 'TRACE') return false;
    const b=shapeBounds(shape);
    return b.left>=left-1e-6&&b.right<=right+1e-6&&b.top>=top-1e-6&&b.bottom<=bottom+1e-6;
  }).map(shape => shape.id);
}
export function shapeBounds(shape) {
    let points;
    if (shape.kind === 'WALL_CIRCLE') points = [{x:shape.x,y:shape.y+shape.height/2},{x:shape.x+shape.width,y:shape.y+shape.height/2},{x:shape.x+shape.width/2,y:shape.y},{x:shape.x+shape.width/2,y:shape.y+shape.height}];
    else if (shape.controlPoints) {
      const controls = shape.controlPoints.map(p => worldPoint(shape, p));
      points = [controls[0], controls.at(-1)];
      if (shape.kind === 'CURVE') for (const axis of ['x', 'y']) {
        const [a,b,c] = controls.map(p => p[axis]), denominator = a - 2 * b + c, t = (a - b) / denominator;
        if (Math.abs(denominator) > 1e-9 && t > 0 && t < 1) points.push({x:(1-t)**2*controls[0].x+2*(1-t)*t*controls[1].x+t*t*controls[2].x,y:(1-t)**2*controls[0].y+2*(1-t)*t*controls[1].y+t*t*controls[2].y});
      }
    } else points = (shape.outline || (shape.kind === 'DIVIDER' ? [{x:0,y:0},{x:1,y:0},{x:.5,y:1}] : [{x:0,y:0},{x:1,y:0},{x:1,y:1},{x:0,y:1}])).map(p => worldPoint(shape, p));
    return {left:Math.min(...points.map(p=>p.x)),right:Math.max(...points.map(p=>p.x)),top:Math.min(...points.map(p=>p.y)),bottom:Math.max(...points.map(p=>p.y))};
}
export function selectionGaps(shapes,axis) {
  if(!['x','y'].includes(axis))return [];
  const start=axis==='x'?'left':'top',end=axis==='x'?'right':'bottom';
  const ordered=shapes.filter(s=>!overlayKinds.has(s.kind)).map(shape=>({shape,bounds:shapeBounds(shape)})).sort((a,b)=>a.bounds[start]-b.bounds[start]);
  return ordered.slice(1).map((next,i)=>({from:ordered[i],to:next,gap:next.bounds[start]-ordered[i].bounds[end]}));
}
export function groupFrame(shapes) {
  if(!shapes.length)return null;
  const bounds=shapes.map(shapeBounds),x=Math.min(...bounds.map(b=>b.left)),y=Math.min(...bounds.map(b=>b.top));
  const width=Math.max(...bounds.map(b=>b.right))-x,height=Math.max(...bounds.map(b=>b.bottom))-y;
  return [x,y,width,height].every(Number.isFinite)?{x,y,width,height,cx:x+width/2,cy:y+height/2}:null;
}
// One rigid transform: rotate centres and local angles about the same world pivot.
export function rotatedShapes(shapes,width,height,degrees,infinite=false,pivot=null) {
  const frame=groupFrame(shapes);pivot??=frame?{x:frame.cx,y:frame.cy}:null;
  if(shapes.length<2||!Number.isFinite(degrees)||!pivot||![pivot.x,pivot.y].every(Number.isFinite))
    return {error:'Chọn ít nhất hai hình và nhập góc xoay hợp lệ.'};
  const delta=normalizeAngle(degrees),angle=delta*Math.PI/180,c=Math.cos(angle),s=Math.sin(angle);
  const result=shapes.map(shape=>{if(delta===0)return structuredClone(shape);const dx=shape.x+shape.width/2-pivot.x,dy=shape.y+shape.height/2-pivot.y;return {...structuredClone(shape),x:pivot.x+dx*c-dy*s-shape.width/2,y:pivot.y+dx*s+dy*c-shape.height/2,rotation:normalizeAngle(shape.rotation+delta)};});
  if(!result.every(shape=>frameInside(shape,width,height,infinite)))return {error:'Không thể xoay nhóm tại vị trí này. Hãy kiểm tra vị trí và kích thước các hình.'};
  return {shapes:result};
}
export function measurementScale(drawing,unit='m') {
  const s=drawing.scale,distance=s?Math.hypot(s.end.x-s.start.x,s.end.y-s.start.y):0;
  const metres={mm:.001,cm:.01,m:1};unit=unit==='cm'?'cm':'m';
  return s?.confirmed&&Number.isFinite(s.length)&&s.length>0&&Number.isFinite(distance)&&distance>0&&['mm','cm','m'].includes(s.unit)
    ?{factor:s.length*metres[s.unit]/distance/metres[unit],unit}:{factor:null,unit};
}
// Threshold is in screen pixels so zooming does not change the feel of magnetic guides.
export function smartPlacement(moving,references,scale,{fallbackX=0,fallbackY=0,tolerance=6}={}) {
  const frame=groupFrame(moving),empty={shapes:moving.map(s=>structuredClone(s)),guides:[],distances:[]};
  if(!frame||!Number.isFinite(scale)||scale<=0)return empty;
  const ids=new Set(moving.map(s=>s.id)),refs=references.filter(s=>s.kind!=='TRACE'&&!ids.has(s.id)).map(s=>({id:s.id,...shapeBounds(s)}));
  const box={left:frame.x,right:frame.x+frame.width,top:frame.y,bottom:frame.y+frame.height};
  const limit=tolerance/scale,choices={};
  const neighbours=(b,axis)=>{const lo=axis==='x'?'left':'top',hi=axis==='x'?'right':'bottom',crossLo=axis==='x'?'top':'left',crossHi=axis==='x'?'bottom':'right';const crossing=refs.filter(r=>r[crossHi]>=b[crossLo]-1e-6&&r[crossLo]<=b[crossHi]+1e-6);return {before:crossing.filter(r=>r[hi]<=b[lo]+1e-6).sort((a,z)=>z[hi]-a[hi]),after:crossing.filter(r=>r[lo]>=b[hi]-1e-6).sort((a,z)=>a[lo]-z[lo])};};
  for(const axis of ['x','y']) {
    const lo=axis==='x'?'left':'top',hi=axis==='x'?'right':'bottom',crossLo=axis==='x'?'top':'left',crossHi=axis==='x'?'bottom':'right',anchors=b=>[b[lo],(b[lo]+b[hi])/2,b[hi]],candidates=[];
    const offer=c=>{if(Number.isFinite(c.delta)&&Math.abs(c.delta)<=limit)candidates.push(c);};
    for(const ref of refs)for(const [i,a]of anchors(box).entries())for(const [j,b]of anchors(ref).entries())offer({axis,delta:b-a,position:b,ref,priority:i===j?(i===1?1:0):2,distance:Math.abs((box[crossLo]+box[crossHi])-(ref[crossLo]+ref[crossHi]))});
    const {before,after}=neighbours(box,axis),size=box[hi]-box[lo];
    if(before[0]&&after[0]&&after[0][lo]-before[0][hi]>=size)offer({axis,kind:'equal',delta:(before[0][hi]+after[0][lo]-size)/2-box[lo],priority:3,distance:0});
    if(before.length>=2){const gap=before[0][lo]-before[1][hi];if(gap>=0)offer({axis,kind:'equal',delta:before[0][hi]+gap-box[lo],priority:3,distance:0,baseline:[before[1],before[0]]});}
    if(after.length>=2){const gap=after[1][lo]-after[0][hi];if(gap>=0)offer({axis,kind:'equal',delta:after[0][lo]-gap-size-box[lo],priority:3,distance:0,baseline:[after[0],after[1]]});}
    candidates.sort((a,b)=>{const difference=Math.abs(a.delta)-Math.abs(b.delta);return Math.abs(difference)>1e-7?difference:a.priority-b.priority||a.distance-b.distance;});choices[axis]=candidates[0];
  }
  const dx=choices.x?.delta??fallbackX,dy=choices.y?.delta??fallbackY,shapes=moving.map(s=>({...structuredClone(s),x:s.x+dx,y:s.y+dy}));
  const final={left:box.left+dx,right:box.right+dx,top:box.top+dy,bottom:box.bottom+dy},guides=[],distances=[];
  const dimension=(a,b,axis,equal=false)=>{const start=axis==='x'?a.right:a.bottom,end=axis==='x'?b.left:b.top;return {axis,start,end,gap:end-start,cross:Math.max(axis==='x'?a.bottom:a.right,axis==='x'?b.bottom:b.right),equal};};
  for(const axis of ['x','y']) {
    const choice=choices[axis],{before,after}=neighbours(final,axis);
    if(choice&&choice.kind!=='equal'){const r=choice.ref;guides.push({axis,position:choice.position,from:Math.min(axis==='x'?final.top:final.left,axis==='x'?r.top:r.left),to:Math.max(axis==='x'?final.bottom:final.right,axis==='x'?r.bottom:r.right)});}
    if(before[0])distances.push(dimension(before[0],final,axis,choice?.kind==='equal'));
    if(after[0])distances.push(dimension(final,after[0],axis,choice?.kind==='equal'));
    if(choice?.baseline)distances.push(dimension(...choice.baseline,axis,true));
  }
  return {shapes,guides,distances};
}
export function arrangeShapes(shapes,width,height,{axis,mode,gap=0,infiniteCanvas=false}) {
  if(!['x','y'].includes(axis)||!['align','space','distribute'].includes(mode)||shapes.length<2)
    return {error:'Chọn ít nhất hai đối tượng để căn chỉnh.'};
  const participants=mode==='align'?shapes:shapes.filter(s=>!overlayKinds.has(s.kind));
  if(participants.length<2)return {error:'Chọn ít nhất hai hình bố trí; ký hiệu và chú thích không dùng để chia khe hở.'};
  if(mode==='distribute'&&participants.length<3)return {error:'Chọn ít nhất ba hình bố trí để giãn đều; với hai hình, hãy nhập khe hở.'};
  if(mode==='space'&&(!Number.isFinite(gap)||gap<0))return {error:'Khe hở phải là số hữu hạn, không âm.'};
  const start=axis==='x'?'left':'top',end=axis==='x'?'right':'bottom';
  const ordered=participants.map(shape=>({shape,b:shapeBounds(shape)})).sort((a,b)=>a.b[start]-b.b[start]);
  if(mode==='distribute') {
    gap=(ordered.at(-1).b[end]-ordered[0].b[start]-ordered.reduce((sum,o)=>sum+o.b[end]-o.b[start],0))/(ordered.length-1);
    if(gap< -1e-6)return {error:'Không đủ khoảng trống để giãn đều mà không chồng hình. Hãy nhập khe hở hoặc di chuyển hai hình ngoài cùng.'};
    gap=Math.max(0,gap);
  }
  let cursor=ordered[0].b[start];const moved=new Map();
  for(const {shape,b} of ordered) {
    const after={...structuredClone(shape),[axis]:shape[axis]+cursor-b[start]};moved.set(shape.id,after);
    if(mode!=='align')cursor+=b[end]-b[start]+gap;
  }
  const result=shapes.map(s=>moved.get(s.id)||structuredClone(s));
  if(mode==='align') {
    if(!Number.isFinite(gap)||gap<0)return {error:'Khe hở phải là số hữu hạn, không âm.'};
    const along=axis==='y'?'x':'y',lo=along==='x'?'left':'top',hi=along==='x'?'right':'bottom';
    const objects=result.filter(s=>!overlayKinds.has(s.kind)).sort((a,b)=>shapeBounds(a)[lo]-shapeBounds(b)[lo]);
    let edge=null;
    for(const s of objects){const b=shapeBounds(s);if(edge!==null)s[along]+=edge+gap-b[lo];edge=shapeBounds(s)[hi];}
  }
  if(!result.every(s=>frameInside(s,width,height,infiniteCanvas)))return {error:infiniteCanvas?'Khoảng cách vượt khả năng xử lý. Hãy kiểm tra đơn vị m/cm.':'Không đủ chỗ trong khung cho khoảng cách này. Hãy giảm khe hở hoặc mở không gian vẽ tự do.'};
  return {shapes:result};
}
// Translate the entire selection by one shared offset, including rotated frames.
export function translatedShapes(shapes, width, height, dx, dy, infinite = false) {
  if (!shapes.length || ![width, height, dx, dy].every(Number.isFinite)) return null;
  const points = shapes.flatMap(s => [{x:0,y:0},{x:1,y:0},{x:1,y:1},{x:0,y:1}].map(p => worldPoint(s,p)));
  const left = Math.min(...points.map(p=>p.x)), right = Math.max(...points.map(p=>p.x));
  const top = Math.min(...points.map(p=>p.y)), bottom = Math.max(...points.map(p=>p.y));
  if (!infinite&&(right-left>width+1e-6 || bottom-top>height+1e-6)) return null;
  if(!infinite){dx = Math.max(-left,Math.min(width-right,dx)); dy = Math.max(-top,Math.min(height-bottom,dy));}
  const moved = shapes.map(s=>({...structuredClone(s),x:s.x+dx,y:s.y+dy}));
  return moved.every(s=>frameInside(s,width,height,infinite)) ? moved : null;
}
export function nextSpaceLabel(shapes) {
  const names = new Set(shapes.filter(s=>s.kind==='SPACE').map(s=>s.label.trim().normalize('NFC').toUpperCase()));
  for (let i=1;i<=1001;i++) { const label='P-'+String(i).padStart(3,'0'); if(!names.has(label)) return label; }
}
export function pasteShapes(drawing, templates, offset, newId = ()=>crypto.randomUUID()) {
  const source = templates.filter(s=>s.kind!=='TRACE');
  if (!source.length) return {error:'Chọn đối tượng rồi bấm Sao chép trước khi dán.'};
  if (drawing.shapes.length+source.length>6000 || drawing.shapes.filter(s=>s.kind!=='TRACE').length+source.length>1000)
    return {error:'Không đủ chỗ để dán cả nhóm. Bản vẽ cho phép tối đa 1.000 đối tượng chỉnh sửa.'};
  const shapes=translatedShapes(source,drawing.width,drawing.height,offset,offset,drawing.infiniteCanvas);
  if(!shapes) return {error:'Nhóm sao chép không vừa khung bản vẽ. Hãy tăng kích thước khung trước khi dán.'};
  const used=[...drawing.shapes];
  for(const shape of shapes) { shape.id=newId(); if(shape.kind==='SPACE')shape.label=nextSpaceLabel(used); used.push(shape); }
  return {shapes};
}
